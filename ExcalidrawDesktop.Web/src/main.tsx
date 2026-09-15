import React from "react";
import ReactDOM from "react-dom/client";
import "@excalidraw/excalidraw/index.css";
import "./styles.css";
import { DesktopApp } from "./DesktopApp";
import { DesktopBridge } from "./bridge/DesktopBridge";

const root = document.getElementById("root");
if (root) {
  const ownerWindow = root.ownerDocument.defaultView;
  if (!ownerWindow) throw new Error("The editor window is unavailable.");
  ownerWindow.EXCALIDRAW_ASSET_PATH = "/";
  const bridge = new DesktopBridge(ownerWindow);
  ReactDOM.createRoot(root).render(
    <React.StrictMode>
      <DesktopApp mountNode={root} bridge={bridge} />
    </React.StrictMode>,
  );
}
