import { restoreAppState, serializeAsJSON } from "@excalidraw/excalidraw";
import type { AppState } from "@excalidraw/excalidraw/types";
import { describe, expect, it } from "vitest";

import { getDocumentRevision } from "./DocumentRevision";

const settings: Partial<AppState> = {
  viewBackgroundColor: "#ffffff",
  gridModeEnabled: false,
  gridSize: 20,
  gridStep: 5,
};

describe("getDocumentRevision", () => {
  it.each([
    { viewBackgroundColor: "#ff0000" },
    { gridModeEnabled: true },
    { gridSize: 40 },
    { gridStep: 10 },
  ])("tracks saved drawing settings: %j", (change) => {
    expect(getDocumentRevision([], { ...settings, ...change }))
      .not.toBe(getDocumentRevision([], settings));
  });

  it("ignores viewport, selection, theme, and tool preferences", () => {
    expect(getDocumentRevision([], {
      ...settings,
      scrollX: 150,
      scrollY: -80,
      zoom: { value: 2 as AppState["zoom"]["value"] },
      selectedElementIds: { selected: true },
      selectedGroupIds: { group: true },
      theme: "dark",
      currentItemStrokeColor: "#ff0000",
    })).toBe(getDocumentRevision([], settings));
  });

  it("returns to the saved revision when a background edit is undone", () => {
    const saved = getDocumentRevision([], settings);
    const changed = { ...settings, viewBackgroundColor: "#ff0000" };
    expect(getDocumentRevision([], changed)).not.toBe(saved);
    expect(getDocumentRevision([], { ...changed, viewBackgroundColor: "#ffffff" }))
      .toBe(saved);
  });

  it("tracks element changes as well as settings", () => {
    expect(getDocumentRevision([{ version: 1 } as never], settings))
      .not.toBe(getDocumentRevision([], settings));
  });

  it("stays clean after saving a scene containing deleted shapes", () => {
    const visible = { version: 3, isDeleted: false } as never;
    const deleted = { version: 7, isDeleted: true } as never;
    const saved = getDocumentRevision([visible], settings);
    expect(getDocumentRevision([visible, deleted], settings)).toBe(saved);
    expect(getDocumentRevision([visible, deleted], { ...settings, scrollX: 100 }))
      .toBe(saved);
  });

  it("tracks deleting the last shape and restoring it through undo", () => {
    const visible = { version: 3, isDeleted: false } as never;
    const deleted = { version: 4, isDeleted: true } as never;
    expect(getDocumentRevision([deleted], settings))
      .not.toBe(getDocumentRevision([visible], settings));
    const savedEmpty = getDocumentRevision([], settings);
    expect(getDocumentRevision([deleted], settings)).toBe(savedEmpty);
    expect(getDocumentRevision([{ version: 5, isDeleted: false } as never], settings))
      .not.toBe(savedEmpty);
    expect(getDocumentRevision([{ version: 6, isDeleted: true } as never], settings))
      .toBe(savedEmpty);
  });

  it("does not confuse a deletion plus an edit with the saved scene", () => {
    const saved = [
      { id: "a", version: 1, isDeleted: false },
      { id: "b", version: 1, isDeleted: false },
    ] as never;
    const changed = [
      { id: "a", version: 2, isDeleted: true },
      { id: "b", version: 2, isDeleted: false },
    ] as never;
    expect(getDocumentRevision(changed, settings)).not.toBe(getDocumentRevision(saved, settings));
  });

  it("tracks replacement, stacking order, and equal-version edits", () => {
    const a = { id: "a", version: 2, versionNonce: 10 } as never;
    const b = { id: "b", version: 2, versionNonce: 20 } as never;
    expect(getDocumentRevision([a], settings)).not.toBe(getDocumentRevision([b], settings));
    expect(getDocumentRevision([a, b], settings)).not.toBe(getDocumentRevision([b, a], settings));
    expect(getDocumentRevision([{ id: "a", version: 2, versionNonce: 11 } as never], settings))
      .not.toBe(getDocumentRevision([a], settings));
  });

  it("tracks every appState field exported by the installed editor", () => {
    // Start from the library's complete default state so this catches newly
    // exported settings when the editor dependency is upgraded.
    const appState = restoreAppState(settings, null);
    const exported = JSON.parse(serializeAsJSON([], appState, {}, "local")).appState;
    const tracked = JSON.parse(getDocumentRevision([], appState));
    delete tracked.elements;
    expect(tracked).toEqual(exported);
  });
});
