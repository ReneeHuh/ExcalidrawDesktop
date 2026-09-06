import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DocumentLoadQueue } from "./DocumentLoadQueue";

const request = (loadId: string) => ({ loadId, fileName: "same.excalidraw", content: "{}", isRecovery: false });
const deferred = <T>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
};

describe("DocumentLoadQueue", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("waits for a save, preserves load order, and correlates same-named files", async () => {
    let busy = true;
    const applied = vi.fn();
    const load = vi.fn(async (payload) => payload.loadId);
    const queue = new DocumentLoadQueue(window, {
      busy: () => busy, setLoading: value => { busy = value; }, load,
      applied, failed: vi.fn(),
    });
    queue.enqueue(request("one")); queue.enqueue(request("two"));
    await vi.advanceTimersByTimeAsync(100);
    expect(load).not.toHaveBeenCalled();
    busy = false;
    await vi.advanceTimersByTimeAsync(25);
    expect(applied.mock.calls.map(call => call[1])).toEqual(["one", "two"]);
    queue.dispose();
  });

  it("timeouts a hung parser, permits another load, and invalidates the late result", async () => {
    const parse = deferred<string>();
    let wasCancelled!: () => boolean;
    let busy = false;
    const applied = vi.fn(); const failed = vi.fn();
    const load = vi.fn((_request, cancelled: () => boolean) => {
      wasCancelled = cancelled;
      return parse.promise;
    });
    const queue = new DocumentLoadQueue(window, {
      busy: () => busy, setLoading: value => { busy = value; }, load, applied, failed,
    });
    queue.enqueue(request("expired"));
    await vi.advanceTimersByTimeAsync(30_000);
    expect(wasCancelled()).toBe(true);
    expect(failed).toHaveBeenCalledOnce();
    expect(busy).toBe(false);
    load.mockResolvedValueOnce("new");
    queue.enqueue(request("current"));
    await vi.advanceTimersByTimeAsync(0);
    parse.resolve("old");
    await vi.advanceTimersByTimeAsync(0);
    expect(applied).toHaveBeenCalledExactlyOnceWith(request("current"), "new");
    queue.dispose();
  });

  it("removes cancelled queued work and disposes every outstanding callback", async () => {
    let busy = true;
    const applied = vi.fn(); const failed = vi.fn(); const load = vi.fn(async () => "scene");
    const queue = new DocumentLoadQueue(window, {
      busy: () => busy, setLoading: value => { busy = value; }, load, applied, failed,
    });
    queue.enqueue(request("cancelled")); queue.cancel("cancelled");
    queue.dispose(); busy = false;
    await vi.advanceTimersByTimeAsync(60_000);
    expect(load).not.toHaveBeenCalled();
    expect(applied).not.toHaveBeenCalled();
    expect(failed).not.toHaveBeenCalled();
  });
});
