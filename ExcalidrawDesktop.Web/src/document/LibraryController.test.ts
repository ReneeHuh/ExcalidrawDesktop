import { describe, expect, it, vi } from "vitest";
import { LibraryController } from "./LibraryController";

const snap = (items: unknown[], revision = "r0") => ({ status: "loaded" as const, content: JSON.stringify(items), revision });
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("LibraryController", () => {
  it("keeps the host's unload barrier until a failed write is retried successfully", async () => {
    const state = vi.fn();
    const host = { loadLibrary: vi.fn().mockResolvedValue(snap([], "r1")),
      saveLibrary: vi.fn().mockRejectedValueOnce(new Error("disk")).mockResolvedValue({ status: "saved", content: '[{"id":"a"}]', revision: "r2" }) };
    const c = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn(), state);
    await c.start(); c.changed([{ id: "a" }]); await flush();
    expect(state).toHaveBeenLastCalledWith(true);
    c.retry(); await flush();
    expect(state).toHaveBeenLastCalledWith(false);
  });

  it("persists an undo to the original library while a previous change is saving", async () => {
    let save!: (value: { status: "saved"; content: string; revision: string }) => void;
    const host = { loadLibrary: vi.fn().mockResolvedValue(snap([], "r1")),
      saveLibrary: vi.fn().mockImplementationOnce(() => new Promise(done => { save = done; }))
        .mockResolvedValue({ status: "saved", content: "[]", revision: "r3" }) };
    const c = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn());
    await c.start(); c.changed([{ id: "a" }]); c.changed([]);
    save({ status: "saved", content: '[{"id":"a"}]', revision: "r2" }); await flush();
    expect(host.saveLibrary).toHaveBeenLastCalledWith("[]", "r2");
    expect(c.hasUnsavedChanges).toBe(false);
  });
  it("keeps a newer broadcast arriving before the initial read completes", async () => {
    let resolve!: (value: ReturnType<typeof snap>) => void;
    const host = { loadLibrary: vi.fn(() => new Promise<ReturnType<typeof snap>>(done => { resolve = done; })), saveLibrary: vi.fn() };
    const apply = vi.fn().mockResolvedValue(undefined);
    const controller = new LibraryController(host, apply, vi.fn());
    const loading = controller.start();
    controller.receive(snap([{ id: "newer" }], "r2"));
    resolve(snap([], "r1"));
    await loading;
    expect(apply).toHaveBeenLastCalledWith([{ id: "newer" }]);
    expect(host.saveLibrary).not.toHaveBeenCalled();
  });

  it("preserves a local addition made while the shared library is loading", async () => {
    let resolve!: (value: ReturnType<typeof snap>) => void;
    const host = { loadLibrary: vi.fn(() => new Promise<ReturnType<typeof snap>>(done => { resolve = done; })),
      saveLibrary: vi.fn(async (content: string) => ({ status: "saved" as const, content, revision: "r2" })) };
    const controller = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn());
    const loading = controller.start();
    controller.changed([{ id: "local" }]);
    resolve(snap([{ id: "remote" }], "r1"));
    await loading; await flush();
    expect(JSON.parse(host.saveLibrary.mock.calls[0][0])).toEqual([{ id: "remote" }, { id: "local" }]);
  });

  it("bounds repeated conflicts and retains local changes for an explicit retry", async () => {
    const failure = vi.fn();
    const host = { loadLibrary: vi.fn().mockResolvedValue(snap([], "r1")),
      saveLibrary: vi.fn().mockRejectedValue({ code: "LibraryConflict" }) };
    const controller = new LibraryController(host, vi.fn().mockResolvedValue(undefined), failure);
    await controller.start(); controller.changed([{ id: "local" }]); await flush();
    expect(host.saveLibrary).toHaveBeenCalledTimes(4);
    expect(controller.hasUnsavedChanges).toBe(true);
    expect(failure).toHaveBeenCalledOnce();
  });
  it("loads without writing back its initial contents", async () => {
    const host = { loadLibrary: vi.fn().mockResolvedValue(snap([{ id: "a" }])), saveLibrary: vi.fn() };
    const apply = vi.fn().mockResolvedValue(undefined);
    await new LibraryController(host, apply, vi.fn()).start();
    expect(host.saveLibrary).not.toHaveBeenCalled();
    expect(apply).toHaveBeenCalledWith([{ id: "a" }]);
  });

  it("persists a user change with the loaded revision", async () => {
    const host = { loadLibrary: vi.fn().mockResolvedValue(snap([], "r1")), saveLibrary: vi.fn().mockResolvedValue({ status: "saved", content: '[{"id":"a"}]', revision: "r2" }) };
    const controller = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn());
    await controller.start(); controller.changed([{ id: "a" }]); await flush();
    expect(host.saveLibrary).toHaveBeenCalledWith('[{"id":"a"}]', "r1");
  });

  it("merges a remote addition after a CAS conflict", async () => {
    const host = { loadLibrary: vi.fn().mockResolvedValueOnce(snap([], "r1")).mockResolvedValueOnce(snap([{ id: "remote" }], "r2")), saveLibrary: vi.fn().mockRejectedValueOnce({ code: "LibraryConflict" }).mockResolvedValue({ status: "saved", content: "[]", revision: "r3" }) };
    const controller = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn());
    await controller.start(); controller.changed([{ id: "local" }]); await flush(); await flush();
    expect(host.saveLibrary).toHaveBeenCalledWith(expect.stringContaining("local"), "r2");
    expect(host.saveLibrary).toHaveBeenCalledWith(expect.stringContaining("remote"), "r2");
  });

  it("does not echo a broadcast received for its own save", async () => {
    const host = { loadLibrary: vi.fn().mockResolvedValue(snap([], "r1")), saveLibrary: vi.fn().mockResolvedValue({ status: "saved", content: '[{"id":"a"}]', revision: "r2" }) };
    const apply = vi.fn().mockResolvedValue(undefined); const c = new LibraryController(host, apply, vi.fn());
    await c.start(); c.changed([{ id: "a" }]); await flush(); c.receive(snap([{ id: "a" }], "r2")); await flush();
    expect(host.loadLibrary).toHaveBeenCalledTimes(1);
  });

  it("retains desired changes and retries after a failed write", async () => {
    const failure = vi.fn(); const host = { loadLibrary: vi.fn().mockResolvedValue(snap([], "r1")), saveLibrary: vi.fn().mockRejectedValueOnce(new Error("disk")).mockResolvedValue({ status: "saved", content: '[{"id":"a"}]', revision: "r2" }) };
    const c = new LibraryController(host, vi.fn().mockResolvedValue(undefined), failure); await c.start(); c.changed([{ id: "a" }]); await flush(); expect(c.hasUnsavedChanges).toBe(true); c.retry(); await flush(); expect(c.hasUnsavedChanges).toBe(false); expect(failure).toHaveBeenCalled();
  });
});
