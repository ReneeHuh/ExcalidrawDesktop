type Snapshot = { revision: string; content: string };
type TimerWindow = Pick<Window, "setTimeout" | "clearTimeout">;

/** Coalesces edits and retries until the host confirms a snapshot reached disk. */
export class RecoverySnapshotController {
  private timer?: number;
  private desiredRevision?: string;
  private storedRevision?: string;
  private inFlight = false;
  private generation = 0;
  private failures = 0;
  private dirtySince?: number;
  private readonly maxDirtyAgeMilliseconds = 10_000;

  public constructor(
    private readonly ownerWindow: TimerWindow,
    private readonly capture: () => Snapshot | null,
    private readonly persist: (content: string) => Promise<{ status: "stored" | "ignored" }>,
  ) {}

  public schedule(revision: string) {
    if (revision === this.desiredRevision) {
      return;
    }
    if (revision === this.storedRevision && !this.inFlight) {
      this.desiredRevision = undefined;
      this.clearTimer();
      return;
    }
    this.desiredRevision = revision;
    if (this.dirtySince === undefined) {
      this.dirtySince = Date.now();
    }
    // Edits and viewport events must not keep postponing a failed write.
    if (!this.inFlight && this.timer === undefined) {
      const age = this.dirtySince === undefined ? 0 : Date.now() - this.dirtySince;
      this.arm(Math.min(2_000, Math.max(1, this.maxDirtyAgeMilliseconds - age)));
    }
  }

  public reset() {
    this.generation++;
    this.clearTimer();
    this.desiredRevision = undefined;
    this.storedRevision = undefined;
    this.failures = 0;
    this.dirtySince = undefined;
  }

  private clearTimer() {
    if (this.timer !== undefined) {
      this.ownerWindow.clearTimeout(this.timer);
      this.timer = undefined;
    }
  }

  private arm(delay: number) {
    this.clearTimer();
    this.timer = this.ownerWindow.setTimeout(() => {
      this.timer = undefined;
      void this.flush();
    }, delay);
  }

  private async flush() {
    if (this.inFlight || this.desiredRevision === undefined) {
      return;
    }
    this.inFlight = true;
    const generation = this.generation;
    try {
      const snapshot = this.capture();
      if (!snapshot) {
        this.desiredRevision = undefined;
        return;
      }
      this.desiredRevision = snapshot.revision;
      const result = await this.persist(snapshot.content);
      if (generation !== this.generation) {
        return;
      }
      if (result.status !== "stored") {
        throw new Error("The host did not store the recovery snapshot.");
      }
      this.storedRevision = snapshot.revision;
      this.failures = 0;
      if (this.desiredRevision === snapshot.revision) {
        this.desiredRevision = undefined;
        this.dirtySince = undefined;
      }
    } catch {
      if (generation === this.generation) {
        this.failures++;
      }
    } finally {
      this.inFlight = false;
      if (this.desiredRevision !== undefined) {
      const retryDelay = Math.min(30_000, 2_000 * 2 ** Math.min(this.failures, 4));
      const age = this.dirtySince === undefined ? 0 : Date.now() - this.dirtySince;
      const boundedDelay = this.failures === 0
        ? Math.min(retryDelay, Math.max(1, this.maxDirtyAgeMilliseconds - age))
        : retryDelay;
      this.arm(boundedDelay);
      }
    }
  }
}
