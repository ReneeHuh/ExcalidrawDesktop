import {
  CaptureUpdateAction,
  loadFromBlob,
  serializeAsJSON,
} from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";

import type { DesktopBridge } from "../bridge/DesktopBridge";
import { getDocumentRevision } from "./DocumentRevision";

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

type LoadDocumentOptions = {
  api: DocumentEditorApi;
  fileName: string;
  content: string;
  loadScene?: SceneLoader;
  isCancelled?: () => boolean;
};

export type OpenDocumentResult =
  | { status: "cancelled" }
  | { status: "opened"; fileName: string; revision: string };

export const requestNewDocument = (
  bridge: Pick<DesktopBridge, "newDocument">,
  hasUnsavedChanges: boolean,
) => bridge.newDocument(hasUnsavedChanges);

export const loadDocumentContentIntoEditor = async ({
  api,
  fileName,
  content,
  loadScene = loadFromBlob,
  isCancelled = () => false,
}: LoadDocumentOptions): Promise<OpenDocumentResult> => {
  const scene = await loadScene(
    new Blob([content], { type: "application/json" }),
    api.getAppState(),
    api.getSceneElements(),
  );

  if (isCancelled()) {
    throw new Error("The document load was cancelled.");
  }

  api.addFiles(Object.values(scene.files));
  api.updateScene({
    elements: scene.elements,
    appState: scene.appState,
    captureUpdate: CaptureUpdateAction.NEVER,
  });
  api.history.clear();

  return {
    status: "opened",
    fileName,
    revision: getDocumentRevision(scene.elements, scene.appState),
  };
};

type SaveDocumentOptions = {
  api: DocumentEditorApi;
  bridge: Pick<DesktopBridge, "saveDocument" | "saveDocumentAs">;
  saveAs: boolean;
  closeRequestId?: string;
  serialize?: typeof serializeAsJSON;
};

export type SaveDocumentResult =
  | { status: "cancelled" }
  | { status: "saved"; fileName: string; revision: string };

export const saveDocumentFromEditor = async ({
  api,
  bridge,
  saveAs,
  closeRequestId,
  serialize = serializeAsJSON,
}: SaveDocumentOptions): Promise<SaveDocumentResult> => {
  const elements = api.getSceneElements();
  const appState = api.getAppState();
  // Capture the baseline before awaiting the native save. The user can keep
  // editing while a picker or disk write is in progress.
  const revision = getDocumentRevision(elements, appState);
  const content = serialize(
    elements,
    appState,
    api.getFiles(),
    "local",
  );
  const response = saveAs
    ? await bridge.saveDocumentAs(content, closeRequestId)
    : await bridge.saveDocument(content, closeRequestId);

  return response.status === "saved" ? { ...response, revision } : response;
};
