import { describe, expect, it, vi } from "vitest";

import { exportWholeDrawingAsPng } from "./ImageExportController";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";

const request = {
  exportId: "720e34f1-a3ea-4af3-93ed-a950b2357c42",
  uploadUrl:
    "https://export-9d32585b1ac74f489ef08b7a55d49570.excalidraw.local/_desktop/export/720e34f1-a3ea-4af3-93ed-a950b2357c42",
  maxDimension: 16_384,
  maxBytes: 100 * 1024 * 1024,
  scale: 2,
  padding: 10,
};

const createApi = (elements: unknown[] = [{ id: "one" }]) =>
  ({
    getSceneElements: vi.fn(() => elements),
    getFiles: vi.fn(() => ({ image: { id: "image" } })),
    getAppState: vi.fn(() => ({
      viewBackgroundColor: "#ffffff",
      exportWithDarkMode: true,
    })),
  }) as unknown as ExcalidrawImperativeAPI;

describe("exportWholeDrawingAsPng", () => {
  it("exports every scene element and uploads a bounded PNG", async () => {
    const api = createApi();
    const png = new Blob([new Uint8Array([137, 80, 78, 71])], {
      type: "image/png",
    });
    const exportToBlob = vi.fn(async () => png);
    const upload = vi.fn(async () => undefined);

    await exportWholeDrawingAsPng(api, request, window, {
      exportToBlob: exportToBlob as never,
      getCommonBounds: vi.fn(() => [0, 0, 100, 50] as [number, number, number, number]),
      upload,
    });

    expect(exportToBlob).toHaveBeenCalledWith(
      expect.objectContaining({
        elements: api.getSceneElements(),
        files: api.getFiles(),
        mimeType: "image/png",
        exportPadding: 10,
        appState: expect.objectContaining({
          exportBackground: true,
          exportEmbedScene: false,
          exportWithDarkMode: false,
        }),
      }),
    );
    expect(upload).toHaveBeenCalledWith(
      request.uploadUrl,
      png,
      request.exportId,
    );
  });

  it("rejects an empty drawing without rendering or uploading", async () => {
    const api = createApi([]);
    const exportToBlob = vi.fn();
    const upload = vi.fn();

    await expect(
      exportWholeDrawingAsPng(api, request, window, {
        exportToBlob: exportToBlob as never,
        getCommonBounds: vi.fn(),
        upload,
      }),
    ).rejects.toThrow("Add something");
    expect(exportToBlob).not.toHaveBeenCalled();
    expect(upload).not.toHaveBeenCalled();
  });

  it("rejects dimensions and byte sizes beyond the native limits", async () => {
    const api = createApi();
    const upload = vi.fn();

    await expect(
      exportWholeDrawingAsPng(api, request, window, {
        exportToBlob: vi.fn() as never,
        getCommonBounds: vi.fn(() =>
          [0, 0, 20_000, 1] as [number, number, number, number],
        ),
        upload,
      }),
    ).rejects.toThrow("dimension limit");

    await expect(
      exportWholeDrawingAsPng(api, { ...request, maxBytes: 1 }, window, {
        exportToBlob: vi.fn(async () =>
          new Blob([new Uint8Array([1, 2])], { type: "image/png" }),
        ) as never,
        getCommonBounds: vi.fn(() =>
          [0, 0, 10, 10] as [number, number, number, number],
        ),
        upload,
      }),
    ).rejects.toThrow("exceeds");
    expect(upload).not.toHaveBeenCalled();
  });

  it("honors cancellation before a hung renderer can upload", async () => {
    const controller = new AbortController();
    controller.abort();
    const upload = vi.fn();
    await expect(exportWholeDrawingAsPng(createApi(), request, window, {
      exportToBlob: vi.fn() as never,
      getCommonBounds: vi.fn(() => [0, 0, 10, 10] as [number, number, number, number]),
      upload,
    }, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
    expect(upload).not.toHaveBeenCalled();
  });

  it("stops waiting for a renderer already in progress and ignores its late blob", async () => {
    const controller = new AbortController();
    let finish!: (blob: Blob) => void;
    const upload = vi.fn();
    const operation = exportWholeDrawingAsPng(createApi(), request, window, {
      exportToBlob: vi.fn(() => new Promise<Blob>(resolve => { finish = resolve; })) as never,
      getCommonBounds: vi.fn(() => [0, 0, 10, 10] as [number, number, number, number]),
      upload,
    }, controller.signal);
    controller.abort();
    await expect(operation).rejects.toMatchObject({ name: "AbortError" });
    finish(new Blob(["png"], { type: "image/png" }));
    await Promise.resolve();
    expect(upload).not.toHaveBeenCalled();
  });
});
