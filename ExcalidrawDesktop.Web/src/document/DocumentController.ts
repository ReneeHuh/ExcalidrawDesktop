import {
  CaptureUpdateAction,
  getSceneVersion,
  loadFromBlob,
  serializeAsJSON,
} from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";

import type { DesktopBridge } from "../bridge/DesktopBridge";

export type DocumentEditorApi = Pick<
  ExcalidrawImperativeAPI,
  | "addFiles"
  | "getAppState"
  | "getFiles"
  | "getSceneElements"
  | "history"
  | "updateScene"
>;

type SceneLoader = typeof loadFromBlob;

type OpenDocumentOptions = {
  api: DocumentEditorApi;
  bridge: Pick<DesktopBridge, "notifyDocumentOpened" | "openDocument">;
  hasUnsavedChanges: boolean;
  loadScene?: SceneLoader;
};

type LoadDocumentOptions = {
  api: DocumentEditorApi;
  bridge: Pick<DesktopBridge, "notifyDocumentOpened">;
  fileName: string;
  content: string;
  notifyOpened?: boolean;
  loadScene?: SceneLoader;
};

export type OpenDocumentResult =
  | { status: "cancelled" }
  | { status: "opened"; fileName: string; sceneVersion: number };

export const requestNewDocument = (
  bridge: Pick<DesktopBridge, "newDocument">,
  hasUnsavedChanges: boolean,
) => bridge.newDocument(hasUnsavedChanges);

export const openDocumentIntoEditor = async ({
  api,
  bridge,
  hasUnsavedChanges,
  loadScene = loadFromBlob,
}: OpenDocumentOptions): Promise<OpenDocumentResult> => {
  const response = await bridge.openDocument(hasUnsavedChanges);
  if (response.status === "cancelled") {
    return response;
  }

  return loadDocumentContentIntoEditor({
    api,
    bridge,
    fileName: response.fileName,
    content: response.content,
    loadScene,
  });
};

export const loadDocumentContentIntoEditor = async ({
  api,
  bridge,
  fileName,
  content,
  notifyOpened = true,
  loadScene = loadFromBlob,
}: LoadDocumentOptions): Promise<OpenDocumentResult> => {
  const scene = await loadScene(
    new Blob([content], { type: "application/json" }),
    api.getAppState(),
    api.getSceneElements(),
  );

  api.addFiles(Object.values(scene.files));
  api.updateScene({
    elements: scene.elements,
    appState: scene.appState,
    captureUpdate: CaptureUpdateAction.NEVER,
  });
  api.history.clear();
  if (notifyOpened) {
    bridge.notifyDocumentOpened(fileName);
  }

  return {
    status: "opened",
    fileName,
    sceneVersion: getSceneVersion(scene.elements),
  };
};

type SaveDocumentOptions = {
  api: DocumentEditorApi;
  bridge: Pick<DesktopBridge, "saveDocument" | "saveDocumentAs">;
  saveAs: boolean;
  serialize?: typeof serializeAsJSON;
};

export type SaveDocumentResult =
  | { status: "cancelled" }
  | { status: "saved"; fileName: string; sceneVersion: number };

export const saveDocumentFromEditor = async ({
  api,
  bridge,
  saveAs,
  serialize = serializeAsJSON,
}: SaveDocumentOptions): Promise<SaveDocumentResult> => {
  const elements = api.getSceneElements();
  const sceneVersion = getSceneVersion(elements);
  const content = serialize(
    elements,
    api.getAppState(),
    api.getFiles(),
    "local",
  );
  const response = saveAs
    ? await bridge.saveDocumentAs(content)
    : await bridge.saveDocument(content);

  return response.status === "saved" ? { ...response, sceneVersion } : response;
};
