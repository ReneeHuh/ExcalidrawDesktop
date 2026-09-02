import { describe, expect, it, vi } from "vitest";

import type { DesktopBridge } from "../bridge/DesktopBridge";

import {
  type DocumentEditorApi,
  loadDocumentContentIntoEditor,
  openDocumentIntoEditor,
  requestNewDocument,
  saveDocumentFromEditor,
} from "./DocumentController";

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
      sceneVersion: 0,
    });
    expect(api.updateScene).toHaveBeenCalledOnce();
    expect(api.history.clear).toHaveBeenCalledOnce();
    expect(bridge.notifyDocumentOpened).toHaveBeenCalledWith(
      "drawing.excalidraw",
    );
  });
});

describe("loadDocumentContentIntoEditor", () => {
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
      sceneVersion: 0,
    });
    expect(serialize).toHaveBeenCalledWith([], {}, {}, "local");
    expect(bridge.saveDocument).toHaveBeenCalledWith("serialized scene");
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
    expect(bridge.saveDocumentAs).toHaveBeenCalledWith("serialized scene");
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
