import { afterEach, describe, expect, it, vi } from "vitest";

import { DesktopBridge, type WebViewTransport } from "./DesktopBridge";
import type { BridgeMessage } from "./BridgeProtocol";

class FakeTransport implements WebViewTransport {
  public posted: BridgeMessage[] = [];
  private listener?: (event: MessageEvent<BridgeMessage>) => void;

  public postMessage(message: BridgeMessage) {
    this.posted.push(message);
  }

  public addEventListener(
    type: "message",
    listener: (event: MessageEvent<BridgeMessage>) => void,
  ) {
    this.listener = listener;
  }

  public respond(message: BridgeMessage) {
    this.listener?.(new MessageEvent("message", { data: message }));
  }
}

const createBridge = () => {
  const transport = new FakeTransport();
  const ownerWindow = {
    chrome: { webview: transport },
    document: window.document,
    crypto: window.crypto,
    setTimeout: window.setTimeout.bind(window),
    clearTimeout: window.clearTimeout.bind(window),
  } as unknown as Window;
  return { bridge: new DesktopBridge(ownerWindow), transport };
};

describe("DesktopBridge", () => {
  it("marks explicit library retries so a declined repair can be offered again", async () => {
    const { bridge, transport } = createBridge();
    const response = bridge.loadLibrary(true);
    const request = transport.posted[0];
    expect(request.payload).toEqual({ retryRepair: true });
    transport.respond({ version: 1, kind: "response", requestId: request.requestId, method: request.method,
      payload: { status: "unavailable" } });
    await expect(response).resolves.toEqual({ status: "unavailable" });
  });

  it("pauses library loading timeout while the native repair decision is open", async () => {
    vi.useFakeTimers();
    const { bridge, transport } = createBridge();
    const response = bridge.loadLibrary();
    const request = transport.posted[0];
    transport.respond({ version: 1, kind: "event", requestId: "progress", method: "document.saveProgress",
      payload: { requestId: request.requestId, isPickerOpen: true } });
    await vi.advanceTimersByTimeAsync(60_000);
    transport.respond({ version: 1, kind: "event", requestId: "progress", method: "document.saveProgress",
      payload: { requestId: request.requestId, isPickerOpen: false } });
    transport.respond({ version: 1, kind: "response", requestId: request.requestId, method: request.method,
      payload: { status: "loaded", content: "[]", revision: "repaired" } });
    await expect(response).resolves.toEqual({ status: "loaded", content: "[]", revision: "repaired" });
  });
  afterEach(() => vi.useRealTimers());
  it("correlates and validates a cancelled document.save response", async () => {
    const { bridge, transport } = createBridge();
    const response = bridge.saveDocument("{}");
    const request = transport.posted[0];

    expect(request.payload).toEqual({ content: "{}" });
    transport.respond({
      version: 1,
      kind: "response",
      requestId: request.requestId,
      method: request.method,
      payload: { status: "cancelled" },
    });

    await expect(response).resolves.toEqual({ status: "cancelled" });
  });

  it("rejects a structured native error", async () => {
    const { bridge, transport } = createBridge();
    const response = bridge.saveDocument("{}");
    const request = transport.posted[0];

    transport.respond({
      version: 1,
      kind: "response",
      requestId: request.requestId,
      method: request.method,
      error: { code: "DocumentInvalid", message: "Invalid drawing." },
    });

    await expect(response).rejects.toMatchObject({
      code: "DocumentInvalid",
      message: "Invalid drawing.",
    });
  });

  it("rejects a response whose method does not match its request", async () => {
    const { bridge, transport } = createBridge();
    const response = bridge.saveDocument("{}");
    const request = transport.posted[0];

    transport.respond({
      version: 1,
      kind: "response",
      requestId: request.requestId,
      method: "app.ping",
      payload: { host: "winui", ready: true },
    });

    await expect(response).rejects.toMatchObject({
      code: "BridgeCorrelationInvalid",
    });
  });

  it("sends and validates a document.save response", async () => {
    const { bridge, transport } = createBridge();
    const response = bridge.saveDocument("serialized scene");
    const request = transport.posted[0];

    expect(request.method).toBe("document.save");
    expect(request.payload).toEqual({ content: "serialized scene" });
    transport.respond({
      version: 1,
      kind: "response",
      requestId: request.requestId,
      method: request.method,
      payload: { status: "saved", fileName: "drawing.excalidraw" },
    });

    await expect(response).resolves.toEqual({
      status: "saved",
      fileName: "drawing.excalidraw",
    });
  });

  it("sends dirty state and a typed document.new request", async () => {
    const { bridge, transport } = createBridge();
    bridge.notifyDocumentDirtyChanged(true);
    const response = bridge.newDocument(true);
    const dirtyEvent = transport.posted[0];
    const request = transport.posted[1];

    expect(dirtyEvent).toMatchObject({
      kind: "event",
      method: "document.dirtyChanged",
      payload: { isDirty: true },
    });
    expect(request).toMatchObject({
      kind: "request",
      method: "document.new",
      payload: { hasUnsavedChanges: true },
    });
    transport.respond({
      version: 1,
      kind: "response",
      requestId: request.requestId,
      method: request.method,
      payload: { status: "created" },
    });

    await expect(response).resolves.toEqual({ status: "created" });
  });

  it("subscribes to native close-save requests", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();
    const unsubscribe = bridge.onSaveRequested(listener);

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "document.saveRequested",
      payload: { reason: "close", closeRequestId: "11111111-1111-4111-8111-111111111111" },
    });
    expect(listener).toHaveBeenCalledWith({ reason: "close", closeRequestId: "11111111-1111-4111-8111-111111111111" });

    unsubscribe();
    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "document.saveRequested",
      payload: { reason: "close", closeRequestId: "11111111-1111-4111-8111-111111111111" },
    });
    expect(listener).toHaveBeenCalledOnce();
  });

  it.each([undefined, "invalid", 123])("ignores close requests with invalid IDs: %s", (closeRequestId) => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();
    bridge.onSaveRequested(listener);
    transport.respond({
      version: 1, kind: "event", requestId: crypto.randomUUID(),
      method: "document.saveRequested", payload: { reason: "close", closeRequestId },
    });
    expect(listener).not.toHaveBeenCalled();
  });

  it("echoes close IDs on both success and cancellation", () => {
    const { bridge, transport } = createBridge();
    const closeRequestId = crypto.randomUUID();
    bridge.notifyCloseReady(closeRequestId);
    bridge.notifyCloseCancelled(closeRequestId);
    expect(transport.posted.slice(-2)).toMatchObject([
      { method: "app.closeReady", payload: { closeRequestId } },
      { method: "app.closeCancelled", payload: { closeRequestId } },
    ]);
  });

  it.each(["save", "saveAs"] as const)(
    "subscribes to native %s menu requests",
    (reason) => {
      const { bridge, transport } = createBridge();
      const listener = vi.fn();
      bridge.onSaveRequested(listener);

      transport.respond({
        version: 1,
        kind: "event",
        requestId: crypto.randomUUID(),
        method: "document.saveRequested",
        payload: { reason },
      });

      expect(listener).toHaveBeenCalledWith({ reason });
    },
  );

  it("delivers validated automation edit events to registered listeners", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();
    const unsubscribe = bridge.onAutomationEditRequested(listener);

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "app.automationEditRequested",
      payload: { elementId: "recovery-test-element" },
    });
    expect(listener).toHaveBeenCalledWith({
      elementId: "recovery-test-element",
    });

    unsubscribe();
  });

  it("delivers the native theme even when it arrives before subscription", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "app.themeChanged",
      payload: { theme: "dark" },
    });

    bridge.onThemeChanged(listener);
    expect(listener).toHaveBeenCalledWith({ theme: "dark" });
  });

  it("delivers and validates a pending native language", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "app.languageChanged",
      payload: { langCode: "ar-SA", direction: "rtl" },
    });

    bridge.onLanguageChanged(listener);
    expect(listener).toHaveBeenCalledWith({
      langCode: "ar-SA",
      direction: "rtl",
    });

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "app.languageChanged",
      payload: { langCode: "../../bad", direction: "rtl" },
    });
    expect(listener).toHaveBeenCalledOnce();
  });

  it("delivers and validates a pending native image export request", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();
    const exportId = "720e34f1-a3ea-4af3-93ed-a950b2357c42";

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "image.exportRequested",
      payload: {
        exportId,
        uploadUrl:
          `https://export-9d32585b1ac74f489ef08b7a55d49570.excalidraw.local/_desktop/export/${exportId}`,
        maxDimension: 16384,
        maxBytes: 104857600,
        scale: 2,
        padding: 10,
      },
    });

    bridge.onImageExportRequested(listener);
    bridge.notifyImageExportFailed(exportId, "render failed");

    expect(listener).toHaveBeenCalledWith(
      expect.objectContaining({ exportId, scale: 2, padding: 10 }),
    );
    expect(transport.posted.at(-1)).toMatchObject({
      kind: "event",
      method: "image.exportFailed",
      payload: { exportId, message: "render failed" },
    });
  });

  it("rejects an image export URL outside the isolated upload origin", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();
    const exportId = "720e34f1-a3ea-4af3-93ed-a950b2357c42";
    bridge.onImageExportRequested(listener);

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "image.exportRequested",
      payload: {
        exportId,
        uploadUrl: `https://example.com/_desktop/export/${exportId}`,
        maxDimension: 16384,
        maxBytes: 104857600,
        scale: 2,
        padding: 10,
      },
    });

    expect(listener).not.toHaveBeenCalled();
  });

  it("requires a validated disk acknowledgement for recovery", async () => {
    const { bridge, transport } = createBridge();
    const response = bridge.saveRecoverySnapshot("snapshot");
    const request = transport.posted[0];
    expect(request).toMatchObject({ kind: "request", method: "document.recoverySnapshot" });
    const rejected = expect(response).rejects.toThrow("payload is invalid");
    transport.respond({ ...request, kind: "response", payload: { status: "saved" } });
    await rejected;
  });

  it("times out unacknowledged recovery so the caller can retry", async () => {
    vi.useFakeTimers();
    const { bridge } = createBridge();
    const response = expect(bridge.saveRecoverySnapshot("snapshot")).rejects.toMatchObject({ code: "BridgeTimeout" });
    await vi.advanceTimersByTimeAsync(10_000);
    await response;
  });

  it("cancels a lost save and allows a new save without accepting its late reply", async () => {
    vi.useFakeTimers();
    const { bridge, transport } = createBridge();
    const first = bridge.saveDocument("first");
    const old = transport.posted[0];
    const rejected = expect(first).rejects.toMatchObject({ code: "BridgeTimeout" });
    await vi.advanceTimersByTimeAsync(30_000);
    await rejected;
    expect(transport.posted[1]).toMatchObject({ method: "document.cancelSave", payload: { requestId: old.requestId } });
    const second = bridge.saveDocument("second");
    const current = transport.posted[2];
    transport.respond({ ...old, kind: "response", payload: { status: "saved", fileName: "old.excalidraw" } });
    transport.respond({ ...current, kind: "response", payload: { status: "saved", fileName: "new.excalidraw" } });
    await expect(second).resolves.toMatchObject({ fileName: "new.excalidraw" });
  });

  it("pauses save deadlines only for the owning native picker", async () => {
    vi.useFakeTimers();
    const { bridge, transport } = createBridge();
    const response = bridge.saveDocumentAs("drawing");
    const request = transport.posted[0];
    const progress = (isPickerOpen: boolean) => transport.respond({
      version: 1, kind: "event", requestId: crypto.randomUUID(), method: "document.saveProgress",
      payload: { requestId: request.requestId, isPickerOpen },
    });
    progress(true);
    await vi.advanceTimersByTimeAsync(120_000);
    expect(transport.posted).toHaveLength(1);
    progress(false);
    const rejected = expect(response).rejects.toMatchObject({ code: "BridgeTimeout" });
    await vi.advanceTimersByTimeAsync(30_000);
    await rejected;
  });

  it("propagates host close cancellation only to the matching save", async () => {
    const { bridge, transport } = createBridge();
    const closeRequestId = crypto.randomUUID();
    const first = bridge.saveDocument("drawing", closeRequestId);
    const rejected = expect(first).rejects.toMatchObject({ code: "SaveCancelled" });
    transport.respond({ version: 1, kind: "event", requestId: crypto.randomUUID(),
      method: "document.saveCancelled", payload: { closeRequestId } });
    await rejected;
    const second = bridge.saveDocument("new", crypto.randomUUID());
    const current = transport.posted.at(-1)!;
    transport.respond({ version: 1, kind: "event", requestId: crypto.randomUUID(),
      method: "document.saveCancelled", payload: { closeRequestId } });
    transport.respond({ ...current, kind: "response", payload: { status: "saved", fileName: "new.excalidraw" } });
    await expect(second).resolves.toMatchObject({ status: "saved" });
  });

  it("settles a save when sending the request throws", async () => {
    const { bridge, transport } = createBridge();
    vi.spyOn(transport, "postMessage").mockImplementation(() => { throw new Error("closed transport"); });
    await expect(bridge.saveDocument("drawing")).rejects.toMatchObject({ code: "BridgeUnavailable" });
  });

  it("sends typed workspace tab events", () => {
    const { bridge, transport } = createBridge();

    bridge.requestNewTab();
    bridge.requestOpenDocument();
    bridge.requestSelectAdjacentTab("previous");
    bridge.requestCloseTab();
    bridge.notifyCloseCancelled("11111111-1111-4111-8111-111111111111");
    bridge.notifyLanguageApplied("ar-SA", "rtl");
    bridge.notifyDocumentRecovered();
    bridge.notifyDocumentLoadFailed();

    expect(transport.posted).toMatchObject([
      { kind: "event", method: "workspace.newTabRequested" },
      { kind: "event", method: "workspace.openRequested" },
      {
        kind: "event",
        method: "workspace.selectAdjacentTabRequested",
        payload: { direction: "previous" },
      },
      { kind: "event", method: "workspace.closeTabRequested" },
      { kind: "event", method: "app.closeCancelled" },
      {
        kind: "event",
        method: "app.languageApplied",
        payload: { langCode: "ar-SA", direction: "rtl" },
      },
      { kind: "event", method: "document.recovered" },
      { kind: "event", method: "document.loadFailed" },
    ]);
  });

  it("delivers a native document load even when it arrives before subscription", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "document.loadRequested",
      payload: {
        fileName: "drawing.excalidraw",
        content: "{}",
        isRecovery: true,
      },
    });

    bridge.onDocumentLoadRequested(listener);
    expect(listener).toHaveBeenCalledWith({
      fileName: "drawing.excalidraw",
      content: "{}",
      isRecovery: true,
    });
  });
});
