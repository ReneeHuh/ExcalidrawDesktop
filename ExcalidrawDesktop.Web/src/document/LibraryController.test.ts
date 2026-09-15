import { describe, expect, it, vi } from "vitest";
import { LibraryController } from "./LibraryController";

const snap = (items: unknown[], revision = "r0") => ({ status: "loaded" as const, content: JSON.stringify(items), revision });
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("LibraryController", () => {
  it("repairs corruption after loading and restores unchanged items along with local edits", async () => {
    const host = {
      loadLibrary: vi.fn().mockResolvedValueOnce(snap([{ id: "existing" }], "r1")).mockResolvedValue(snap([], "reset")),
      saveLibrary: vi.fn().mockRejectedValueOnce({ code: "LibraryCorrupt" })
        .mockImplementation(async (content: string) => ({ status: "saved", content, revision: "r3" })),
    };
    const failure = vi.fn();
    const c = new LibraryController(host, vi.fn().mockResolvedValue(undefined), failure);
    await c.start(); c.changed([{ id: "existing" }, { id: "local" }]); await flush();
    expect(host.saveLibrary).toHaveBeenLastCalledWith('[{"id":"existing"},{"id":"local"}]', "reset");
    expect(c.hasUnsavedChanges).toBe(false);
    expect(failure).not.toHaveBeenCalled();
  });

  it("retains edits after cancelling repair and explicitly requests repair on Retry", async () => {
    const host = {
      loadLibrary: vi.fn().mockResolvedValueOnce(snap([], "r1"))
        .mockResolvedValueOnce({ status: "unavailable" }).mockResolvedValue(snap([], "reset")),
      saveLibrary: vi.fn().mockRejectedValueOnce({ code: "LibraryCorrupt" })
        .mockImplementation(async (content: string) => ({ status: "saved", content, revision: "r3" })),
    };
    const c = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn());
    await c.start(); c.changed([{ id: "local" }]); await flush();
    expect(c.hasUnsavedChanges).toBe(true);
    expect(host.saveLibrary).toHaveBeenCalledTimes(1);
    c.retry(); await flush();
    expect(host.loadLibrary).toHaveBeenLastCalledWith(true);
    expect(host.saveLibrary).toHaveBeenLastCalledWith('[{"id":"local"}]', "reset");
    expect(c.hasUnsavedChanges).toBe(false);
  });

  it("uses a repair broadcast even when its own stale repair approval reports unavailable", async () => {
    let resolve!: (value: { status: "unavailable" }) => void;
    const host = { loadLibrary: vi.fn(() => new Promise<{ status: "unavailable" }>(done => { resolve = done; })), saveLibrary: vi.fn() };
    const apply = vi.fn().mockResolvedValue(undefined);
    const failure = vi.fn();
    const c = new LibraryController(host, apply, failure);
    const loading = c.start();
    c.receive(snap([{ id: "other-window" }], "repaired"));
    resolve({ status: "unavailable" }); await loading;
    expect(apply).toHaveBeenLastCalledWith([{ id: "other-window" }]);
    expect(failure).not.toHaveBeenCalled();
  });

  it("keeps local removals and remote edits when reloading a normal conflict", async () => {
    const host = {
      loadLibrary: vi.fn().mockResolvedValueOnce(snap([{ id: "removed" }, { id: "kept", name: "old" }], "r1"))
        .mockResolvedValue(snap([{ id: "removed" }, { id: "kept", name: "remote edit" }, { id: "remote" }], "r2")),
      saveLibrary: vi.fn().mockRejectedValueOnce({ code: "LibraryConflict" })
        .mockImplementation(async (content: string) => ({ status: "saved", content, revision: "r3" })),
    };
    const c = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn());
    await c.start(); c.changed([{ id: "kept", name: "old" }]); await flush();
    expect(host.saveLibrary).toHaveBeenLastCalledWith('[{"id":"kept","name":"remote edit"},{"id":"remote"}]', "r2");
    expect(c.hasUnsavedChanges).toBe(false);
  });

  it("waits for repair before saving edits made while the dialog is open", async () => {
    let resolve!: (value: ReturnType<typeof snap>) => void;
    const host = {
      loadLibrary: vi.fn().mockResolvedValueOnce(snap([], "r1"))
        .mockImplementationOnce(() => new Promise<ReturnType<typeof snap>>(done => { resolve = done; })),
      saveLibrary: vi.fn().mockRejectedValueOnce({ code: "LibraryCorrupt" })
        .mockImplementation(async (content: string) => ({ status: "saved", content, revision: "r3" })),
    };
    const c = new LibraryController(host, vi.fn().mockResolvedValue(undefined), vi.fn());
    await c.start(); c.changed([{ id: "first" }]); await flush();
    c.changed([{ id: "first" }, { id: "second" }]); await flush();
    expect(host.saveLibrary).toHaveBeenCalledTimes(1);
    resolve(snap([], "reset")); await flush();
    expect(host.saveLibrary).toHaveBeenLastCalledWith('[{"id":"first"},{"id":"second"}]', "reset");
    expect(c.hasUnsavedChanges).toBe(false);
  });

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
