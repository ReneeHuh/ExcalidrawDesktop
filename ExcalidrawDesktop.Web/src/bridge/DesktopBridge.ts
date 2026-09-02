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
  private readonly themeChangedListeners = new Set<
    (payload: HostEventMap["app.themeChanged"]) => void
  >();
  private readonly imageExportRequestedListeners = new Set<
    (payload: HostEventMap["image.exportRequested"]) => void
  >();
  private pendingTheme?: HostEventMap["app.themeChanged"];
  private pendingDocumentLoad?: HostEventMap["document.loadRequested"];
  private pendingImageExport?: HostEventMap["image.exportRequested"];

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

  public notifyImageExportFailed(exportId: string, message: string) {
    this.notify("image.exportFailed", { exportId, message });
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

  public onImageExportRequested(
    listener: (payload: HostEventMap["image.exportRequested"]) => void,
  ) {
    this.imageExportRequestedListeners.add(listener);
    if (this.pendingImageExport) {
      const pending = this.pendingImageExport;
      this.pendingImageExport = undefined;
      listener(pending);
    }
    return () => {
      this.imageExportRequestedListeners.delete(listener);
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
        return;
      }

      if (
        message.method === "image.exportRequested" &&
        typeof eventPayload === "object" &&
        eventPayload !== null &&
        "exportId" in eventPayload &&
        typeof eventPayload.exportId === "string" &&
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
          eventPayload.exportId,
        ) &&
        "uploadPath" in eventPayload &&
        typeof eventPayload.uploadPath === "string" &&
        eventPayload.uploadPath ===
          `/_desktop/export/${eventPayload.exportId}` &&
        "maxDimension" in eventPayload &&
        typeof eventPayload.maxDimension === "number" &&
        Number.isSafeInteger(eventPayload.maxDimension) &&
        eventPayload.maxDimension > 0 &&
        "maxBytes" in eventPayload &&
        typeof eventPayload.maxBytes === "number" &&
        Number.isSafeInteger(eventPayload.maxBytes) &&
        eventPayload.maxBytes > 0 &&
        "scale" in eventPayload &&
        typeof eventPayload.scale === "number" &&
        Number.isFinite(eventPayload.scale) &&
        eventPayload.scale > 0 &&
        "padding" in eventPayload &&
        typeof eventPayload.padding === "number" &&
        Number.isFinite(eventPayload.padding) &&
        eventPayload.padding >= 0
      ) {
        const exportRequest: HostEventMap["image.exportRequested"] = {
          exportId: eventPayload.exportId,
          uploadPath: eventPayload.uploadPath,
          maxDimension: eventPayload.maxDimension,
          maxBytes: eventPayload.maxBytes,
          scale: eventPayload.scale,
          padding: eventPayload.padding,
        };
        if (this.imageExportRequestedListeners.size === 0) {
          this.pendingImageExport = exportRequest;
        } else {
          this.imageExportRequestedListeners.forEach((listener) =>
            listener(exportRequest),
          );
        }
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
