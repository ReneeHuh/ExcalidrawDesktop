export type BridgeKind = "request" | "response" | "event";

export type BridgeError = {
  code: string;
  message: string;
};

export type BridgeMessage = {
  version: 1;
  kind: BridgeKind;
  requestId: string;
  method: string;
  payload?: unknown;
  error?: BridgeError;
};

export type PingResponse = {
  host: "winui" | "browser-development";
  ready: true;
};

export type DocumentOpenResponse =
  | { status: "cancelled" }
  | { status: "opened"; fileName: string; content: string };

export type DocumentSaveResponse =
  | { status: "cancelled" }
  | { status: "saved"; fileName: string };

export type DocumentNewResponse =
  | { status: "cancelled" }
  | { status: "created" };

export type BridgeRequestMap = {
  "app.ping": {
    payload: undefined;
    response: PingResponse;
  };
  "document.open": {
    payload: { hasUnsavedChanges: boolean };
    response: DocumentOpenResponse;
  };
  "document.new": {
    payload: { hasUnsavedChanges: boolean };
    response: DocumentNewResponse;
  };
  "document.save": {
    payload: { content: string };
    response: DocumentSaveResponse;
  };
  "document.saveAs": {
    payload: { content: string };
    response: DocumentSaveResponse;
  };
};

export type BridgeRequestMethod = keyof BridgeRequestMap;

export type BridgeEventMap = {
  "app.ready": undefined;
  "app.languageApplied": {
    langCode: string;
    direction: "ltr" | "rtl";
  };
  "app.closeReady": undefined;
  "app.closeCancelled": undefined;
  "document.created": undefined;
  "document.dirtyChanged": { isDirty: boolean };
  "document.opened": { fileName: string };
  "document.recovered": undefined;
  "document.recoverySnapshot": { content: string };
  "image.exportFailed": {
    exportId: string;
    message: string;
  };
  "workspace.newTabRequested": undefined;
  "workspace.openRequested": undefined;
  "workspace.closeTabRequested": undefined;
  "workspace.selectAdjacentTabRequested": {
    direction: "next" | "previous";
  };
};

export type BridgeEventMethod = keyof BridgeEventMap;

export type HostEventMap = {
  "app.themeChanged": {
    theme: "light" | "dark";
  };
  "app.languageChanged": {
    langCode: string;
    direction: "ltr" | "rtl";
  };
  "app.automationEditRequested": {
    elementId: string;
  };
  "document.saveRequested": {
    reason: "save" | "saveAs" | "close" | "externalConflict";
  };
  "document.loadRequested": {
    fileName: string;
    content: string;
    isRecovery: boolean;
  };
  "image.exportRequested": {
    exportId: string;
    uploadUrl: string;
    maxDimension: number;
    maxBytes: number;
    scale: number;
    padding: number;
  };
};

export type HostEventMethod = keyof HostEventMap;

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

export const parseBridgeResponse = <Method extends BridgeRequestMethod>(
  method: Method,
  payload: unknown,
): BridgeRequestMap[Method]["response"] => {
  if (method === "app.ping") {
    if (
      !isRecord(payload) ||
      (payload.host !== "winui" && payload.host !== "browser-development") ||
      payload.ready !== true
    ) {
      throw new Error("The app.ping response payload is invalid.");
    }
    return payload as BridgeRequestMap[Method]["response"];
  }

  if (method === "document.open") {
    if (!isRecord(payload)) {
      throw new Error("The document.open response payload is invalid.");
    }

    if (payload.status === "cancelled") {
      return { status: "cancelled" } as BridgeRequestMap[Method]["response"];
    }

    if (
      payload.status === "opened" &&
      typeof payload.fileName === "string" &&
      payload.fileName.length > 0 &&
      typeof payload.content === "string"
    ) {
      return {
        status: "opened",
        fileName: payload.fileName,
        content: payload.content,
      } as BridgeRequestMap[Method]["response"];
    }
  }

  if (method === "document.new") {
    if (!isRecord(payload)) {
      throw new Error("The document.new response payload is invalid.");
    }

    if (payload.status === "cancelled" || payload.status === "created") {
      return { status: payload.status } as BridgeRequestMap[Method]["response"];
    }
  }

  if (method === "document.save" || method === "document.saveAs") {
    if (!isRecord(payload)) {
      throw new Error(`The ${method} response payload is invalid.`);
    }

    if (payload.status === "cancelled") {
      return { status: "cancelled" } as BridgeRequestMap[Method]["response"];
    }

    if (
      payload.status === "saved" &&
      typeof payload.fileName === "string" &&
      payload.fileName.length > 0
    ) {
      return {
        status: "saved",
        fileName: payload.fileName,
      } as BridgeRequestMap[Method]["response"];
    }
  }

  throw new Error(`The ${method} response payload is invalid.`);
};
