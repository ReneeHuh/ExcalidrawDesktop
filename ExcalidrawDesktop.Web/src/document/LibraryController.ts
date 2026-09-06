type Item = { id: string; [key: string]: unknown };
type Snapshot = { content: string; revision: string };
type LibraryHost = {
  loadLibrary: () => Promise<({ status: "loaded" } & Snapshot) | { status: "unavailable" }>;
  saveLibrary: (content: string, expectedRevision: string) => Promise<{ status: "saved" } & Snapshot>;
};

const parse = (content: string): Item[] => {
  const items: unknown = JSON.parse(content);
  if (!Array.isArray(items) || items.some(item => !item || typeof item.id !== "string"))
    throw new Error("The shared library is invalid.");
  return items;
};

/** Applies the local delta to a newer shared library without erasing other tabs' additions. */
export const mergeLibraryChanges = (base: Item[], local: Item[], remote: Item[]): Item[] => {
  const result = new Map(remote.map(item => [item.id, item]));
  const previous = new Map(base.map(item => [item.id, item]));
  const desired = new Map(local.map(item => [item.id, item]));
  for (const item of base) if (!desired.has(item.id)) result.delete(item.id);
  for (const item of local) {
    if (JSON.stringify(item) !== JSON.stringify(previous.get(item.id))) result.set(item.id, item);
  }
  return [...result.values()];
};

export class LibraryController {
  private base?: Snapshot;
  private desired?: string;
  private applying?: string;
  private disposed = false;
  private writing = false;
  private remote?: Snapshot;
  private failed = false;
  private conflicts = 0;

  public constructor(private readonly host: LibraryHost,
    private readonly apply: (items: Item[]) => Promise<unknown>,
    private readonly onFailure: () => void,
    private readonly onUnsavedChanged: (hasUnsavedChanges: boolean) => void = () => {}) {}

  public get isSaving() { return this.writing; }
  public get hasUnsavedChanges() { return this.desired !== undefined; }

  public async start() {
    try {
      const result = await this.host.loadLibrary();
      if (this.disposed) return;
      if (result.status !== "loaded") throw new Error("Library unavailable");
      const latest = this.remote ?? result;
      this.remote = undefined;
      parse(latest.content);
      this.base = latest;
      if (this.desired !== undefined) {
        this.desired = JSON.stringify(mergeLibraryChanges([], parse(this.desired), parse(latest.content)));
        await this.applyContent(this.desired);
        void this.flush();
      } else {
        await this.applyContent(latest.content);
      }
    } catch {
      if (!this.disposed) this.onFailure();
    }
  }

  public changed(items: readonly unknown[]) {
    const content = JSON.stringify(items);
    // Excalidraw reports its initial empty library and reports updateLibrary
    // results through the same callback as user changes.
    if (content === this.applying || (content === this.base?.content && this.desired === undefined) ||
      (!this.base && content === "[]")) return;
    this.desired = content;
    this.onUnsavedChanged(true);
    this.failed = false;
    this.conflicts = 0;
    void this.flush();
  }

  public receive(snapshot: Snapshot) {
    if (this.disposed || snapshot.revision === this.base?.revision) return;
    try { parse(snapshot.content); } catch { this.onFailure(); return; }
    if (this.writing || this.desired !== undefined || !this.base) {
      this.remote = snapshot;
      return;
    }
    this.base = snapshot;
    void this.applyContent(snapshot.content).catch(() => this.onFailure());
  }

  public retry() {
    this.failed = false;
    this.conflicts = 0;
    if (!this.base) void this.start();
    else void this.flush();
  }

  public dispose() { this.disposed = true; }

  private async applyContent(content: string) {
    if (this.disposed) return;
    this.applying = JSON.stringify(parse(content));
    try { await this.apply(parse(content)); }
    finally { this.applying = undefined; }
  }

  private async flush() {
    if (this.disposed || this.writing || this.failed || !this.base || this.desired === undefined) return;
    this.writing = true;
    const base = this.base;
    const content = this.desired;
    try {
      const saved = await this.host.saveLibrary(content, base.revision);
      if (this.disposed) return;
      this.base = saved;
      this.conflicts = 0;
      if (this.desired === content) this.desired = undefined;
    } catch (error) {
      if (this.disposed) return;
      if (typeof error === "object" && error !== null && "code" in error &&
        error.code === "LibraryConflict" && this.conflicts++ < 3) {
        try {
          const latest = await this.host.loadLibrary();
          if (this.disposed) return;
          if (latest.status !== "loaded") throw new Error("Library unavailable");
          this.desired = JSON.stringify(mergeLibraryChanges(parse(base.content), parse(this.desired ?? content), parse(latest.content)));
          this.base = latest;
          await this.applyContent(this.desired);
        } catch { this.failed = true; this.onFailure(); }
      } else {
        this.failed = true;
        this.onFailure();
      }
    } finally {
      this.writing = false;
      if (!this.disposed) this.onUnsavedChanged(this.hasUnsavedChanges);
      if (this.desired !== undefined) void this.flush();
      else if (this.remote) {
        const remote = this.remote;
        this.remote = undefined;
        // Ignore an older broadcast sent during our own successful write.
        if (remote.revision !== base.revision && remote.revision !== this.base?.revision)
          void this.start();
      }
    }
  }
}
