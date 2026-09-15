import React from "react";
import { getDesktopShortcut } from "./workspace/DesktopShortcuts";

import {
  Excalidraw,
  MainMenu,
  serializeAsJSON,
  THEME,
} from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";

import { DesktopBridge, DesktopBridgeError } from "./bridge/DesktopBridge";
import {
  loadDocumentContentIntoEditor,
  saveDocumentFromEditor,
} from "./document/DocumentController";
import { getDocumentRevision } from "./document/DocumentRevision";
import { RecoverySnapshotController } from "./document/RecoverySnapshotController";
import { DocumentLoadQueue } from "./document/DocumentLoadQueue";
import { useEditorLibrary } from "./document/useEditorLibrary";
import { useImageExport } from "./export/useImageExport";
import { useDesktopSmoke } from "./testing/useDesktopSmoke";
import {
  getDesktopErrorString,
  getDesktopString,
} from "./localization/DesktopStrings";

const desktopUIOptions = {
  canvasActions: {
    loadScene: false,
    saveToActiveFile: false,
  },
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
  // undefined: initial empty editor; null: recovered content must be saved.
  const savedRevision = React.useRef<string | null | undefined>(undefined);
  const isDirty = React.useRef(false);
  const lastReportedRevision = React.useRef<string | undefined>(undefined);
  const isDocumentOperationInProgress = React.useRef(false);
  const [closeBarrier, setCloseBarrier] = React.useState<{ barrierId: string; locked: boolean } | null>(null);
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

  const { library, libraryFailed, retryLibrary } = useEditorLibrary(excalidrawAPI, desktopBridge);

  React.useEffect(() => {
    const unsubscribe = desktopBridge.onCloseBarrierRequested(({ barrierId, locked }) => {
      setCloseBarrier(current => locked ? { barrierId, locked: true } :
        current?.barrierId === barrierId ? null : current);
    });
    return () => { unsubscribe(); };
  }, []);

  React.useEffect(() => {
    if (!closeBarrier?.locked || !excalidrawAPI) {
      if (excalidrawAPI && isDirty.current) scheduleRecoverySnapshot(
        getDocumentRevision(excalidrawAPI.getSceneElements(), excalidrawAPI.getAppState()));
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
      const dirty = revision !== savedRevision.current;
      // The acknowledgement carries the frozen scene. The host only approves
      // close after writing this exact checkpoint to durable draft storage.
      recovery.reset();
      desktopBridge.notifyCloseBarrierReady(closeBarrier.barrierId, dirty,
        !library.current?.hasUnsavedChanges, dirty ? serializeAsJSON(
          excalidrawAPI.getSceneElements(), excalidrawAPI.getAppState(),
          excalidrawAPI.getFiles(), "local") : undefined);
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

  useDesktopSmoke(mountNode, desktopBridge, excalidrawAPI, closeBarrier?.locked ?? false, isDocumentOperationInProgress, library);
  useImageExport(mountNode, desktopBridge, excalidrawAPI, language.langCode);

  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (closeBarrier?.locked) {
        event.preventDefault();
        event.stopImmediatePropagation();
        return;
      }
      const command = getDesktopShortcut(event);
      if (!command) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      if (event.repeat) return;
      desktopBridge.requestWorkspaceCommand(command);
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
        <button type="button" onClick={retryLibrary}>
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
          <MainMenu.Item onSelect={() => desktopBridge.requestWorkspaceCommand("newWindow")} shortcut="Ctrl+N">
            {getDesktopString(language.langCode, "newWindow")}
          </MainMenu.Item>
          <MainMenu.Item onSelect={() => desktopBridge.requestWorkspaceCommand("reopenClosed")} shortcut="Ctrl+Shift+T">
            {getDesktopString(language.langCode, "reopenClosed")}
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
