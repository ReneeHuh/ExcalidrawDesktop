import { describe, expect, it, vi } from "vitest";
import { restoreAppState } from "@excalidraw/excalidraw";

import type { DesktopBridge } from "../bridge/DesktopBridge";

import {
  type DocumentEditorApi,
  loadDocumentContentIntoEditor,
  openDocumentIntoEditor,
  requestNewDocument,
  saveDocumentFromEditor,
} from "./DocumentController";
import { getDocumentRevision } from "./DocumentRevision";

const createApi = (): DocumentEditorApi => ({
  addFiles: vi.fn(),
  getAppState: vi.fn(() => ({} as never)),
  getFiles: vi.fn(() => ({} as never)),
  getSceneElements: vi.fn(() => []),
  history: { clear: vi.fn() },
  updateScene: vi.fn(),
});

describe("openDocumentIntoEditor", () => {
  it("leaves the editor unchanged when the native picker is cancelled", async () => {
    const api = createApi();
    const bridge: Pick<DesktopBridge, "notifyDocumentOpened" | "openDocument"> =
      {
        openDocument: vi.fn(async () => ({ status: "cancelled" as const })),
        notifyDocumentOpened: vi.fn(),
      };

    await expect(
      openDocumentIntoEditor({ api, bridge, hasUnsavedChanges: true }),
    ).resolves.toEqual({ status: "cancelled" });
    expect(api.updateScene).not.toHaveBeenCalled();
    expect(api.addFiles).not.toHaveBeenCalled();
    expect(bridge.notifyDocumentOpened).not.toHaveBeenCalled();
  });

  it("leaves the editor unchanged when scene restoration fails", async () => {
    const api = createApi();
    const bridge: Pick<DesktopBridge, "notifyDocumentOpened" | "openDocument"> =
      {
        openDocument: vi.fn(async () => ({
          status: "opened" as const,
          fileName: "invalid.excalidraw",
          content: "invalid",
        })),
        notifyDocumentOpened: vi.fn(),
      };
    const loadScene = vi.fn(async () => {
      throw new Error("invalid scene");
    });

    await expect(
      openDocumentIntoEditor({
        api,
        bridge,
        hasUnsavedChanges: false,
        loadScene,
      }),
    ).rejects.toThrow("invalid scene");
    expect(api.updateScene).not.toHaveBeenCalled();
    expect(api.addFiles).not.toHaveBeenCalled();
    expect(bridge.notifyDocumentOpened).not.toHaveBeenCalled();
  });

  it("restores a valid scene and reports the opened filename", async () => {
    const api = createApi();
    const bridge: Pick<DesktopBridge, "notifyDocumentOpened" | "openDocument"> =
      {
        openDocument: vi.fn(async () => ({
          status: "opened" as const,
          fileName: "drawing.excalidraw",
          content: "{}",
        })),
        notifyDocumentOpened: vi.fn(),
      };
    const loadScene = vi.fn(async () => ({
      elements: [],
      appState: {},
      files: {},
    }));

    await expect(
      openDocumentIntoEditor({
        api,
        bridge,
        hasUnsavedChanges: false,
        loadScene: loadScene as never,
      }),
    ).resolves.toEqual({
      status: "opened",
      fileName: "drawing.excalidraw",
      revision: getDocumentRevision([], {}),
    });
    expect(api.updateScene).toHaveBeenCalledOnce();
    expect(api.history.clear).toHaveBeenCalledOnce();
    expect(bridge.notifyDocumentOpened).toHaveBeenCalledWith(
      "drawing.excalidraw",
    );
  });
});

describe("loadDocumentContentIntoEditor", () => {
  it("uses the loaded background as the clean baseline instead of the previous editor state", async () => {
    const api = createApi();
    vi.mocked(api.getAppState).mockReturnValue({ viewBackgroundColor: "#ffffff" } as never);
    const appState = restoreAppState({ viewBackgroundColor: "#ff0000" }, null);
    const result = await loadDocumentContentIntoEditor({
      api,
      bridge: { notifyDocumentOpened: vi.fn() },
      fileName: "background.excalidraw",
      content: "{}",
      loadScene: vi.fn(async () => ({ elements: [], appState, files: {} })),
    });
    expect(result).toEqual({
      status: "opened",
      fileName: "background.excalidraw",
      revision: getDocumentRevision([], appState),
    });
  });

  it("restores recovery content without confirming a newly opened file", async () => {
    const api = createApi();
    const bridge: Pick<DesktopBridge, "notifyDocumentOpened"> = {
      notifyDocumentOpened: vi.fn(),
    };

    await loadDocumentContentIntoEditor({
      api,
      bridge,
      fileName: "Recovered drawing",
      content: "{}",
      notifyOpened: false,
      loadScene: vi.fn(async () => ({
        elements: [],
        appState: {},
        files: {},
      })) as never,
    });

    expect(api.updateScene).toHaveBeenCalledOnce();
    expect(bridge.notifyDocumentOpened).not.toHaveBeenCalled();
  });
});

describe("saveDocumentFromEditor", () => {
  it("serializes the complete scene and saves to the active file", async () => {
    const api = createApi();
    const bridge: Pick<DesktopBridge, "saveDocument" | "saveDocumentAs"> = {
      saveDocument: vi.fn(async () => ({
        status: "saved" as const,
        fileName: "drawing.excalidraw",
      })),
      saveDocumentAs: vi.fn(),
    };
    const serialize = vi.fn(() => "serialized scene");

    await expect(
      saveDocumentFromEditor({
        api,
        bridge,
        saveAs: false,
        serialize: serialize as never,
      }),
    ).resolves.toEqual({
      status: "saved",
      fileName: "drawing.excalidraw",
      revision: getDocumentRevision([], {}),
    });
    expect(serialize).toHaveBeenCalledWith([], {}, {}, "local");
    expect(bridge.saveDocument).toHaveBeenCalledWith("serialized scene", undefined);
    expect(bridge.saveDocumentAs).not.toHaveBeenCalled();
  });

  it("routes Save As through the native picker contract", async () => {
    const api = createApi();
    const bridge: Pick<DesktopBridge, "saveDocument" | "saveDocumentAs"> = {
      saveDocument: vi.fn(),
      saveDocumentAs: vi.fn(async () => ({ status: "cancelled" as const })),
    };

    await expect(
      saveDocumentFromEditor({
        api,
        bridge,
        saveAs: true,
        serialize: vi.fn(() => "serialized scene") as never,
      }),
    ).resolves.toEqual({ status: "cancelled" });
    expect(bridge.saveDocument).not.toHaveBeenCalled();
    expect(bridge.saveDocumentAs).toHaveBeenCalledWith("serialized scene", undefined);
  });

  it.each([false, true])("keeps the saved background baseline during an in-flight save (saveAs=%s)", async (saveAs) => {
    const api = createApi();
    let appState = { viewBackgroundColor: "#ffffff" };
    vi.mocked(api.getAppState).mockImplementation(() => appState as never);
    let finishSave!: (result: { status: "saved"; fileName: string }) => void;
    const save = vi.fn((_content: string) => new Promise<{ status: "saved"; fileName: string }>(
      (resolve) => { finishSave = resolve; },
    ));
    const pending = saveDocumentFromEditor({
      api,
      bridge: { saveDocument: save, saveDocumentAs: save },
      saveAs,
    });
    expect(JSON.parse(save.mock.calls[0][0] as string).appState.viewBackgroundColor)
      .toBe("#ffffff");
    appState = { viewBackgroundColor: "#ff0000" };
    finishSave({ status: "saved", fileName: "drawing.excalidraw" });
    const result = await pending;
    expect(result.status).toBe("saved");
    if (result.status !== "saved") { throw new Error("Expected a saved result"); }
    expect(result.revision).toBe(getDocumentRevision([], { viewBackgroundColor: "#ffffff" }));
    expect(result.revision).not.toBe(getDocumentRevision([], appState));
  });
});

describe("requestNewDocument", () => {
  it("forwards dirty state without mutating the editor before confirmation", async () => {
    const bridge: Pick<DesktopBridge, "newDocument"> = {
      newDocument: vi.fn(async () => ({ status: "created" as const })),
    };

    await expect(requestNewDocument(bridge, true)).resolves.toEqual({
      status: "created",
    });
    expect(bridge.newDocument).toHaveBeenCalledWith(true);
  });
});
