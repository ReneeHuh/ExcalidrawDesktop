import type { HostEventMap } from "../bridge/BridgeProtocol";
import { abortable } from "../bridge/AbortableOperation";

type Load = HostEventMap["document.loadRequested"];
type Timers = Pick<Window, "setTimeout" | "clearTimeout"> & { AbortController: typeof AbortController };

/** Serializes host loads with saves and prevents expired parsers from applying a scene. */
export class DocumentLoadQueue<T> {
  private queued: Load[] = [];
  private active?: { request: Load; controller: AbortController };
  private timer?: number;
  private disposed = false;

  public constructor(private readonly owner: Timers, private readonly callbacks: {
    busy: () => boolean;
    setLoading: (loading: boolean) => void;
    load: (request: Load, cancelled: () => boolean) => Promise<T>;
    applied: (request: Load, result: T) => void;
    failed: (request: Load, error: unknown) => void;
  }) {}

  public enqueue(request: Load) {
    if (this.disposed || !request.loadId || this.active?.request.loadId === request.loadId ||
      this.queued.some(item => item.loadId === request.loadId)) return;
    this.queued.push(request);
    this.pump();
  }

  public cancel(loadId: string) {
    this.queued = this.queued.filter(item => item.loadId !== loadId);
    if (this.active?.request.loadId === loadId) this.active.controller.abort();
  }

  public dispose() {
    this.disposed = true;
    this.queued = [];
    if (this.timer !== undefined) this.owner.clearTimeout(this.timer);
    this.active?.controller.abort();
  }

  private pump() {
    if (this.disposed || this.active || this.queued.length === 0) return;
    if (this.callbacks.busy()) {
      if (this.timer === undefined) this.timer = this.owner.setTimeout(() => {
        this.timer = undefined;
        this.pump();
      }, 25);
      return;
    }
    const request = this.queued.shift()!;
    const controller = new this.owner.AbortController();
    this.active = { request, controller };
    this.callbacks.setLoading(true);
    const timeout = this.owner.setTimeout(() => controller.abort(), 30_000);
    void abortable(this.callbacks.load(request, () => controller.signal.aborted || this.disposed), controller.signal)
      .then(result => {
        if (!controller.signal.aborted && !this.disposed) this.callbacks.applied(request, result);
      })
      .catch((error: unknown) => {
        if (!this.disposed) this.callbacks.failed(request, error);
      })
      .finally(() => {
        this.owner.clearTimeout(timeout);
        this.active = undefined;
        this.callbacks.setLoading(false);
        this.pump();
      });
  }
}
