import { getSceneVersion } from "@excalidraw/excalidraw";
import type { ExcalidrawElement } from "@excalidraw/excalidraw/element/types";
import type { AppState } from "@excalidraw/excalidraw/types";

/** The drawing content used for dirty state and recovery deduplication. */
export const getDocumentRevision = (
  elements: readonly ExcalidrawElement[],
  appState: Partial<AppState>,
): string =>
  JSON.stringify({
    sceneVersion: getSceneVersion(elements),
    // These are the appState fields written by Excalidraw's local serializer.
    // Keep this small: onChange also fires for pointer and viewport activity.
    viewBackgroundColor: appState.viewBackgroundColor,
    gridModeEnabled: appState.gridModeEnabled,
    gridSize: appState.gridSize,
    gridStep: appState.gridStep,
  });
