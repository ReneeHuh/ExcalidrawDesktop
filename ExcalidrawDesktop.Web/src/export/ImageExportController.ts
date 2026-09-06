import { exportToBlob, getCommonBounds } from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";
import { abortable, abortError } from "../bridge/AbortableOperation";

export type WholeDrawingExportRequest = {
  exportId: string;
  uploadUrl: string;
  maxDimension: number;
  maxBytes: number;
  scale: number;
  padding: number;
};

type ImageExportDependencies = {
  exportToBlob: typeof exportToBlob;
  getCommonBounds: typeof getCommonBounds;
  upload?: (path: string, blob: Blob, exportId: string, signal?: AbortSignal) => Promise<void>;
};

const exportError = (code: string, message: string) => Object.assign(new Error(message), { code });

const defaultDependencies: ImageExportDependencies = {
  exportToBlob,
  getCommonBounds,
};

export const exportWholeDrawingAsPng = async (
  api: ExcalidrawImperativeAPI,
  request: WholeDrawingExportRequest,
  ownerWindow: Window,
  dependencies: ImageExportDependencies = defaultDependencies,
  signal?: AbortSignal,
) => {
  if (signal?.aborted) throw abortError();
  const elements = api.getSceneElements();
  if (elements.length === 0) {
    throw exportError("ExportEmpty", "Add something to the drawing before exporting it.");
  }

  const [minX, minY, maxX, maxY] = dependencies.getCommonBounds(elements);
  const width = Math.ceil((maxX - minX + request.padding * 2) * request.scale);
  const height = Math.ceil((maxY - minY + request.padding * 2) * request.scale);
  if (
    !Number.isSafeInteger(width) ||
    !Number.isSafeInteger(height) ||
    width <= 0 ||
    height <= 0 ||
    width > request.maxDimension ||
    height > request.maxDimension
  ) {
    throw exportError("ExportDimensionLimit",
      `The exported image would exceed the ${request.maxDimension}px dimension limit.`,
    );
  }

  const blob = await abortable<Blob>(dependencies.exportToBlob({
    elements,
    files: api.getFiles(),
    appState: {
      ...api.getAppState(),
      exportBackground: true,
      exportEmbedScene: false,
      exportWithDarkMode: false,
    },
    mimeType: "image/png",
    exportPadding: request.padding,
    getDimensions: (sceneWidth: number, sceneHeight: number) => ({
      width: Math.ceil(sceneWidth * request.scale),
      height: Math.ceil(sceneHeight * request.scale),
      scale: request.scale,
    }),
  }), signal);
  if (signal?.aborted) throw abortError();
  if (blob.type !== "image/png") {
    throw exportError("ExportInvalidImage", "The editor did not produce a PNG image.");
  }
  if (blob.size === 0 || blob.size > request.maxBytes) {
    throw exportError("ExportByteLimit",
      `The exported PNG is empty or exceeds the ${Math.floor(request.maxBytes / 1024 / 1024)} MB limit.`,
    );
  }

  if (dependencies.upload) {
    if (signal) {
      await abortable(dependencies.upload(request.uploadUrl, blob, request.exportId, signal), signal);
    } else {
      await dependencies.upload(request.uploadUrl, blob, request.exportId);
    }
    return;
  }

  const response = await abortable(ownerWindow.fetch(request.uploadUrl, {
    method: "POST",
    headers: {
      "Content-Type": "image/png",
      "X-Excalidraw-Export-Id": request.exportId,
    },
    body: blob,
    signal,
  }), signal);
  if (!response.ok) {
    throw exportError("ExportUploadFailed", "The PNG could not be written by the desktop host.");
  }
};
