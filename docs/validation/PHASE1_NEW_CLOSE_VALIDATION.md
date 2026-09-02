# Phase 1 New and close validation

This checklist covers untitled-document reset and protection against closing with unsaved work.

## Automated

- [x] New forwards the current dirty state to the typed native bridge before the editor is reset.
- [x] A successful New response is distinct from cancellation.
- [x] Dirty-state changes are sent to the native host through validated event payloads.
- [x] Native close-save requests are delivered to registered web listeners and can be unsubscribed safely.
- [x] Native-to-web close-save events use a parseable versioned bridge envelope.
- [x] TypeScript, web tests, native tests, and the WinUI build pass with the close guard enabled.
- [x] The packaged startup and ping/ready smoke test continues to pass.

## Manual packaged-app checks

- [ ] The desktop menu exposes `New`, and Ctrl+N invokes it exactly once.
- [ ] New on a clean drawing resets the canvas without a confirmation dialog.
- [ ] Cancelling dirty New preserves the elements, embedded files, history, active file, and title.
- [ ] Confirming dirty New clears all editor state, resets the title, and makes the next Ctrl+S open Save As.
- [ ] Closing a clean drawing exits without prompting.
- [ ] Cancelling the dirty-close prompt keeps the window and drawing unchanged.
- [ ] Choosing Discard closes without starting a save.
- [ ] Choosing Save for an active file writes it and then closes.
- [ ] Choosing Save for an untitled drawing opens Save As; cancelling the picker keeps the window open and dirty.
- [ ] A denied or failed close-save reports an error and keeps the window open and dirty.
- [ ] Editing while the close-save picker is open keeps the window open because the saved snapshot is no longer current.

Record Windows and WebView2 versions for the manual run. Exercise New and close with a drawing containing an embedded image so the reset and preservation cases cover binary data.
