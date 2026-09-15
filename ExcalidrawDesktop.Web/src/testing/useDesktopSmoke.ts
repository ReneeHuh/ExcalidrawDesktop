import React from "react";
import { CaptureUpdateAction } from "@excalidraw/excalidraw";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";
import type { DesktopBridge } from "../bridge/DesktopBridge";

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
  updateAppState(appState: Parameters<ExcalidrawImperativeAPI["updateScene"]>[0]["appState"]): void;
  focusCanvas(): boolean;
};

export const useDesktopSmoke = (
  mountNode: HTMLElement,
  desktopBridge: DesktopBridge,
  excalidrawAPI: ExcalidrawImperativeAPI | null,
  isCloseLocked: boolean,
  isDocumentOperationInProgress: React.MutableRefObject<boolean>,
) => {
  const ownerDocument = mountNode.ownerDocument;
  const ownerWindow = ownerDocument.defaultView;
  if (!ownerWindow) throw new Error("The editor window is unavailable.");

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
      if (isCloseLocked) return;
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
  }, [isCloseLocked, excalidrawAPI, desktopBridge, ownerWindow]);

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
  }, [excalidrawAPI, ownerDocument, ownerWindow, isDocumentOperationInProgress]);

};
