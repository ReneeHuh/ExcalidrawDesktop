import React from "react";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";
import { DesktopBridge, DesktopBridgeError } from "../bridge/DesktopBridge";
import { getDesktopErrorString, getDesktopString } from "../localization/DesktopStrings";
import { exportWholeDrawingAsPng } from "./ImageExportController";

export const useImageExport = (
  mountNode: HTMLElement,
  desktopBridge: DesktopBridge,
  excalidrawAPI: ExcalidrawImperativeAPI | null,
  langCode: string,
) => {
  const ownerWindow = mountNode.ownerDocument.defaultView;
  if (!ownerWindow) throw new Error("The editor window is unavailable.");
  const activeExport = React.useRef<{ exportId: string; controller: AbortController } | null>(null);

  React.useEffect(() => {
    if (!excalidrawAPI) {
      return;
    }

    return desktopBridge.onImageExportRequested((request) => {
      if (activeExport.current) {
        desktopBridge.notifyImageExportFailed(
          request.exportId,
          getDesktopString(langCode, "exportRunning"),
        );
        return;
      }

      const controller = new ownerWindow.AbortController();
      activeExport.current = { exportId: request.exportId, controller };
      void exportWholeDrawingAsPng(excalidrawAPI, request, ownerWindow, undefined, controller.signal)
        .catch((error: unknown) => {
          if (controller.signal.aborted || activeExport.current?.exportId !== request.exportId) return;
          const message = getDesktopErrorString(
            langCode,
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
  }, [excalidrawAPI, langCode, desktopBridge, ownerWindow]);

  React.useEffect(() => {
    const unsubscribe = desktopBridge.onImageExportCancelRequested(({ exportId }) => {
      if (activeExport.current?.exportId === exportId) {
        activeExport.current.controller.abort();
      }
    });
    return () => { unsubscribe(); };
  }, [desktopBridge]);


  React.useEffect(() => () => {
    activeExport.current?.controller.abort();
    activeExport.current = null;
  }, []);
};
