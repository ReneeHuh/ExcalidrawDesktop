import React from "react";
import ReactDOM from "react-dom/client";

import {
  CaptureUpdateAction,
  Excalidraw,
  getSceneVersion,
  MainMenu,
  serializeAsJSON,
  THEME,
} from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";
import "@excalidraw/excalidraw/index.css";

import { DesktopBridge, DesktopBridgeError } from "./bridge/DesktopBridge";
import {
  loadDocumentContentIntoEditor,
  saveDocumentFromEditor,
} from "./document/DocumentController";
import { exportWholeDrawingAsPng } from "./export/ImageExportController";
import {
  getDesktopErrorString,
  getDesktopString,
} from "./localization/DesktopStrings";
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
ownerDocument.documentElement.lang = "en";
ownerDocument.documentElement.dir = "ltr";
const desktopBridge = new DesktopBridge(ownerWindow);

const desktopUIOptions = {
  canvasActions: {
    loadScene: false,
    saveToActiveFile: false,
  },
};

type DesktopSmokeState = {
  elementIds: string[];
  fileIds: string[];
  scrollX: number;
  scrollY: number;
  zoom: number;
  selectedElementIds: string[];
  indexedDbValue: string | null;
};

type DesktopSmokeApi = {
  configureState(options: {
    elementId: string;
    scrollX: number;
    scrollY: number;
    zoom: number;
    indexedDbValue: string;
  }): void;
  readState(): DesktopSmokeState;
  focusCanvas(): boolean;
};

const DesktopApp = () => {
  const [language, setLanguage] = React.useState<{
    langCode: string;
    direction: "ltr" | "rtl";
  }>({ langCode: "en", direction: "ltr" });
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

  React.useEffect(
    () =>
      desktopBridge.onLanguageChanged((nextLanguage) => {
        setLanguage(nextLanguage);
      }),
    [],
  );

  React.useEffect(() => {
    ownerDocument.documentElement.lang = language.langCode;
    ownerDocument.documentElement.dir = language.direction;
    desktopBridge.notifyLanguageApplied(
      ownerDocument.documentElement.lang,
      ownerDocument.documentElement.dir === "rtl" ? "rtl" : "ltr",
    );
  }, [language]);

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
          message: getDesktopErrorString(
            language.langCode,
            error instanceof DesktopBridgeError ? error.code : undefined,
            "openFailed",
          ),
          closable: true,
          duration: 5000,
        });
      } finally {
        isDocumentOperationInProgress.current = false;
      }
    });
  }, [excalidrawAPI, language.langCode, updateDirty]);

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
          message: getDesktopErrorString(
            language.langCode,
            error instanceof DesktopBridgeError ? error.code : undefined,
            "saveFailed",
          ),
          closable: true,
          duration: 5000,
        });
        return false;
      } finally {
        isDocumentOperationInProgress.current = false;
      }
    },
    [excalidrawAPI, language.langCode, updateDirty],
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
    if (
      !excalidrawAPI ||
      !ownerWindow.location.search
        .slice(1)
        .split("&")
        .includes("desktopSmoke=1")
    ) {
      return;
    }

    return desktopBridge.onAutomationEditRequested(({ elementId }) => {
      const existingElements = excalidrawAPI.getSceneElements();
      excalidrawAPI.updateScene({
        elements: [
          ...existingElements,
          {
            id: elementId,
            type: "rectangle",
            x: 40 + existingElements.length * 20,
            y: 40,
            width: 120,
            height: 80,
            angle: 0,
            strokeColor: "#1e1e1e",
            backgroundColor: "#a5d8ff",
            fillStyle: "solid",
            strokeWidth: 2,
            strokeStyle: "solid",
            roughness: 1,
            opacity: 100,
            groupIds: [],
            frameId: null,
            roundness: { type: 3 },
            seed: 101 + existingElements.length,
            version: 1,
            versionNonce: 201 + existingElements.length,
            isDeleted: false,
            boundElements: [],
            updated: Date.now(),
            link: null,
            locked: false,
          } as never,
        ],
        captureUpdate: CaptureUpdateAction.IMMEDIATELY,
      });
    });
  }, [excalidrawAPI]);

  React.useEffect(() => {
    if (
      !excalidrawAPI ||
      !ownerWindow.location.search
        .slice(1)
        .split("&")
        .includes("desktopSmoke=1")
    ) {
      return;
    }

    const smokeWindow = ownerWindow as typeof ownerWindow & {
      __EXCALIDRAW_DESKTOP_SMOKE__?: DesktopSmokeApi;
    };
    const databaseName = "excalidraw-desktop-isolation-smoke";
    const storeName = "state";
    const storageKey = "tab-state";
    let verifiedIndexedDbValue: string | null = null;
    const openDatabase = () =>
      new Promise<IDBDatabase>((resolve, reject) => {
        const request = ownerWindow.indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => {
          request.result.createObjectStore(storeName);
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });
    const writeIndexedDb = async (value: string) => {
      const database = await openDatabase();
      try {
        await new Promise<void>((resolve, reject) => {
          const transaction = database.transaction(storeName, "readwrite");
          transaction.objectStore(storeName).put(value, storageKey);
          transaction.oncomplete = () => resolve();
          transaction.onerror = () => reject(transaction.error);
          transaction.onabort = () => reject(transaction.error);
        });
      } finally {
        database.close();
      }
    };
    const readIndexedDb = async () => {
      const database = await openDatabase();
      try {
        return await new Promise<string | null>((resolve, reject) => {
          const request = database
            .transaction(storeName, "readonly")
            .objectStore(storeName)
            .get(storageKey);
          request.onsuccess = () =>
            resolve(typeof request.result === "string" ? request.result : null);
          request.onerror = () => reject(request.error);
        });
      } finally {
        database.close();
      }
    };

    smokeWindow.__EXCALIDRAW_DESKTOP_SMOKE__ = {
      configureState(options) {
        void writeIndexedDb(options.indexedDbValue)
          .then(readIndexedDb)
          .then((value) => {
            verifiedIndexedDbValue = value;
          });
        const createRectangle = (id: string, x: number) =>
          ({
            id,
            type: "rectangle",
            x,
            y: 60,
            width: 140,
            height: 90,
            angle: 0,
            strokeColor: "#1e1e1e",
            backgroundColor: "#a5d8ff",
            fillStyle: "solid",
            strokeWidth: 2,
            strokeStyle: "solid",
            roughness: 1,
            opacity: 100,
            groupIds: [],
            frameId: null,
            roundness: { type: 3 },
            seed: x + 301,
            version: 1,
            versionNonce: x + 401,
            isDeleted: false,
            boundElements: [],
            updated: Date.now(),
            link: null,
            locked: false,
          }) as never;
        const baseline = createRectangle(`${options.elementId}-baseline`, -120);
        excalidrawAPI.updateScene({
          elements: [baseline],
          captureUpdate: CaptureUpdateAction.NEVER,
        });
        excalidrawAPI.updateScene({
          elements: [
            baseline,
            createRectangle(options.elementId, 80),
          ],
          appState: {
            scrollX: options.scrollX,
            scrollY: options.scrollY,
            zoom: { value: options.zoom as never },
            selectedElementIds: { [options.elementId]: true },
          },
          captureUpdate: CaptureUpdateAction.IMMEDIATELY,
        });
      },
      readState() {
        const appState = excalidrawAPI.getAppState();
        return {
          elementIds: excalidrawAPI.getSceneElements().map(({ id }) => id),
          fileIds: Object.keys(excalidrawAPI.getFiles()),
          scrollX: appState.scrollX,
          scrollY: appState.scrollY,
          zoom: appState.zoom.value,
          selectedElementIds: Object.keys(appState.selectedElementIds),
          indexedDbValue: verifiedIndexedDbValue,
        };
      },
      focusCanvas() {
        const editor = ownerDocument.querySelector<HTMLElement>(".excalidraw");
        if (!editor) {
          return false;
        }
        editor.tabIndex = 0;
        editor.focus();
        return ownerDocument.activeElement === editor;
      },
    };

    return () => {
      delete smokeWindow.__EXCALIDRAW_DESKTOP_SMOKE__;
    };
  }, [excalidrawAPI]);

  React.useEffect(() => {
    if (!excalidrawAPI) {
      return;
    }

    return desktopBridge.onImageExportRequested((request) => {
      if (isImageExportInProgress.current) {
        desktopBridge.notifyImageExportFailed(
          request.exportId,
          getDesktopString(language.langCode, "exportRunning"),
        );
        return;
      }

      isImageExportInProgress.current = true;
      void exportWholeDrawingAsPng(excalidrawAPI, request, ownerWindow)
        .catch((error: unknown) => {
          const message = getDesktopErrorString(
            language.langCode,
            error instanceof DesktopBridgeError ? error.code : undefined,
            "exportFailed",
          );
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
  }, [excalidrawAPI, language.langCode]);

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
    <main
      className="desktop-app"
      aria-label={getDesktopString(language.langCode, "editorLabel")}
    >
      <div className="editor-surface">
        <Excalidraw
          excalidrawAPI={setExcalidrawAPI}
          langCode={language.langCode}
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
            {getDesktopString(language.langCode, "newTab")}
          </MainMenu.Item>
          <MainMenu.Item onSelect={openDocument} shortcut="Ctrl+O">
            {getDesktopString(language.langCode, "open")}
          </MainMenu.Item>
          <MainMenu.Item onSelect={() => saveDocument(false)} shortcut="Ctrl+S">
            {getDesktopString(language.langCode, "save")}
          </MainMenu.Item>
          <MainMenu.Item
            onSelect={() => saveDocument(true)}
            shortcut="Ctrl+Shift+S"
          >
            {getDesktopString(language.langCode, "saveAs")}
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
