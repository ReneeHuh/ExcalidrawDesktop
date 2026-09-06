import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { RecoverySnapshotController } from "./RecoverySnapshotController";

const deferred = () => {
  let resolve!: (result: { status: "stored" | "ignored" }) => void;
  const promise = new Promise<{ status: "stored" | "ignored" }>((complete) => { resolve = complete; });
  return { promise, resolve };
};

describe("RecoverySnapshotController", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const setup = () => {
    const state = { revision: "a", content: "drawing a" };
    const persist = vi.fn(async (_content: string): Promise<{ status: "stored" | "ignored" }> => ({ status: "stored" }));
    return { state, persist, controller: new RecoverySnapshotController(window, () => ({ ...state }), persist) };
  };

  it("does not postpone recovery for repeated viewport notifications", async () => {
    const { controller, persist } = setup();
    for (let i = 0; i < 10; i++) {
      controller.schedule("a");
      await vi.advanceTimersByTimeAsync(250);
    }
    expect(persist).toHaveBeenCalledExactlyOnceWith("drawing a");
  });

  it("checkpoints during continuous edits", async () => {
    const { controller, persist, state } = setup();
    for (let i = 0; i < 60; i++) {
      state.revision = `r${i}`;
      state.content = `drawing ${i}`;
      controller.schedule(state.revision);
      await vi.advanceTimersByTimeAsync(1_000);
    }
    // Assert before any quiet interval: the old trailing debounce wrote nothing.
    expect(persist.mock.calls.length).toBeGreaterThanOrEqual(5);
    expect(persist).toHaveBeenLastCalledWith("drawing 59");
  });

  it("retries a failed write without needing another edit", async () => {
    const { controller, persist } = setup();
    persist.mockRejectedValueOnce(new Error("disk unavailable"));
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(2_000);
    expect(persist).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(4_000);
    expect(persist).toHaveBeenCalledTimes(2);
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(60_000);
    expect(persist).toHaveBeenCalledTimes(2);
  });

  it("does not accept an ignored snapshot as durable", async () => {
    const { controller, persist } = setup();
    persist.mockResolvedValueOnce({ status: "ignored" });
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(6_000);
    expect(persist).toHaveBeenCalledTimes(2);
  });

  it("retries the newest drawing and caps retry delays", async () => {
    const { controller, persist, state } = setup();
    persist.mockRejectedValue(new Error("disk full"));
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(2_000);
    state.revision = "b";
    state.content = "drawing b";
    controller.schedule("b");
    await vi.advanceTimersByTimeAsync(4_000);
    expect(persist).toHaveBeenLastCalledWith("drawing b");
    await vi.advanceTimersByTimeAsync(8_000 + 16_000 + 30_000 + 30_000);
    expect(persist).toHaveBeenCalledTimes(6);
  });

  it("does not overlap writes and follows an edit made during a write", async () => {
    const { controller, persist, state } = setup();
    const write = deferred();
    persist.mockReturnValueOnce(write.promise);
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(2_000);
    state.revision = "b";
    state.content = "drawing b";
    controller.schedule("b");
    await vi.advanceTimersByTimeAsync(10_000);
    expect(persist).toHaveBeenCalledTimes(1);
    write.resolve({ status: "stored" });
    await vi.advanceTimersByTimeAsync(2_000);
    expect(persist).toHaveBeenLastCalledWith("drawing b");
  });

  it("rewrites an earlier revision when an in-flight snapshot overwrites it", async () => {
    const { controller, persist, state } = setup();
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(2_000);
    const write = deferred();
    persist.mockReturnValueOnce(write.promise);
    state.revision = "b";
    state.content = "drawing b";
    controller.schedule("b");
    await vi.advanceTimersByTimeAsync(2_000);
    state.revision = "a";
    state.content = "drawing a";
    controller.schedule("a");
    write.resolve({ status: "stored" });
    await vi.advanceTimersByTimeAsync(2_000);
    expect(persist).toHaveBeenCalledTimes(3);
    expect(persist).toHaveBeenLastCalledWith("drawing a");
  });

  it("cancels retries on save, reset, or disposal and ignores late acknowledgements", async () => {
    const { controller, persist } = setup();
    const write = deferred();
    persist.mockReturnValueOnce(write.promise);
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(2_000);
    controller.reset();
    write.resolve({ status: "stored" });
    await vi.advanceTimersByTimeAsync(60_000);
    expect(persist).toHaveBeenCalledTimes(1);
    controller.schedule("a");
    await vi.advanceTimersByTimeAsync(2_000);
    expect(persist).toHaveBeenCalledTimes(2);
  });
});
