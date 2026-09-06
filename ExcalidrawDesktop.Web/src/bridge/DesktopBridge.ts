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
import { isSupportedLanguageCode } from "../localization/DesktopStrings";

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
  closeRequestId?: string;
  cancel?: (code: string) => void;
  setPickerOpen?: (open: boolean) => void;
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
  private readonly closeBarrierListeners = new Set<
    (payload: HostEventMap["document.closeBarrierRequested"]) => void
  >();
  private readonly loadCancelledListeners = new Set<(payload: { loadId: string }) => void>();
  private readonly libraryChangedListeners = new Set<(payload: { content: string; revision: string }) => void>();
  private readonly loadRequestedListeners = new Set<
    (payload: HostEventMap["document.loadRequested"]) => void
  >();
  private readonly themeChangedListeners = new Set<
    (payload: HostEventMap["app.themeChanged"]) => void
  >();
  private readonly languageChangedListeners = new Set<
    (payload: HostEventMap["app.languageChanged"]) => void
  >();
  private readonly automationEditRequestedListeners = new Set<
    (payload: HostEventMap["app.automationEditRequested"]) => void
  >();
  private readonly imageExportRequestedListeners = new Set<
    (payload: HostEventMap["image.exportRequested"]) => void
  >();
  private readonly imageExportCancelListeners = new Set<
    (payload: HostEventMap["image.exportCancelRequested"]) => void
  >();
  private pendingTheme?: HostEventMap["app.themeChanged"];
  private pendingLanguage?: HostEventMap["app.languageChanged"];
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

  public notifyLanguageApplied(
    langCode: string,
    direction: "ltr" | "rtl",
  ) {
    this.notify("app.languageApplied", { langCode, direction });
  }

  public notifyCloseReady(closeRequestId: string) {
    this.notify("app.closeReady", { closeRequestId });
  }

  public notifyCloseCancelled(closeRequestId: string) {
    this.notify("app.closeCancelled", { closeRequestId });
  }

  public notifyCloseBarrierReady(barrierId: string, isDirty: boolean, canClose = true) {
    this.transport?.postMessage({
      version: 1,
      kind: "event",
      requestId: this.ownerWindow.crypto.randomUUID(),
      method: "document.closeBarrierReady",
      payload: { barrierId, isDirty, canClose },
    } as BridgeMessage);
  }

  public onCloseBarrierRequested(
    listener: (payload: HostEventMap["document.closeBarrierRequested"]) => void,
  ) {
    this.closeBarrierListeners.add(listener);
    return () => this.closeBarrierListeners.delete(listener);
  }

  public onDocumentLoadCancelled(listener: (payload: { loadId: string }) => void) {
    this.loadCancelledListeners.add(listener);
    return () => this.loadCancelledListeners.delete(listener);
  }

  public onLibraryChanged(listener: (payload: { content: string; revision: string }) => void) {
    this.libraryChangedListeners.add(listener);
    return () => this.libraryChangedListeners.delete(listener);
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

  public notifyLibraryStateChanged(hasUnsavedChanges: boolean) {
    this.notify("library.stateChanged", { hasUnsavedChanges });
  }

  public notifyDocumentOpened(fileName: string) {
    this.notify("document.opened", { fileName });
  }

  public notifyDocumentRecovered() {
    this.notify("document.recovered", undefined);
  }

  public notifyDocumentLoadFailed(loadId?: string) {
    if (loadId) this.notify("document.loadFailed", { loadId });
    else this.transport?.postMessage({ version: 1, kind: "event", requestId: this.ownerWindow.crypto.randomUUID(), method: "document.loadFailed" } as BridgeMessage);
  }

  public notifyDocumentLoadApplied(loadId: string, fileName: string, isRecovery: boolean) {
    this.notify("document.loadApplied", { loadId, fileName, isRecovery });
  }

  public notifyDocumentLoadCancelled(loadId: string) {
    this.notify("document.loadCancelled", { loadId });
  }

  public saveRecoverySnapshot(content: string) {
    return this.request("document.recoverySnapshot", { content }, 10_000);
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

  public onLanguageChanged(
    listener: (payload: HostEventMap["app.languageChanged"]) => void,
  ) {
    this.languageChangedListeners.add(listener);
    if (this.pendingLanguage) {
      const pending = this.pendingLanguage;
      this.pendingLanguage = undefined;
      listener(pending);
    }
    return () => {
      this.languageChangedListeners.delete(listener);
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

  public onImageExportCancelRequested(
    listener: (payload: HostEventMap["image.exportCancelRequested"]) => void,
  ) {
    this.imageExportCancelListeners.add(listener);
    return () => this.imageExportCancelListeners.delete(listener);
  }

  public ping(): Promise<PingResponse> {
    if (!this.transport) {
      return Promise.resolve({ host: "browser-development", ready: true });
    }

    return this.request("app.ping", undefined, 10_000);
  }

  public loadLibrary() {
    if (!this.transport) return Promise.resolve({ status: "unavailable" as const });
    return this.request("library.load", undefined, 10_000);
  }

  public saveLibrary(content: string, expectedRevision: string) {
    if (!this.transport) return Promise.reject(new DesktopBridgeError("LibraryUnavailable", "The shared library is unavailable."));
    return this.request("library.save", { content, expectedRevision }, 10_000);
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

  public saveDocument(content: string, closeRequestId?: string): Promise<DocumentSaveResponse> {
    if (!this.transport) {
      return Promise.resolve({ status: "cancelled" });
    }

    return this.request("document.save", { content, ...(closeRequestId ? { closeRequestId } : {}) }, 30_000, closeRequestId);
  }

  public saveDocumentAs(content: string, closeRequestId?: string): Promise<DocumentSaveResponse> {
    if (!this.transport) {
      return Promise.resolve({ status: "cancelled" });
    }

    return this.request("document.saveAs", { content, ...(closeRequestId ? { closeRequestId } : {}) }, 30_000, closeRequestId);
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

  public onAutomationEditRequested(
    listener: (payload: HostEventMap["app.automationEditRequested"]) => void,
  ) {
    this.automationEditRequestedListeners.add(listener);
    return () => {
      this.automationEditRequestedListeners.delete(listener);
    };
  }

  private request<Method extends BridgeRequestMethod>(
    method: Method,
    payload: BridgeRequestMap[Method]["payload"],
    timeoutMilliseconds?: number,
    closeRequestId?: string,
  ): Promise<BridgeRequestMap[Method]["response"]> {
    if (!this.transport) {
      return Promise.reject(
        new DesktopBridgeError(
          "BridgeUnavailable",
          "The desktop bridge is unavailable.",
        ),
      );
    }

    const requestId = this.ownerWindow.crypto.randomUUID();
    const response = new Promise<BridgeRequestMap[Method]["response"]>(
      (resolve, reject) => {
        const pendingRequest: PendingRequest = {
          method,
          resolve: (responsePayload) => {
            try {
              resolve(parseBridgeResponse(method, responsePayload));
            } catch (error) {
              reject(error);
            }
          },
          reject,
        };

        const clearTimer = () => {
          if (pendingRequest.timeoutId !== undefined) {
            this.ownerWindow.clearTimeout(pendingRequest.timeoutId);
            pendingRequest.timeoutId = undefined;
          }
        };
        pendingRequest.closeRequestId = closeRequestId;
        pendingRequest.cancel = (code) => {
          if (!this.pending.delete(requestId)) {
            return;
          }
          clearTimer();
          if (method === "document.save" || method === "document.saveAs") {
            try {
              this.notify("document.cancelSave", { requestId });
            } catch {
              // The host may already be gone; the caller still needs to settle.
            }
          }
          reject(new DesktopBridgeError(code, `The ${method} request did not complete.`));
        };
        const startTimer = () => {
          clearTimer();
          if (timeoutMilliseconds) {
            pendingRequest.timeoutId = this.ownerWindow.setTimeout(
              () => pendingRequest.cancel?.("BridgeTimeout"), timeoutMilliseconds);
          }
        };
        if (method === "document.save" || method === "document.saveAs") {
          pendingRequest.setPickerOpen = (open) => open ? clearTimer() : startTimer();
        }
        startTimer();

        this.pending.set(requestId, pendingRequest);
      },
    );

    try {
      this.transport.postMessage({
        version: 1,
        kind: "request",
        requestId,
        method,
        ...(payload === undefined ? {} : { payload }),
      });
    } catch {
      this.pending.get(requestId)?.cancel?.("BridgeUnavailable");
    }

    return response;
  }

  private notify<Method extends BridgeEventMethod>(
    method: Method,
    payload: BridgeEventMap[Method],
  ) {
    this.transport?.postMessage({
      version: 1,
      kind: "event",
      requestId: this.ownerWindow.crypto.randomUUID(),
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
      if (typeof eventPayload === "object" && eventPayload !== null) {
        if (message.method === "document.saveProgress" &&
            "requestId" in eventPayload && typeof eventPayload.requestId === "string" &&
            "isPickerOpen" in eventPayload && typeof eventPayload.isPickerOpen === "boolean") {
          this.pending.get(eventPayload.requestId)?.setPickerOpen?.(eventPayload.isPickerOpen);
          return;
        }
        if (message.method === "document.saveCancelled" &&
            "closeRequestId" in eventPayload && typeof eventPayload.closeRequestId === "string") {
          for (const pending of this.pending.values()) {
            if (pending.closeRequestId === eventPayload.closeRequestId) {
              pending.cancel?.("SaveCancelled");
            }
          }
          return;
        }
      }

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
        message.method === "app.languageChanged" &&
        typeof eventPayload === "object" &&
        eventPayload !== null &&
        "langCode" in eventPayload &&
        typeof eventPayload.langCode === "string" &&
        isSupportedLanguageCode(eventPayload.langCode) &&
        "direction" in eventPayload &&
        (eventPayload.direction === "ltr" || eventPayload.direction === "rtl")
      ) {
        const languageUpdate: HostEventMap["app.languageChanged"] = {
          langCode: eventPayload.langCode,
          direction: eventPayload.direction,
        };
        if (this.languageChangedListeners.size === 0) {
          this.pendingLanguage = languageUpdate;
        } else {
          this.languageChangedListeners.forEach((listener) =>
            listener(languageUpdate),
          );
        }
        return;
      }

      if (
        message.method === "app.automationEditRequested" &&
        typeof message.payload === "object" &&
        message.payload !== null &&
        "elementId" in message.payload &&
        typeof message.payload.elementId === "string" &&
        /^[a-z0-9-]{1,64}$/i.test(message.payload.elementId)
      ) {
        const elementId = message.payload.elementId;
        this.automationEditRequestedListeners.forEach((listener) =>
          listener({ elementId }),
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
        const loadId = "loadId" in message.payload && typeof message.payload.loadId === "string" &&
          /^[0-9a-f-]{8,64}$/i.test(message.payload.loadId) ? message.payload.loadId : undefined;
        const payload = {
          ...(loadId ? { loadId } : {}),
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
        (message.method === "image.exportCancelRequested" || message.method === "image.exportCancelled") &&
        typeof message.payload === "object" && message.payload !== null &&
        "exportId" in message.payload && typeof message.payload.exportId === "string" &&
        /^[0-9a-f-]{8,64}$/i.test(message.payload.exportId)
      ) {
        const payload = message.payload as { exportId: string };
        this.imageExportCancelListeners.forEach((listener) =>
          listener({ exportId: payload.exportId }),
        );
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
        const closeRequestId = "closeRequestId" in message.payload &&
          typeof message.payload.closeRequestId === "string" &&
          /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(message.payload.closeRequestId)
            ? message.payload.closeRequestId : undefined;
        if (reason === "close" && !closeRequestId) {
          return;
        }
        this.saveRequestedListeners.forEach((listener) =>
          listener({ reason, ...(closeRequestId ? { closeRequestId } : {}) }),
        );
        return;
      }

      if (
        message.method === "document.closeBarrierRequested" &&
        typeof message.payload === "object" && message.payload !== null &&
        "barrierId" in message.payload && typeof message.payload.barrierId === "string" &&
        /^[0-9a-f-]{8,64}$/i.test(message.payload.barrierId) &&
        "locked" in message.payload && typeof message.payload.locked === "boolean"
      ) {
        const payload = message.payload as { barrierId: string; locked: boolean };
        this.closeBarrierListeners.forEach((listener) => listener({
          barrierId: payload.barrierId,
          locked: payload.locked,
        }));
        return;
      }

      if (message.method === "document.loadCancelled" && typeof message.payload === "object" && message.payload !== null &&
        "loadId" in message.payload && typeof message.payload.loadId === "string") {
        this.loadCancelledListeners.forEach((listener) => listener({ loadId: (message.payload as { loadId: string }).loadId }));
        return;
      }

      if (message.method === "library.changed" && typeof message.payload === "object" && message.payload !== null &&
        "content" in message.payload && typeof message.payload.content === "string" &&
        "revision" in message.payload && typeof message.payload.revision === "string") {
        const payload = message.payload as { content: string; revision: string };
        this.libraryChangedListeners.forEach((listener) => listener(payload));
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
        "uploadUrl" in eventPayload &&
        typeof eventPayload.uploadUrl === "string" &&
        this.isValidImageExportUrl(
          eventPayload.uploadUrl,
          eventPayload.exportId,
        ) &&
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
          uploadUrl: eventPayload.uploadUrl,
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

  private isValidImageExportUrl(value: string, exportId: string) {
    const url = this.ownerWindow.document.createElement("a");
    url.href = value;
    return (
      url.protocol === "https:" &&
      /^export-[0-9a-f]{32}\.excalidraw\.local$/i.test(url.hostname) &&
      url.port === "" &&
      url.username === "" &&
      url.password === "" &&
      url.pathname === `/_desktop/export/${exportId}` &&
      url.search === "" &&
      url.hash === ""
    );
  }
}
