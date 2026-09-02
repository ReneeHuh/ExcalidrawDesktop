import {
  type BridgeEventMap,
  type BridgeEventMethod,
  type BridgeMessage,
  type BridgeRequestMap,
  type BridgeRequestMethod,
  type DocumentOpenResponse,
  type DocumentNewResponse,
  type DocumentSaveResponse,
  type HostEventMap,
  parseBridgeResponse,
  type PingResponse,
} from "./BridgeProtocol";

export type WebViewTransport = {
  postMessage(message: BridgeMessage): void;
  addEventListener(
    type: "message",
    listener: (event: MessageEvent<BridgeMessage>) => void,
  ): void;
};

declare global {
  interface Window {
    EXCALIDRAW_ASSET_PATH: string | string[] | undefined;
    chrome?: {
      webview?: WebViewTransport;
    };
  }
}

type PendingRequest = {
  method: BridgeRequestMethod;
  resolve: (payload: unknown) => void;
  reject: (error: Error) => void;
  timeoutId?: number;
};

export class DesktopBridgeError extends Error {
  public constructor(public readonly code: string, message: string) {
    super(message);
    this.name = "DesktopBridgeError";
  }
}

export class DesktopBridge {
  private readonly transport: WebViewTransport | undefined;
  private readonly pending = new Map<string, PendingRequest>();
  private readonly saveRequestedListeners = new Set<
    (payload: HostEventMap["document.saveRequested"]) => void
  >();
  private readonly loadRequestedListeners = new Set<
    (payload: HostEventMap["document.loadRequested"]) => void
  >();
  private readonly statusChangedListeners = new Set<
    (payload: HostEventMap["document.statusChanged"]) => void
  >();
  private readonly themeChangedListeners = new Set<
    (payload: HostEventMap["app.themeChanged"]) => void
  >();
  private pendingTheme?: HostEventMap["app.themeChanged"];
  private pendingDocumentLoad?: HostEventMap["document.loadRequested"];

  public constructor(private readonly ownerWindow: Window) {
    this.transport = ownerWindow.chrome?.webview;
    this.transport?.addEventListener("message", (event) => {
      this.handleMessage(event.data);
    });
  }

  public notifyReady() {
    this.notify("app.ready", undefined);
  }

  public notifyCloseReady() {
    this.notify("app.closeReady", undefined);
  }

  public notifyCloseCancelled() {
    this.notify("app.closeCancelled", undefined);
  }

  public requestNewTab() {
    this.notify("workspace.newTabRequested", undefined);
  }

  public requestOpenDocument() {
    this.notify("workspace.openRequested", undefined);
  }

  public requestCloseTab() {
    this.notify("workspace.closeTabRequested", undefined);
  }

  public requestSelectAdjacentTab(direction: "next" | "previous") {
    this.notify("workspace.selectAdjacentTabRequested", { direction });
  }

  public notifyDocumentCreated() {
    this.notify("document.created", undefined);
  }

  public notifyDocumentDirtyChanged(isDirty: boolean) {
    this.notify("document.dirtyChanged", { isDirty });
  }

  public notifyDocumentOpened(fileName: string) {
    this.notify("document.opened", { fileName });
  }

  public notifyDocumentRecovered() {
    this.notify("document.recovered", undefined);
  }

  public notifyRecoverySnapshot(content: string) {
    this.notify("document.recoverySnapshot", { content });
  }

  public requestResolveExternalConflict() {
    this.notify("document.resolveExternalConflict", undefined);
  }

  public onDocumentStatusChanged(
    listener: (payload: HostEventMap["document.statusChanged"]) => void,
  ) {
    this.statusChangedListeners.add(listener);
    return () => {
      this.statusChangedListeners.delete(listener);
    };
  }

  public onThemeChanged(
    listener: (payload: HostEventMap["app.themeChanged"]) => void,
  ) {
    this.themeChangedListeners.add(listener);
    if (this.pendingTheme) {
      const pending = this.pendingTheme;
      this.pendingTheme = undefined;
      listener(pending);
    }
    return () => {
      this.themeChangedListeners.delete(listener);
    };
  }

  public ping(): Promise<PingResponse> {
    if (!this.transport) {
      return Promise.resolve({ host: "browser-development", ready: true });
    }

    return this.request("app.ping", undefined, 10_000);
  }

  public openDocument(
    hasUnsavedChanges: boolean,
  ): Promise<DocumentOpenResponse> {
    if (!this.transport) {
      return Promise.resolve({ status: "cancelled" });
    }

    return this.request("document.open", { hasUnsavedChanges });
  }

  public newDocument(hasUnsavedChanges: boolean): Promise<DocumentNewResponse> {
    if (!this.transport) {
      return Promise.resolve({ status: "created" });
    }

    return this.request("document.new", { hasUnsavedChanges });
  }

  public saveDocument(content: string): Promise<DocumentSaveResponse> {
    if (!this.transport) {
      return Promise.resolve({ status: "cancelled" });
    }

    return this.request("document.save", { content });
  }

  public saveDocumentAs(content: string): Promise<DocumentSaveResponse> {
    if (!this.transport) {
      return Promise.resolve({ status: "cancelled" });
    }

    return this.request("document.saveAs", { content });
  }

  public onSaveRequested(
    listener: (payload: HostEventMap["document.saveRequested"]) => void,
  ) {
    this.saveRequestedListeners.add(listener);
    return () => {
      this.saveRequestedListeners.delete(listener);
    };
  }

  public onDocumentLoadRequested(
    listener: (payload: HostEventMap["document.loadRequested"]) => void,
  ) {
    this.loadRequestedListeners.add(listener);
    if (this.pendingDocumentLoad) {
      const pending = this.pendingDocumentLoad;
      this.pendingDocumentLoad = undefined;
      listener(pending);
    }
    return () => {
      this.loadRequestedListeners.delete(listener);
    };
  }

  private request<Method extends BridgeRequestMethod>(
    method: Method,
    payload: BridgeRequestMap[Method]["payload"],
    timeoutMilliseconds?: number,
  ): Promise<BridgeRequestMap[Method]["response"]> {
    if (!this.transport) {
      return Promise.reject(
        new DesktopBridgeError(
          "BridgeUnavailable",
          "The desktop bridge is unavailable.",
        ),
      );
    }

    const requestId = crypto.randomUUID();
    const response = new Promise<BridgeRequestMap[Method]["response"]>(
      (resolve, reject) => {
        const pendingRequest: PendingRequest = {
          method,
          resolve: (responsePayload) =>
            resolve(parseBridgeResponse(method, responsePayload)),
          reject,
        };

        if (timeoutMilliseconds) {
          pendingRequest.timeoutId = this.ownerWindow.setTimeout(() => {
            this.pending.delete(requestId);
            reject(
              new DesktopBridgeError(
                "BridgeTimeout",
                `The ${method} request timed out.`,
              ),
            );
          }, timeoutMilliseconds);
        }

        this.pending.set(requestId, pendingRequest);
      },
    );

    this.transport.postMessage({
      version: 1,
      kind: "request",
      requestId,
      method,
      ...(payload === undefined ? {} : { payload }),
    });

    return response;
  }

  private notify<Method extends BridgeEventMethod>(
    method: Method,
    payload: BridgeEventMap[Method],
  ) {
    this.transport?.postMessage({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method,
      ...(payload === undefined ? {} : { payload }),
    });
  }

  private handleMessage(message: BridgeMessage) {
    if (message?.version !== 1) {
      return;
    }

    if (message.kind === "event") {
      const eventPayload = message.payload;
      if (
        message.method === "app.themeChanged" &&
        typeof eventPayload === "object" &&
        eventPayload !== null &&
        "theme" in eventPayload &&
        (eventPayload.theme === "light" || eventPayload.theme === "dark")
      ) {
        const themeUpdate: HostEventMap["app.themeChanged"] = {
          theme: eventPayload.theme,
        };
        if (this.themeChangedListeners.size === 0) {
          this.pendingTheme = themeUpdate;
        } else {
          this.themeChangedListeners.forEach((listener) =>
            listener(themeUpdate),
          );
        }
        return;
      }

      if (
        message.method === "document.statusChanged" &&
        typeof eventPayload === "object" &&
        eventPayload !== null &&
        "document" in eventPayload &&
        typeof eventPayload.document === "string" &&
        "status" in eventPayload &&
        typeof eventPayload.status === "string" &&
        "actionable" in eventPayload &&
        typeof eventPayload.actionable === "boolean"
      ) {
        const statusUpdate: HostEventMap["document.statusChanged"] = {
          document: eventPayload.document as string,
          status: eventPayload.status as string,
          actionable: eventPayload.actionable as boolean,
        };
        this.statusChangedListeners.forEach((listener) =>
          listener(statusUpdate),
        );
        return;
      }

      if (
        message.method === "document.loadRequested" &&
        typeof message.payload === "object" &&
        message.payload !== null &&
        "fileName" in message.payload &&
        typeof message.payload.fileName === "string" &&
        message.payload.fileName.length > 0 &&
        "content" in message.payload &&
        typeof message.payload.content === "string" &&
        "isRecovery" in message.payload &&
        typeof message.payload.isRecovery === "boolean"
      ) {
        const payload = {
          fileName: message.payload.fileName,
          content: message.payload.content,
          isRecovery: message.payload.isRecovery,
        };
        if (this.loadRequestedListeners.size === 0) {
          this.pendingDocumentLoad = payload;
        } else {
          this.loadRequestedListeners.forEach((listener) => listener(payload));
        }
        return;
      }

      if (
        message.method === "document.saveRequested" &&
        typeof message.payload === "object" &&
        message.payload !== null &&
        "reason" in message.payload &&
        (message.payload.reason === "save" ||
          message.payload.reason === "saveAs" ||
          message.payload.reason === "close" ||
          message.payload.reason === "externalConflict")
      ) {
        const reason = message.payload.reason;
        this.saveRequestedListeners.forEach((listener) =>
          listener({ reason }),
        );
      }
      return;
    }

    if (message.kind !== "response") {
      return;
    }

    const pendingRequest = this.pending.get(message.requestId);
    if (!pendingRequest) {
      return;
    }

    this.pending.delete(message.requestId);
    if (pendingRequest.timeoutId !== undefined) {
      this.ownerWindow.clearTimeout(pendingRequest.timeoutId);
    }

    if (message.method !== pendingRequest.method) {
      pendingRequest.reject(
        new DesktopBridgeError(
          "BridgeCorrelationInvalid",
          "The desktop bridge response method did not match its request.",
        ),
      );
    } else if (message.error) {
      pendingRequest.reject(
        new DesktopBridgeError(message.error.code, message.error.message),
      );
    } else {
      pendingRequest.resolve(message.payload);
    }
  }
}
