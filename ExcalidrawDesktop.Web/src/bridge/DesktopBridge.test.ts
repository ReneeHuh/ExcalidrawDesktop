import { describe, expect, it, vi } from "vitest";

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
    setTimeout: window.setTimeout.bind(window),
    clearTimeout: window.clearTimeout.bind(window),
  } as unknown as Window;
  return { bridge: new DesktopBridge(ownerWindow), transport };
};

describe("DesktopBridge", () => {
  it("correlates and validates a cancelled document.open response", async () => {
    const { bridge, transport } = createBridge();
    const response = bridge.openDocument(true);
    const request = transport.posted[0];

    expect(request.payload).toEqual({ hasUnsavedChanges: true });
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
    const response = bridge.openDocument(false);
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
    const response = bridge.openDocument(false);
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
      payload: { reason: "close" },
    });
    expect(listener).toHaveBeenCalledWith({ reason: "close" });

    unsubscribe();
    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "document.saveRequested",
      payload: { reason: "close" },
    });
    expect(listener).toHaveBeenCalledOnce();
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

  it("receives status updates and requests conflict resolution", () => {
    const { bridge, transport } = createBridge();
    const listener = vi.fn();
    bridge.onDocumentStatusChanged(listener);

    transport.respond({
      version: 1,
      kind: "event",
      requestId: crypto.randomUUID(),
      method: "document.statusChanged",
      payload: {
        document: "drawing.excalidraw",
        status: "Changed on disk — Resolve",
        actionable: true,
      },
    });
    bridge.requestResolveExternalConflict();

    expect(listener).toHaveBeenCalledWith({
      document: "drawing.excalidraw",
      status: "Changed on disk — Resolve",
      actionable: true,
    });
    expect(transport.posted.at(-1)).toMatchObject({
      kind: "event",
      method: "document.resolveExternalConflict",
    });
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

  it("sends typed workspace tab events", () => {
    const { bridge, transport } = createBridge();

    bridge.requestNewTab();
    bridge.requestOpenDocument();
    bridge.requestSelectAdjacentTab("previous");
    bridge.requestCloseTab();
    bridge.notifyCloseCancelled();
    bridge.notifyDocumentRecovered();
    bridge.notifyRecoverySnapshot("snapshot");

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
      { kind: "event", method: "document.recovered" },
      {
        kind: "event",
        method: "document.recoverySnapshot",
        payload: { content: "snapshot" },
      },
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
