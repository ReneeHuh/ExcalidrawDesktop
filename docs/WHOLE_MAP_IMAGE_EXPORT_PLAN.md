# Whole Map Image Export Plan

Status: Implementation and packaged end-to-end automation complete; manual
visual and failure-environment validation remains
Created: September 2, 2026
Tracked by: [`DESKTOP_BACKLOG.md`](DESKTOP_BACKLOG.md)

## Goal

Add a reliable desktop workflow that saves the whole current drawing as a PNG
image. "Whole map" means every visible element in the drawing, including
content outside the current viewport. It does not mean a screenshot of the
current zoomed or scrolled view.

The image export remains separate from normal `.excalidraw` document saving.
Exporting an image must not change the active document path, clear its dirty
state, or interfere with recovery.

## Recommended first release

- Export PNG only.
- Include all non-deleted elements, regardless of selection or viewport.
- Crop the image to the complete scene bounds with a small padding.
- Include the canvas background.
- Render at 2x scale for a useful default resolution.
- Use a native Windows save picker.
- Suggest `<drawing-name>.png`, or `Untitled.png` for a new drawing.

SVG, PDF, transparent-background export, custom scale, clipboard export,
per-frame export, and batch export remain follow-up work for the broader export
center described in `FUTURE_DESKTOP_ROADMAP.md`.

## Current state

The native File menu now exposes **Export whole drawing as PNG…**. The web
controller renders every scene element and embedded file through Excalidraw's
public `exportToBlob()` API with a background, ten-pixel scene padding, and 2×
dimensions. The previous web-owned `Export` and `SaveAsImage` menu items were
removed so there is one clear image-save path.

The typed JSON bridge carries only bounded export metadata. PNG bytes travel
as an HTTP POST to an isolated, unmapped per-session HTTPS origin intercepted
by the owning WebView2 host, where origin, CORS preflight, export identifier,
signature, 100 MB size, and 16,384-pixel dimension constraints are enforced
before a transacted Windows-stream write. The upload cannot use the editor's
mapped virtual host because WebView2 does not raise `WebResourceRequested` for
virtual-host folder mappings.

Exporting blocks moving, unloading, suspending, or closing the owning tab until
it completes. Picker cancellation is non-destructive, late or mismatched
messages are ignored, WebView failure clears the operation, and a two-minute
timeout prevents a session remaining permanently busy. Core and web unit tests,
type checking, the production web build, desktop build, startup smoke test, and
title-bar/lifecycle smoke test pass. A packaged export of a representative
off-screen scene also produced a validated 14,400×3,880 PNG through the real
binary transfer and native writer. The manual visual and failure-environment
cases below remain to be recorded.

## Implementation plan

### 1. Add the native command

- Add **File > Export whole drawing as PNG...** to `MainWindow.xaml`.
- Enable the command only when the active editor is ready and is not resuming
  or restoring from hibernation.
- Route the command to the active `DocumentSession`, just like Save and Save As.
- Do not assign a document-saving keyboard shortcut in the first release.
- Keep the command disabled while an export for that session is already in
  progress.

### 2. Extend the bridge contract

- Add an `image.exportRequested` host-to-web event containing an export ID and
  the fixed first-release options.
- Correlate the host event, isolated per-session upload URL, request origin,
  request header, and HTTP response with one export ID. Use a typed bridge
  failure event when rendering fails before upload; the intercepted HTTP
  response reports write completion or rejection.
- Validate export IDs, formats, dimensions, and byte counts on the native side.
- Keep PNG bytes out of ordinary JSON bridge payloads.
- Ensure late responses from a closed, navigated, or hibernated tab are ignored
  safely.

### 3. Render the complete scene in the web editor

- Add a small export controller rather than placing the complete workflow in
  `main.tsx`.
- Read all non-deleted elements from `excalidrawAPI.getSceneElements()`.
- Read embedded image data from `excalidrawAPI.getFiles()`.
- Read the current canvas color and other required render state from
  `excalidrawAPI.getAppState()`.
- Call Excalidraw's public `exportToBlob()` with PNG MIME type, background
  enabled, 2x dimensions, and agreed scene padding.
- Do not filter to selected elements and do not use viewport bounds, scroll,
  or zoom.
- Reject an empty drawing with a friendly, non-destructive message.
- Surface missing or unreadable embedded-image failures instead of silently
  producing an incomplete image.

### 4. Transfer and save the PNG natively

- Introduce a native image export service responsible for the save picker,
  destination validation, binary transfer, writing, cancellation, and errors.
- Use a WebView2 binary-safe mechanism or a measured chunked transfer rather
  than encoding the whole PNG into one JSON/base64 message.
- Show the Windows file picker with a `.png` file type and a filename based on
  the active document.
- Write the output transactionally where the selected storage API supports it.
- Do not update `DocumentService`, the active `.excalidraw` path, recent files,
  saved scene version, recovery snapshot, or dirty status.
- Apply an explicit maximum output size and maximum canvas dimensions so an
  unusually large infinite-canvas scene fails predictably rather than
  exhausting WebView2 or native memory.

### 5. Add progress and error handling

- Mark the active session as exporting before requesting the render.
- Show an **Exporting...** status or toast while rendering and writing.
- Prevent duplicate export requests for the same tab.
- Treat picker cancellation as a normal result without an error notification.
- On success, show the exported filename.
- Report render failure, excessive dimensions, missing image data, access
  denial, insufficient space, and write failure with concise user-facing
  messages.
- Clear export state in every completion, cancellation, failure, tab-close, and
  WebView teardown path.

### 6. Consolidate export entry points

- Remove or replace `MainMenu.DefaultItems.SaveAsImage` after the native export
  workflow is available so users do not see two competing ways to save a PNG.
- Decide whether to retain Excalidraw's richer `Export` dialog. If retained,
  label it clearly as an advanced export flow and ensure its file output also
  follows the desktop policy.
- The first release should present one obvious command for saving the whole
  drawing as a PNG.

## Expected code areas

- `ExcalidrawDesktop.Web/src/main.tsx`
- `ExcalidrawDesktop.Web/src/bridge/BridgeProtocol.ts`
- `ExcalidrawDesktop.Web/src/bridge/DesktopBridge.ts`
- A new web image export controller and its tests
- `ExcalidrawDesktop.App/MainWindow.xaml`
- `ExcalidrawDesktop.App/MainWindow.xaml.cs`
- `ExcalidrawDesktop.App/Models/DocumentSession.cs`
- `ExcalidrawDesktop.App/Services/BridgeDispatcher.cs`
- A new native image export service
- Bridge and export tests in the web and core test projects

## Test plan

### Automated tests

- Validate all new bridge requests, events, responses, and malformed payloads.
- Prove the export controller passes all scene elements and embedded files to
  `exportToBlob()`.
- Prove selection, current viewport, scroll, and zoom do not restrict output.
- Prove an empty scene does not start a native file write.
- Prove export success, cancellation, and failure do not change document dirty
  state or the active `.excalidraw` path.
- Prove simultaneous exports from different tabs remain correlated to their
  owning sessions.
- Prove a second export request in the same tab is rejected or ignored while
  the first is active.

### Visual and packaged tests

- Export a scene with elements far outside the visible viewport and verify all
  elements appear in the PNG.
- Verify text, arrows, bindings, frames, canvas background, and embedded and
  cropped images.
- Verify output while the editor is zoomed, scrolled, and has a selection.
- Verify light and dark application themes do not unexpectedly recolor the
  exported drawing.
- Verify Unicode and long document filenames produce a valid suggested PNG
  name.
- Verify picker cancellation, an unwritable destination, insufficient disk
  space, and an output exceeding configured limits.
- Verify closing, moving, hibernating, or tearing out a tab during export does
  not save another tab's image or leave the session permanently busy.
- Run a large-scene performance test and record render time, peak memory, output
  size, and UI responsiveness.

## Acceptance criteria

- The File menu has one clear command for exporting the whole drawing as PNG.
- The saved PNG contains every visible scene element, including off-screen
  content, and is not limited to the current selection or viewport.
- Embedded images and the canvas background render correctly.
- The native picker suggests a sensible `.png` filename.
- Cancelling or failing an export does not modify the drawing or its save state.
- Large exports have explicit bounds and understandable errors.
- Export work is isolated to the owning tab and cannot be confused across
  multiple windows or torn-out tabs.
- Existing New, Open, Save, Save As, recovery, dirty-close, and tab lifecycle
  behavior continues to pass its tests.
