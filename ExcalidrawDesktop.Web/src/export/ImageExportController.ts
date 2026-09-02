import { exportToBlob, getCommonBounds } from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";

export type WholeDrawingExportRequest = {
  exportId: string;
  uploadPath: string;
  maxDimension: number;
  maxBytes: number;
  scale: number;
  padding: number;
};

type ImageExportDependencies = {
  exportToBlob: typeof exportToBlob;
  getCommonBounds: typeof getCommonBounds;
  upload?: (path: string, blob: Blob, exportId: string) => Promise<void>;
};

const defaultDependencies: ImageExportDependencies = {
  exportToBlob,
  getCommonBounds,
};

export const exportWholeDrawingAsPng = async (
  api: ExcalidrawImperativeAPI,
  request: WholeDrawingExportRequest,
  ownerWindow: Window,
  dependencies: ImageExportDependencies = defaultDependencies,
) => {
  const elements = api.getSceneElements();
  if (elements.length === 0) {
    throw new Error("Add something to the drawing before exporting it.");
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
    throw new Error(
      `The exported image would exceed the ${request.maxDimension}px dimension limit.`,
    );
  }

  const blob = await dependencies.exportToBlob({
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
  });
  if (blob.type !== "image/png") {
    throw new Error("The editor did not produce a PNG image.");
  }
  if (blob.size === 0 || blob.size > request.maxBytes) {
    throw new Error(
      `The exported PNG is empty or exceeds the ${Math.floor(request.maxBytes / 1024 / 1024)} MB limit.`,
    );
  }

  if (dependencies.upload) {
    await dependencies.upload(request.uploadPath, blob, request.exportId);
    return;
  }

  const response = await ownerWindow.fetch(request.uploadPath, {
    method: "POST",
    headers: {
      "Content-Type": "image/png",
      "X-Excalidraw-Export-Id": request.exportId,
    },
    body: blob,
  });
  if (!response.ok) {
    throw new Error("The PNG could not be written by the desktop host.");
  }
};
