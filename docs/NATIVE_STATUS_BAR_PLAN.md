# Native Status Bar Plan

Status: Implemented September 2, 2026; build and unit validation complete.
Packaged smoke plus manual visual, DPI, theme, and assistive-technology
validation remains.

## Goal

Move document status presentation out of the embedded Excalidraw HTML and into
the WinUI window shell. The status bar remains associated with the active
drawing, while editor content uses the WebView's full height.

## Design

- Place a 32-pixel status surface below the editor in `MainWindow.xaml`.
- Show the active document path or display name on the left.
- Show save, recovery, sleep, resume, export, and external-file state on the
  right.
- Render external-file conflicts as a native button that opens the existing
  WinUI resolution dialog directly.
- Hide the drawing status bar while the Settings tab is active.
- Use WinUI theme resources, text trimming, tooltips, automation identifiers,
  accessible names, and a polite live region.
- Ignore asynchronous updates from inactive sessions so background work cannot
  replace the selected tab's status.

## Bridge cleanup

- Remove the `document.statusChanged` host-to-web event.
- Remove the `document.resolveExternalConflict` web-to-host event.
- Keep native save-conflict detection in `BridgeDispatcher`; that callback is
  independent of the removed status-bar click route.
- Remove React status state, footer markup, styles, and obsolete bridge tests.

## Validation

- [x] Web type checking and unit tests pass.
- [x] WinUI and core projects build with no errors.
- [x] The status bar is owned by the window rather than each WebView.
- [x] Active-tab selection controls the displayed document and state.
- [x] Settings hides the drawing status surface.
- [x] External-file resolution is invoked directly from native UI.
- [x] Obsolete status bridge messages and HTML/CSS are removed.
- [ ] Run the packaged title-bar/status-bar smoke test after resolving the
  locally installed development-package registration conflict (`0x80073CFB`).
- [ ] Confirm layout at 100%, 150%, and 200% scaling on hardware.
- [ ] Confirm light, dark, and high-contrast rendering on hardware.
- [ ] Confirm Narrator announces status changes and the Resolve action.
