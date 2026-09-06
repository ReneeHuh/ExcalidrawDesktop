import type { ExcalidrawElement } from "@excalidraw/excalidraw/element/types";
import type { AppState } from "@excalidraw/excalidraw/types";

/** The drawing content used for dirty state and recovery deduplication. */
export const getDocumentRevision = (
  elements: readonly ExcalidrawElement[],
  appState: Partial<AppState>,
): string =>
  JSON.stringify({
    // onChange includes tombstones; getSceneElements() and saved files do not.
    elements: elements.filter((element) => !element.isDeleted)
      .map((element) => [element.id, element.version, element.versionNonce]),
    // These are the appState fields written by Excalidraw's local serializer.
    // Keep this small: onChange also fires for pointer and viewport activity.
    viewBackgroundColor: appState.viewBackgroundColor,
    gridModeEnabled: appState.gridModeEnabled,
    gridSize: appState.gridSize,
    gridStep: appState.gridStep,
  });
