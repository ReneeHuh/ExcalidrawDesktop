type Item = { id: string; [key: string]: unknown };
type Snapshot = { content: string; revision: string };
type LibraryHost = {
  loadLibrary: (retryRepair?: boolean) => Promise<({ status: "loaded" } & Snapshot) | { status: "unavailable" }>;
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
  private loading = false;
  private needsRepair = false;
  private remote?: Snapshot;
  private failed = false;
  private conflicts = 0;

  public constructor(private readonly host: LibraryHost,
    private readonly apply: (items: Item[]) => Promise<unknown>,
    private readonly onFailure: () => void,
    private readonly onUnsavedChanged: (hasUnsavedChanges: boolean) => void = () => {}) {}

  public get isSaving() { return this.writing || this.loading; }
  public get hasUnsavedChanges() { return this.desired !== undefined; }

  public async start(retryRepair = false) {
    if (this.disposed || this.loading) return;
    this.loading = true;
    const previous = this.base;
    this.remote = undefined;
    try {
      const result = await this.host.loadLibrary(retryRepair);
      if (this.disposed) return;
      const latest = this.remote ?? (result.status === "loaded" ? result : undefined);
      if (!latest) throw new Error("Library unavailable");
      this.remote = undefined;
      parse(latest.content);
      if (this.desired !== undefined) {
        // After corruption, restore the in-memory library as well as pending edits.
        // Ordinary reloads retain the original delta, including local deletions.
        const baseItems = previous && !this.needsRepair ? parse(previous.content) : [];
        this.desired = JSON.stringify(mergeLibraryChanges(baseItems, parse(this.desired), parse(latest.content)));
        this.base = latest;
        await this.applyContent(this.desired);
      } else {
        this.base = latest;
        await this.applyContent(latest.content);
      }
      this.needsRepair = false;
      this.failed = false;
    } catch {
      if (!this.disposed) { this.failed = true; this.onFailure(); }
    } finally {
      this.loading = false;
      void this.flush();
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
    if (this.writing || this.loading || this.desired !== undefined || !this.base) {
      this.remote = snapshot;
      return;
    }
    this.base = snapshot;
    void this.applyContent(snapshot.content).catch(() => this.onFailure());
  }

  public retry() {
    this.failed = false;
    this.conflicts = 0;
    if (!this.base || this.needsRepair) void this.start(true);
    else void this.flush();
  }

  public dispose() { this.disposed = true; }

  private async applyContent(content: string) {
    if (this.disposed) return;
    const items = parse(content);
    this.applying = JSON.stringify(items);
    try { await this.apply(items); }
    finally { this.applying = undefined; }
  }

  private async flush() {
    if (this.disposed || this.writing || this.loading || this.failed || !this.base || this.desired === undefined) return;
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
      const code = typeof error === "object" && error !== null && "code" in error ? error.code : undefined;
      if (code === "LibraryCorrupt") this.needsRepair = true;
      if ((code === "LibraryConflict" || code === "LibraryCorrupt") && this.conflicts++ < 3) {
        await this.start();
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
