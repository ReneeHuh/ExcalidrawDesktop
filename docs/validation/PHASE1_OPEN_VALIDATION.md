# Phase 1 Open validation

This checklist covers the first native document-lifecycle vertical slice.

## Automated

- [x] Version 1 bridge requests parse and preserve their UUID correlation identifiers.
- [x] Unsupported versions, malformed messages, and invalid identifiers are rejected.
- [x] Native bridge errors retain a stable code and user-safe message.
- [x] Minimal valid Excalidraw documents pass structural validation.
- [x] Malformed JSON, library files, and invalid document shapes are rejected.
- [x] A cancelled native Open response does not update elements, files, history, or title.
- [x] A scene-restore failure does not update elements, files, history, or title.
- [x] A successful restore replaces the scene, restores embedded files, clears history, and reports the filename.
- [x] The browser bridge rejects responses with mismatched request methods.
- [x] The packaged startup and ping/ready smoke test continues to pass.

## Manual packaged-app checks

- [ ] The desktop main menu contains native `Open…`, `Save`, and `Save As…` items and no browser-backed Open or Save item.
- [ ] Ctrl+O opens the native `.excalidraw` picker exactly once.
- [ ] Cancelling the picker leaves the current drawing and window title unchanged.
- [ ] Cancelling the discard confirmation leaves the current drawing and title unchanged.
- [ ] Opening a valid drawing restores every element and embedded image.
- [ ] The title becomes `<filename> — Excalidraw Desktop` only after restoration succeeds.
- [ ] Opening malformed JSON reports an error and leaves the current drawing unchanged.
- [ ] Selecting a file larger than 50 MB reports the size-limit error and leaves the drawing unchanged.

Record the fixture filename and Windows/WebView2 versions for each manual run. Do not mark the Open slice complete until the valid drawing includes at least one embedded image and the cancellation checks have been exercised against a dirty scene.
