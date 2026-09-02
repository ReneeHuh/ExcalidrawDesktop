# Phase 0 validation

This checklist records the compatibility-spike evidence for Excalidraw Desktop. A checked item has been exercised against the packaged WinUI application, not only the browser development server.

## Automated build and startup

- [x] Existing Excalidraw packages build from the pinned repository baseline.
- [x] Desktop TypeScript type-check passes.
- [x] Production Vite bundle includes local fonts and lazy-loaded chunks.
- [x] WinUI x64 Debug build completes with zero warnings and zero errors.
- [x] Loose MSIX development package registers successfully for the current user.
- [x] Packaged application starts and remains responsive.
- [x] WebView2 loads the private `https://app.excalidraw.local` origin.
- [x] The web editor sends `app.ready`; the host removes its startup state and updates the title.
- [x] The host responds to the versioned `app.ping` request.
- [x] `Invoke-SmokeTest.ps1` reproduces packaged startup and bidirectional bridge readiness with state polling instead of a fixed-duration startup delay.

Last automated run: September 1, 2026.

At the time of this validation, the repository was based directly on the upstream Excalidraw source tree, whose repository-wide typecheck contained two unrelated baseline errors. The desktop integration's dedicated typecheck passed. The desktop project now consumes the published `@excalidraw/excalidraw` package and no longer runs upstream source tests as part of its build.

## Manual compatibility matrix

- [ ] Mouse drawing, selection, resize, rotation, pan, and zoom.
- [ ] Touch pan, pinch zoom, selection, and drawing.
- [ ] Pen drawing, erasing, pressure, barrel button, and palm rejection.
- [ ] Keyboard shortcuts, focus traversal, and text-editor focus recovery.
- [ ] Text editing with a representative IME and an RTL input language.
- [ ] Copy and paste of elements, plain text, images, PNG, and SVG.
- [ ] Image insertion followed by PNG and SVG export.
- [ ] Open or import the exported representative drawing again.
- [ ] Switch through representative locales and verify lazy chunks load.
- [ ] Light and dark mode rendering.
- [ ] Rendering at 100%, 150%, and 200% display scaling.
- [ ] Move the window between displays with different scaling.
- [ ] Cold start with network adapters disabled after installation.
- [ ] Verify no editor, font, locale, or chunk request reaches a remote origin.

Record the Windows build, WebView2 Runtime version, display configuration, and input hardware beside each manual run. Phase 0 is not complete until every applicable manual item passes or has an explicitly accepted limitation.
