import { describe, expect, it } from "vitest";
import { getDesktopShortcut } from "./DesktopShortcuts";

const key = (key: string, shiftKey = false, altKey = false) =>
  ({ key, ctrlKey: true, metaKey: false, shiftKey, altKey });

describe("desktop shortcut modifiers", () => {
  it.each([
    ["n", false, false, "newWindow"], ["t", false, false, "newTab"],
    ["t", true, false, "reopenClosed"], ["w", false, false, "closeTab"],
    ["w", true, false, "closeWindow"], ["s", false, true, "saveAll"],
    ["s", true, false, "saveAs"], ["s", false, false, "save"],
    ["Tab", false, false, "nextTab"], ["Tab", true, false, "previousTab"],
    ["1", false, false, "tab1"], ["9", false, false, "tab9"],
    ["n", true, false, null], ["t", true, true, null],
    ["w", false, true, null], ["s", true, true, null], ["9", true, false, null],
  ])("routes %s with shift=%s alt=%s to %s", (value, shift, alt, expected) => {
    expect(getDesktopShortcut(key(value as string, shift as boolean, alt as boolean))).toBe(expected);
  });
  it("leaves unmodified drawing keys and AltGr to the editor", () => {
    expect(getDesktopShortcut({ ...key("t"), ctrlKey: false })).toBeNull();
    expect(getDesktopShortcut(key("t", false, true))).toBeNull();
  });
});
