import React from "react";
import type { ExcalidrawImperativeAPI } from "@excalidraw/excalidraw/types";
import type { DesktopBridge } from "../bridge/DesktopBridge";
import { LibraryController } from "./LibraryController";

export const useEditorLibrary = (excalidrawAPI: ExcalidrawImperativeAPI | null, desktopBridge: DesktopBridge) => {
  const library = React.useRef<LibraryController | null>(null);
  const [libraryFailed, setLibraryFailed] = React.useState(false);

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

  const retryLibrary = () => {
    setLibraryFailed(false);
    library.current?.retry();
  };
  return { library, libraryFailed, retryLibrary };
};
