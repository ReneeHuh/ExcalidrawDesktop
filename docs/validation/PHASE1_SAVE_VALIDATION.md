# Phase 1 Save and Save As validation

This checklist covers active-file ownership and native `.excalidraw` writes.

## Automated

- [x] Save serializes the current elements, export-cleaned app state, and embedded files through Excalidraw's public serializer.
- [x] Save and Save As use distinct typed bridge methods and validate correlated responses.
- [x] Cancelling Save As returns without reporting a saved scene version.
- [x] Successful writes report the filename and exact scene version that was serialized.
- [x] Native validation rejects malformed documents and payloads above the 50 MB UTF-8 limit.
- [x] The native host builds with the transacted-write implementation.
- [x] The packaged startup and ping/ready smoke test continues to pass.

## Manual packaged-app checks

- [ ] The desktop menu exposes `Save` and `Save As…`; the editor's browser-backed file actions remain unreachable.
- [ ] Ctrl+S on an untitled drawing opens the native Save As picker exactly once.
- [ ] Cancelling that picker leaves the title and dirty state unchanged.
- [ ] A successful first save creates a valid `.excalidraw` file and updates the window title.
- [ ] Editing and pressing Ctrl+S updates the active file without showing another picker.
- [ ] Ctrl+Shift+S selects a different file, updates the title, and makes that file the target of the next Ctrl+S.
- [ ] A drawing containing an embedded image survives Save, close, and Open without data loss.
- [ ] A file written by the desktop app opens correctly on excalidraw.com.
- [ ] A denied or failed write reports an error, leaves the drawing dirty, and preserves the previous file contents.
- [ ] Changing the canvas while a save picker is open leaves the document dirty after the earlier snapshot is written.

Record the fixture paths and Windows/WebView2 versions for each manual run. Do not mark the Save slice complete until both the embedded-image round trip and failure-preserves-original checks have been exercised.
