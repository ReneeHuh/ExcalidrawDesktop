import React from "react";
import ReactDOM from "react-dom/client";

import {
  Excalidraw,
  getSceneVersion,
  MainMenu,
  serializeAsJSON,
  THEME,
} from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";
import "@excalidraw/excalidraw/index.css";

import { DesktopBridge } from "./bridge/DesktopBridge";
import {
  loadDocumentContentIntoEditor,
  saveDocumentFromEditor,
} from "./document/DocumentController";
import { exportWholeDrawingAsPng } from "./export/ImageExportController";
import "./styles.css";

const root = document.getElementById("root");

if (!root) {
  throw new Error("The Excalidraw Desktop root element is missing.");
}

const ownerDocument = root.ownerDocument;
const ownerWindow = ownerDocument.defaultView;
if (!ownerWindow) {
  throw new Error("The Excalidraw Desktop window is unavailable.");
}

ownerWindow.EXCALIDRAW_ASSET_PATH = "/";
const desktopBridge = new DesktopBridge(ownerWindow);

const desktopUIOptions = {
  canvasActions: {
    loadScene: false,
    saveToActiveFile: false,
  },
};

const DesktopApp = () => {
  const [theme, setTheme] = React.useState<"light" | "dark">(() =>
    ownerWindow.matchMedia("(prefers-color-scheme: dark)").matches
      ? THEME.DARK
      : THEME.LIGHT,
  );
  const [excalidrawAPI, setExcalidrawAPI] =
    React.useState<ExcalidrawImperativeAPI | null>(null);
  const savedSceneVersion = React.useRef(0);
  const isDirty = React.useRef(false);
  const isDocumentOperationInProgress = React.useRef(false);
  const isImageExportInProgress = React.useRef(false);
  const recoveryTimer = React.useRef<number | undefined>(undefined);

  const updateDirty = React.useCallback((nextIsDirty: boolean) => {
    if (!nextIsDirty && recoveryTimer.current !== undefined) {
      ownerWindow.clearTimeout(recoveryTimer.current);
      recoveryTimer.current = undefined;
    }
    if (isDirty.current === nextIsDirty) {
      return;
    }

    isDirty.current = nextIsDirty;
    desktopBridge.notifyDocumentDirtyChanged(nextIsDirty);
  }, []);

  const scheduleRecoverySnapshot = React.useCallback(() => {
    if (!excalidrawAPI) {
      return;
    }

    if (recoveryTimer.current !== undefined) {
      ownerWindow.clearTimeout(recoveryTimer.current);
    }
    recoveryTimer.current = ownerWindow.setTimeout(() => {
      recoveryTimer.current = undefined;
      if (!isDirty.current) {
        return;
      }

      desktopBridge.notifyRecoverySnapshot(
        serializeAsJSON(
          excalidrawAPI.getSceneElements(),
          excalidrawAPI.getAppState(),
          excalidrawAPI.getFiles(),
          "local",
        ),
      );
    }, 2_000);
  }, [excalidrawAPI]);

  React.useEffect(
    () => () => {
      if (recoveryTimer.current !== undefined) {
        ownerWindow.clearTimeout(recoveryTimer.current);
      }
    },
    [],
  );

  React.useEffect(() => {
    void desktopBridge
      .ping()
      .then(() => desktopBridge.notifyReady())
      .catch((error: unknown) => {
        console.error("The Excalidraw Desktop host did not respond.", error);
      });
  }, []);

  React.useEffect(
    () =>
      desktopBridge.onThemeChanged(({ theme: nextTheme }) => {
        setTheme(nextTheme);
      }),
    [],
  );

  React.useEffect(() => {
    ownerDocument.documentElement.dataset.theme = theme;
  }, [theme]);

  const openDocument = React.useCallback(() => {
    desktopBridge.requestOpenDocument();
  }, []);

  React.useEffect(() => {
    if (!excalidrawAPI) {
      return;
    }

    return desktopBridge.onDocumentLoadRequested(async (payload) => {
      if (isDocumentOperationInProgress.current) {
        return;
      }

      isDocumentOperationInProgress.current = true;
      try {
        const result = await loadDocumentContentIntoEditor({
          api: excalidrawAPI,
          bridge: desktopBridge,
          fileName: payload.fileName,
          content: payload.content,
          notifyOpened: !payload.isRecovery,
        });
        if (result.status === "opened") {
          if (payload.isRecovery) {
            savedSceneVersion.current = -1;
            updateDirty(true);
            desktopBridge.notifyDocumentRecovered();
          } else {
            savedSceneVersion.current = result.sceneVersion;
            updateDirty(false);
          }
        }
      } catch (error: unknown) {
        console.error("The drawing could not be opened.", error);
        excalidrawAPI.setToast({
          message:
            error instanceof Error
              ? error.message
              : "The drawing could not be opened.",
          closable: true,
          duration: 5000,
        });
      } finally {
        isDocumentOperationInProgress.current = false;
      }
    });
  }, [excalidrawAPI, updateDirty]);

  const saveDocument = React.useCallback(
    async (saveAs: boolean): Promise<boolean> => {
      if (!excalidrawAPI || isDocumentOperationInProgress.current) {
        return false;
      }

      isDocumentOperationInProgress.current = true;
      try {
        const result = await saveDocumentFromEditor({
          api: excalidrawAPI,
          bridge: desktopBridge,
          saveAs,
        });
        if (result.status === "saved") {
          savedSceneVersion.current = result.sceneVersion;
          updateDirty(
            getSceneVersion(excalidrawAPI.getSceneElements()) !==
              result.sceneVersion,
          );
          return !isDirty.current;
        }
        return false;
      } catch (error: unknown) {
        console.error("The drawing could not be saved.", error);
        excalidrawAPI.setToast({
          message:
            error instanceof Error
              ? error.message
              : "The drawing could not be saved.",
          closable: true,
          duration: 5000,
        });
        return false;
      } finally {
        isDocumentOperationInProgress.current = false;
      }
    },
    [excalidrawAPI, updateDirty],
  );

  React.useEffect(
    () =>
      desktopBridge.onSaveRequested((request) => {
        void saveDocument(
          request.reason === "saveAs" ||
            request.reason === "externalConflict",
        ).then(
          (isClean) => {
            if (request.reason !== "close") {
              return;
            }
            if (isClean) {
              desktopBridge.notifyCloseReady();
            } else {
              desktopBridge.notifyCloseCancelled();
            }
          },
        );
      }),
    [saveDocument],
  );

  React.useEffect(() => {
    if (!excalidrawAPI) {
      return;
    }

    return desktopBridge.onImageExportRequested((request) => {
      if (isImageExportInProgress.current) {
        desktopBridge.notifyImageExportFailed(
          request.exportId,
          "An image export is already running for this drawing.",
        );
        return;
      }

      isImageExportInProgress.current = true;
      void exportWholeDrawingAsPng(excalidrawAPI, request, ownerWindow)
        .catch((error: unknown) => {
          const message =
            error instanceof Error
              ? error.message
              : "The drawing could not be exported as a PNG.";
          desktopBridge.notifyImageExportFailed(request.exportId, message);
          excalidrawAPI.setToast({
            message,
            closable: true,
            duration: 5000,
          });
        })
        .finally(() => {
          isImageExportInProgress.current = false;
        });
    });
  }, [excalidrawAPI]);

  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (
        (event.key.toLowerCase() === "n" ||
          event.key.toLowerCase() === "t") &&
        (event.ctrlKey || event.metaKey) &&
        !event.altKey &&
        !event.shiftKey
      ) {
        event.preventDefault();
        event.stopImmediatePropagation();
        desktopBridge.requestNewTab();
        return;
      }

      if (
        event.key.toLowerCase() === "w" &&
        (event.ctrlKey || event.metaKey) &&
        !event.altKey &&
        !event.shiftKey
      ) {
        event.preventDefault();
        event.stopImmediatePropagation();
        desktopBridge.requestCloseTab();
        return;
      }

      if (
        event.key === "Tab" &&
        (event.ctrlKey || event.metaKey) &&
        !event.altKey
      ) {
        event.preventDefault();
        event.stopImmediatePropagation();
        desktopBridge.requestSelectAdjacentTab(
          event.shiftKey ? "previous" : "next",
        );
        return;
      }

      if (
        event.key.toLowerCase() === "o" &&
        (event.ctrlKey || event.metaKey) &&
        !event.altKey &&
        !event.shiftKey
      ) {
        event.preventDefault();
        event.stopImmediatePropagation();
        void openDocument();
        return;
      }

      if (
        event.key.toLowerCase() === "s" &&
        (event.ctrlKey || event.metaKey) &&
        !event.altKey
      ) {
        event.preventDefault();
        event.stopImmediatePropagation();
        void saveDocument(event.shiftKey);
      }
    };

    ownerWindow.addEventListener("keydown", handleKeyDown, true);
    return () =>
      ownerWindow.removeEventListener("keydown", handleKeyDown, true);
  }, [openDocument, saveDocument]);

  return (
    <main className="desktop-app" aria-label="Excalidraw Desktop editor">
      <div className="editor-surface">
        <Excalidraw
          excalidrawAPI={setExcalidrawAPI}
          theme={theme}
          UIOptions={desktopUIOptions}
          onChange={(elements) => {
            const nextIsDirty =
              getSceneVersion(elements) !== savedSceneVersion.current;
            updateDirty(nextIsDirty);
            if (nextIsDirty) {
              scheduleRecoverySnapshot();
            }
          }}
        >
        <MainMenu>
          <MainMenu.Item
            onSelect={() => desktopBridge.requestNewTab()}
            shortcut="Ctrl+T"
          >
            New Tab
          </MainMenu.Item>
          <MainMenu.Item onSelect={openDocument} shortcut="Ctrl+O">
            Open…
          </MainMenu.Item>
          <MainMenu.Item onSelect={() => saveDocument(false)} shortcut="Ctrl+S">
            Save
          </MainMenu.Item>
          <MainMenu.Item
            onSelect={() => saveDocument(true)}
            shortcut="Ctrl+Shift+S"
          >
            Save As…
          </MainMenu.Item>
          <MainMenu.Separator />
          <MainMenu.DefaultItems.SearchMenu />
          <MainMenu.DefaultItems.Help />
          <MainMenu.DefaultItems.ClearCanvas />
          <MainMenu.DefaultItems.ToggleTheme allowSystemTheme={false} />
          <MainMenu.DefaultItems.ChangeCanvasBackground />
        </MainMenu>
        </Excalidraw>
      </div>
    </main>
  );
};

ReactDOM.createRoot(root).render(
  <React.StrictMode>
    <DesktopApp />
  </React.StrictMode>,
);
