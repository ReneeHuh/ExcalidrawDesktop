export type DesktopCommand = "newTab" | "newWindow" | "reopenClosed" | "closeTab" |
  "closeWindow" | "open" | "save" | "saveAs" | "saveAll" | "nextTab" | "previousTab" |
  `tab${1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9}`;

export function getDesktopShortcut(event: Pick<KeyboardEvent,
  "key" | "ctrlKey" | "metaKey" | "altKey" | "shiftKey">): DesktopCommand | null {
  if (!event.ctrlKey || event.metaKey) return null;
  const key = event.key.toLowerCase();
  if (event.altKey) return key === "s" && !event.shiftKey ? "saveAll" : null;
  if (key === "tab") return event.shiftKey ? "previousTab" : "nextTab";
  if (key === "t") return event.shiftKey ? "reopenClosed" : "newTab";
  if (key === "w") return event.shiftKey ? "closeWindow" : "closeTab";
  if (key === "s") return event.shiftKey ? "saveAs" : "save";
  if (event.shiftKey) return null;
  if (key === "n") return "newWindow";
  if (key === "o") return "open";
  if (/^[1-9]$/.test(key)) return `tab${key}` as DesktopCommand;
  return null;
}
