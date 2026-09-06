import React from "react";
import ReactDOM from "react-dom/client";

import {
  CaptureUpdateAction,
  Excalidraw,
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
import { getDocumentRevision } from "./document/DocumentRevision";
import { RecoverySnapshotController } from "./document/RecoverySnapshotController";
import { DocumentLoadQueue } from "./document/DocumentLoadQueue";
import { LibraryController } from "./document/LibraryController";
import { exportWholeDrawingAsPng } from "./export/ImageExportController";
import {
  getDesktopErrorString,
  getDesktopString,
} from "./localization/DesktopStrings";
import "./styles.css";

const root = document.getElementById("root");

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
  viewBackgroundColor: string;
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
  deleteAllElements(): void;
  isSaving(): boolean;
  addLibraryItem(id: string): void;
  retryLibrary(): void;
  updateAppState(appState: Parameters<ExcalidrawImperativeAPI["updateScene"]>[0]["appState"]): void;
  focusCanvas(): boolean;
};

export const DesktopApp = ({ mountNode, bridge }: { mountNode: HTMLElement; bridge?: DesktopBridge }) => {
  const ownerDocument = mountNode.ownerDocument;
  const ownerWindow = ownerDocument.defaultView;
  if (!ownerWindow) throw new Error("The editor window is unavailable.");
  const desktopBridge = React.useMemo(() => bridge ?? new DesktopBridge(ownerWindow), [bridge, ownerWindow]);
  const [language, setLanguage] = React.useState<{
    langCode: string;
    direction: "ltr" | "rtl";
  }>({ langCode: "en", direction: "ltr" });
  const [theme, setTheme] = React.useState<"light" | "dark">(() =>
    (ownerWindow.matchMedia?.("(prefers-color-scheme: dark)")?.matches ?? false)
      ? THEME.DARK
      : THEME.LIGHT,
  );
  const [excalidrawAPI, setExcalidrawAPI] =
    React.useState<ExcalidrawImperativeAPI | null>(null);
  const library = React.useRef<LibraryController | null>(null);
  const [libraryFailed, setLibraryFailed] = React.useState(false);
  // undefined: initial empty editor; null: recovered content must be saved.
  const savedRevision = React.useRef<string | null | undefined>(undefined);
  const isDirty = React.useRef(false);
  const lastReportedRevision = React.useRef<string | undefined>(undefined);
  const isDocumentOperationInProgress = React.useRef(false);
  const [closeBarrier, setCloseBarrier] = React.useState<{ barrierId: string; locked: boolean } | null>(null);
  const activeExport = React.useRef<{ exportId: string; controller: AbortController } | null>(null);
  const isDocumentLoading = React.useRef(false);
  const recovery = React.useMemo(() => new RecoverySnapshotController(ownerWindow, () => {
    if (!excalidrawAPI || !isDirty.current) {
      return null;
    }
    const elements = excalidrawAPI.getSceneElements();
    const appState = excalidrawAPI.getAppState();
    return {
      revision: getDocumentRevision(elements, appState),
      content: serializeAsJSON(elements, appState, excalidrawAPI.getFiles(), "local"),
    };
  }, (content) => desktopBridge.saveRecoverySnapshot(content)), [excalidrawAPI]);

  const updateDirty = React.useCallback((nextIsDirty: boolean, revision?: string) => {
    if (!nextIsDirty) {
      recovery.reset();
    }
    if (isDirty.current === nextIsDirty &&
      (revision === undefined || revision === lastReportedRevision.current)) {
      return;
    }

    if (revision !== undefined) lastReportedRevision.current = revision;
    isDirty.current = nextIsDirty;
    desktopBridge.notifyDocumentDirtyChanged(nextIsDirty);
  }, [recovery]);

  const scheduleRecoverySnapshot = React.useCallback(
    (revision: string) => recovery.schedule(revision), [recovery]);
  React.useEffect(() => () => recovery.reset(), [recovery]);

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
    if (!excalidrawAPI) return;
    const loads = new DocumentLoadQueue(ownerWindow, {
      busy: () => isDocumentOperationInProgress.current,
      setLoading: (loading) => {
        isDocumentLoading.current = loading;
        isDocumentOperationInProgress.current = loading;
      },
      load: (payload, isCancelled) => loadDocumentContentIntoEditor({
        api: excalidrawAPI,
        fileName: payload.fileName,
        content: payload.content,
        isCancelled,
      }),
      applied: (payload, result) => {
        if (result.status !== "opened") return;
        recovery.reset();
        savedRevision.current = payload.isRecovery ? null : result.revision;
        updateDirty(payload.isRecovery, result.revision);
        if (payload.isRecovery) scheduleRecoverySnapshot(result.revision);
        desktopBridge.notifyDocumentLoadApplied(payload.loadId!, payload.fileName, payload.isRecovery);
      },
      failed: (payload, error) => {
        desktopBridge.notifyDocumentLoadFailed(payload.loadId!);
        excalidrawAPI.setToast({
          message: getDesktopErrorString(language.langCode,
            error instanceof DesktopBridgeError ? error.code : undefined, "openFailed"),
          closable: true,
          duration: 5000,
        });
      },
    });
    const unsubscribe = desktopBridge.onDocumentLoadRequested(payload => loads.enqueue(payload));
    const cancel = desktopBridge.onDocumentLoadCancelled(({ loadId }) => loads.cancel(loadId));
    return () => { unsubscribe(); cancel(); loads.dispose(); };
  }, [excalidrawAPI, language.langCode, recovery, scheduleRecoverySnapshot, updateDirty, desktopBridge]);

  React.useEffect(() => {
    if (!excalidrawAPI) return;
    const controller = new LibraryController(desktopBridge,
      items => excalidrawAPI.updateLibrary({ libraryItems: items as never[], merge: false }),
      () => setLibraryFailed(true),
      pending => desktopBridge.notifyLibraryStateChanged(pending));
    library.current = controller;
    void controller.start();
    const unsubscribe = desktopBridge.onLibraryChanged(snapshot => controller.receive(snapshot));
    return () => {
      unsubscribe();
      controller.dispose();
      if (library.current === controller) library.current = null;
    };
  }, [excalidrawAPI, desktopBridge]);

  React.useEffect(() => {
    const unsubscribe = desktopBridge.onCloseBarrierRequested(({ barrierId, locked }) => {
      setCloseBarrier(current => locked ? { barrierId, locked: true } :
        current?.barrierId === barrierId ? null : current);
    });
    return () => { unsubscribe(); };
  }, []);

  React.useEffect(() => {
    if (!closeBarrier?.locked || !excalidrawAPI) {
      return;
    }
    let cancelled = false;
    const waitForOperation = () => {
      if (cancelled) return;
      if (isDocumentOperationInProgress.current || library.current?.isSaving) {
        ownerWindow.setTimeout(waitForOperation, 25);
        return;
      }
      const revision = getDocumentRevision(
        excalidrawAPI.getSceneElements(), excalidrawAPI.getAppState(),
      );
      desktopBridge.notifyCloseBarrierReady(closeBarrier.barrierId,
        revision !== savedRevision.current, !library.current?.hasUnsavedChanges);
    };
    // Let Excalidraw commit the read-only render before acknowledging the barrier.
    const timer = ownerWindow.setTimeout(waitForOperation, 0);
    return () => {
      cancelled = true;
      ownerWindow.clearTimeout(timer);
    };
  }, [closeBarrier, excalidrawAPI]);

  const saveDocument = React.useCallback(
    async (saveAs: boolean, closeRequestId?: string): Promise<boolean> => {
      if (!excalidrawAPI || isDocumentOperationInProgress.current ||
        (closeBarrier?.locked && !closeRequestId)) {
        return false;
      }

      isDocumentOperationInProgress.current = true;
      try {
        const result = await saveDocumentFromEditor({
          api: excalidrawAPI,
          bridge: desktopBridge,
          saveAs,
          closeRequestId,
        });
        if (result.status === "saved") {
          savedRevision.current = result.revision;
          recovery.reset();
          const currentRevision = getDocumentRevision(
            excalidrawAPI.getSceneElements(),
            excalidrawAPI.getAppState(),
          );
          updateDirty(currentRevision !== result.revision, currentRevision);
          if (isDirty.current) {
            scheduleRecoverySnapshot(currentRevision);
          }
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
    [excalidrawAPI, language.langCode, recovery, scheduleRecoverySnapshot, updateDirty, closeBarrier],
  );

  React.useEffect(
    () =>
      desktopBridge.onSaveRequested((request) => {
        void saveDocument(
          request.reason === "saveAs" ||
            request.reason === "externalConflict",
          request.closeRequestId,
        ).then(
          (isClean) => {
            if (request.reason !== "close" || !request.closeRequestId) {
              return;
            }
            if (isClean) {
              desktopBridge.notifyCloseReady(request.closeRequestId);
            } else {
              desktopBridge.notifyCloseCancelled(request.closeRequestId);
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
      if (closeBarrier?.locked) return;
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
  }, [closeBarrier, excalidrawAPI]);

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
      isSaving: () => isDocumentOperationInProgress.current,
      addLibraryItem: id => { void excalidrawAPI.updateLibrary({ libraryItems: [{
        id, status: "unpublished", created: Date.now(), elements: excalidrawAPI.getSceneElements(),
      }], merge: true }); },
      retryLibrary: () => library.current?.retry(),
      deleteAllElements() {
        excalidrawAPI.updateScene({
          elements: excalidrawAPI.getSceneElementsIncludingDeleted().map((element) => ({
            ...element, isDeleted: true, version: element.version + 1,
          })),
          captureUpdate: CaptureUpdateAction.IMMEDIATELY,
        });
      },
      updateAppState(appState) {
        excalidrawAPI.updateScene({ appState, captureUpdate: CaptureUpdateAction.IMMEDIATELY });
      },
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
          viewBackgroundColor: appState.viewBackgroundColor,
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
      if (activeExport.current) {
        desktopBridge.notifyImageExportFailed(
          request.exportId,
          getDesktopString(language.langCode, "exportRunning"),
        );
        return;
      }

      const controller = new ownerWindow.AbortController();
      activeExport.current = { exportId: request.exportId, controller };
      void exportWholeDrawingAsPng(excalidrawAPI, request, ownerWindow, undefined, controller.signal)
        .catch((error: unknown) => {
          if (controller.signal.aborted || activeExport.current?.exportId !== request.exportId) return;
          const message = getDesktopErrorString(
            language.langCode,
            error instanceof DesktopBridgeError ? error.code :
              (typeof error === "object" && error !== null && "code" in error && typeof error.code === "string" ? error.code : undefined),
            "exportFailed",
            { maxBytes: request.maxBytes },
          );
          desktopBridge.notifyImageExportFailed(request.exportId, message);
          excalidrawAPI.setToast({
            message,
            closable: true,
            duration: 5000,
          });
        })
        .finally(() => {
          if (activeExport.current?.exportId === request.exportId) {
            activeExport.current = null;
          }
        });
    });
  }, [excalidrawAPI, language.langCode]);

  React.useEffect(() => {
    const unsubscribe = desktopBridge.onImageExportCancelRequested(({ exportId }) => {
    if (activeExport.current?.exportId === exportId) {
      activeExport.current?.controller.abort();
    }
    });
    return () => { unsubscribe(); };
  }, []);

  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (closeBarrier?.locked) {
        event.preventDefault();
        event.stopImmediatePropagation();
        return;
      }
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
  }, [closeBarrier, openDocument, saveDocument]);

  return (
    <main
      className="desktop-app"
      aria-label={getDesktopString(language.langCode, "editorLabel")}
    >
      {libraryFailed && <div role="alert" className="library-status">
        {getDesktopString(language.langCode, "libraryUnavailable")}
        <button type="button" onClick={() => { setLibraryFailed(false); library.current?.retry(); }}>
          {getDesktopString(language.langCode, "settingsRetry")}
        </button>
      </div>}
      <div className="editor-surface" style={closeBarrier?.locked ? { position: "relative" } : undefined}>
        {closeBarrier?.locked && <div className="close-barrier-shield" aria-hidden="true" />}
        <Excalidraw
          excalidrawAPI={setExcalidrawAPI}
          langCode={language.langCode}
          theme={theme}
          viewModeEnabled={closeBarrier?.locked ?? false}
          onLibraryChange={items => library.current?.changed(items)}
          UIOptions={desktopUIOptions}
          onChange={(elements, appState) => {
            if (isDocumentLoading.current) return;
            if (savedRevision.current === undefined) {
              savedRevision.current = getDocumentRevision([], appState);
            }
            const revision = getDocumentRevision(elements, appState);
            const nextIsDirty = revision !== savedRevision.current;
            updateDirty(nextIsDirty, revision);
            if (nextIsDirty) {
              scheduleRecoverySnapshot(revision);
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

if (root) {
  const appWindow = root.ownerDocument.defaultView;
  if (!appWindow) throw new Error("The editor window is unavailable.");
  appWindow.EXCALIDRAW_ASSET_PATH = "/";
  const bridge = new DesktopBridge(appWindow);
  ReactDOM.createRoot(root).render(
    <React.StrictMode>
      <DesktopApp mountNode={root} bridge={bridge} />
    </React.StrictMode>,
  );
}
