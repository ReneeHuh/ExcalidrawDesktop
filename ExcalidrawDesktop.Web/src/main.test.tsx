import React, { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DesktopBridge, type WebViewTransport } from "./bridge/DesktopBridge";
import type { BridgeMessage } from "./bridge/BridgeProtocol";

const fixture = vi.hoisted(() => ({ props: {} as any, api: {} as any,
  elements: [] as any[], appState: { viewBackgroundColor: "#ffffff" } as any,
  load: vi.fn(), renderPng: vi.fn() }));
vi.mock("@excalidraw/excalidraw", () => ({
  CaptureUpdateAction: { IMMEDIATELY: "immediately", NEVER: "never" },
  THEME: { LIGHT: "light", DARK: "dark" },
  loadFromBlob: (...args: unknown[]) => fixture.load(...args),
  serializeAsJSON: (elements: unknown, appState: unknown) => JSON.stringify({ type: "excalidraw", elements, appState, files: {} }),
  exportToBlob: (...args: unknown[]) => fixture.renderPng(...args),
  getCommonBounds: () => [0, 0, 10, 10],
  Excalidraw: (props: any) => {
    fixture.props = props;
    React.useLayoutEffect(() => {
      props.excalidrawAPI(fixture.api);
      props.onChange(fixture.elements, fixture.appState);
    }, []);
    return <div className="excalidraw" data-readonly={String(props.viewModeEnabled)}>{props.children}</div>;
  },
  MainMenu: Object.assign(({ children }: any) => <div>{children}</div>, {
    Item: ({ children, onSelect }: any) => <button onClick={onSelect}>{children}</button>,
    Separator: () => null,
    DefaultItems: { SearchMenu: () => null, Help: () => null, ClearCanvas: () => null, ToggleTheme: () => null, ChangeCanvasBackground: () => null },
  }),
}));
import { DesktopApp } from "./main";

const id = () => crypto.randomUUID();
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
};

describe("mounted editor and native bridge orchestration", () => {
  let host: HTMLDivElement;
  let root: Root;
  let messages: BridgeMessage[];
  let listeners: Array<(event: MessageEvent<BridgeMessage>) => void>;
  const deliver = (message: BridgeMessage) => listeners.forEach(listener => listener({ data: message } as MessageEvent<BridgeMessage>));
  const emit = async (method: string, payload: unknown) => {
    await act(async () => deliver({ version: 1, kind: "event", requestId: id(), method, payload }));
  };
  const reply = async (request: BridgeMessage, payload: unknown) => {
    await act(async () => deliver({ ...request, kind: "response", payload }));
  };
  const advance = async (milliseconds: number) => act(async () => { await vi.advanceTimersByTimeAsync(milliseconds); });
  const edit = async (elementId: string) => act(async () => {
    fixture.elements = [{ id: elementId, version: 1, versionNonce: 1, isDeleted: false }];
    fixture.props.onChange(fixture.elements, fixture.appState);
  });
  const requests = (method: string) => messages.filter(message => message.kind === "request" && message.method === method);
  const events = (method: string) => messages.filter(message => message.kind === "event" && message.method === method);

  beforeEach(async () => {
    vi.useFakeTimers();
    (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
    messages = []; listeners = [];
    fixture.elements = []; fixture.appState = { viewBackgroundColor: "#ffffff" };
    fixture.load.mockReset(); fixture.renderPng.mockReset();
    fixture.api = {
      getSceneElements: () => fixture.elements, getAppState: () => fixture.appState,
      getFiles: () => ({}), addFiles: vi.fn(), history: { clear: vi.fn() }, setToast: vi.fn(),
      updateLibrary: vi.fn(async ({ libraryItems }) => fixture.props.onLibraryChange(libraryItems)),
      updateScene: vi.fn(({ elements, appState }) => {
        if (elements) fixture.elements = elements;
        if (appState) fixture.appState = appState;
        fixture.props.onChange(fixture.elements, fixture.appState);
      }),
    };
    const transport: WebViewTransport = {
      addEventListener: (_, listener) => listeners.push(listener),
      postMessage: message => {
        messages.push(message);
        if (message.kind !== "request") return;
        const payload = message.method === "app.ping" ? { host: "winui", ready: true } :
          message.method === "library.load" ? { status: "loaded", content: "[]", revision: "empty" } :
          message.method === "document.recoverySnapshot" ? { status: "stored" } : undefined;
        if (payload) queueMicrotask(() => deliver({ ...message, kind: "response", payload }));
      },
    };
    window.chrome = { webview: transport };
    host = document.createElement("div"); document.body.append(host);
    root = createRoot(host);
    const bridge = new DesktopBridge(window);
    await act(async () => root.render(<DesktopApp mountNode={host} bridge={bridge} />));
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove(); delete window.chrome;
    vi.clearAllTimers(); vi.useRealTimers();
  });

  it("keeps newer edits dirty when an older save finishes", async () => {
    await edit("revision-a");
    await emit("document.saveRequested", { reason: "save" });
    const save = requests("document.save")[0];
    expect(save).toBeDefined();
    await edit("revision-b");
    await advance(2000);
    expect((requests("document.recoverySnapshot").at(-1)!.payload as any).content).toContain("revision-b");
    await reply(save, { status: "saved", fileName: "drawing.excalidraw" });
    expect(events("document.dirtyChanged").at(-1)!.payload).toEqual({ isDirty: true });
    await advance(2000);
    expect((requests("document.recoverySnapshot").at(-1)!.payload as any).content).toContain("revision-b");
  });

  it("queues a load behind a save and acknowledges only after clean state is set", async () => {
    await edit("before-save");
    await emit("document.saveRequested", { reason: "save" });
    fixture.load.mockResolvedValue({ elements: [{ id: "loaded", version: 1 }], appState: fixture.appState, files: {} });
    const loadId = id();
    await emit("document.loadRequested", { loadId, fileName: "other.excalidraw", content: "{}", isRecovery: false });
    expect(fixture.load).not.toHaveBeenCalled();
    await reply(requests("document.save")[0], { status: "saved", fileName: "first.excalidraw" });
    await advance(25);
    expect(fixture.elements[0].id).toBe("loaded");
    expect(events("document.loadApplied").at(-1)!.payload).toEqual({ loadId, fileName: "other.excalidraw", isRecovery: false });
    expect(messages.indexOf(events("document.dirtyChanged").at(-1)!)).toBeLessThan(
      messages.indexOf(events("document.loadApplied").at(-1)!));
    expect(events("document.opened")).toHaveLength(0);
  });

  it("cancels a hung load and ignores its late parser result", async () => {
    const parse = deferred<any>(); fixture.load.mockReturnValueOnce(parse.promise);
    const loadId = id();
    await emit("document.loadRequested", { loadId, fileName: "late.excalidraw", content: "{}", isRecovery: false });
    await emit("document.loadCancelled", { loadId });
    await act(async () => parse.resolve({ elements: [{ id: "late" }], appState: fixture.appState, files: {} }));
    expect(fixture.api.updateScene).not.toHaveBeenCalled();
    expect(events("document.loadApplied")).toHaveLength(0);
    expect(events("document.loadFailed").at(-1)!.payload).toEqual({ loadId });
  });

  it("locks editor input before acknowledging close and ignores an old unlock", async () => {
    await edit("dirty");
    const barrierId = id();
    await emit("document.closeBarrierRequested", { barrierId, locked: true });
    await advance(0);
    expect(host.querySelector(".excalidraw")?.getAttribute("data-readonly")).toBe("true");
    expect(events("document.closeBarrierReady").at(-1)!.payload).toEqual({ barrierId, isDirty: true, canClose: true });
    await emit("document.closeBarrierRequested", { barrierId: id(), locked: false });
    expect(host.querySelector(".excalidraw")?.getAttribute("data-readonly")).toBe("true");
    await emit("document.saveRequested", { reason: "save" });
    expect(requests("document.save")).toHaveLength(0);
    await emit("document.closeBarrierRequested", { barrierId, locked: false });
    expect(host.querySelector(".excalidraw")?.getAttribute("data-readonly")).toBe("false");
  });

  it("releases a hung PNG render on native cancellation and permits retry", async () => {
    await edit("png");
    fixture.renderPng.mockReturnValueOnce(new Promise(() => {}));
    fixture.renderPng.mockResolvedValueOnce(new Blob(["png"], { type: "image/png" }));
    window.fetch = vi.fn(async () => ({ ok: true }) as Response);
    const exportId = id();
    const origin = "https://export-9d32585b1ac74f489ef08b7a55d49570.excalidraw.local";
    const request = { exportId, uploadUrl: `${origin}/_desktop/export/${exportId}`, maxDimension: 16384, maxBytes: 10000, scale: 1, padding: 1 };
    await emit("image.exportRequested", request);
    await emit("image.exportCancelled", { exportId: request.exportId, reason: "timeout" });
    const retryId = id();
    await emit("image.exportRequested", { ...request, exportId: retryId, uploadUrl: `${origin}/_desktop/export/${retryId}` });
    expect(fixture.renderPng).toHaveBeenCalledTimes(2);
    expect(window.fetch).toHaveBeenCalledOnce();
    expect(events("image.exportFailed")).toHaveLength(0);
  });

  it("uses the native library revision and does not save programmatic library loads", async () => {
    expect(requests("library.save")).toHaveLength(0);
    await act(async () => fixture.props.onLibraryChange([{ id: "library-item", elements: [] }]));
    const save = requests("library.save")[0];
    expect(save.payload).toEqual({ content: JSON.stringify([{ id: "library-item", elements: [] }]), expectedRevision: "empty" });
    await reply(save, { status: "saved", content: (save.payload as any).content, revision: "saved" });
    expect(requests("library.save")).toHaveLength(1);
  });
});
