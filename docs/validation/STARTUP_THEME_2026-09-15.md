# Startup editor theme correction

Opening a drawing at startup could leave the editor light while the native
window was dark. Excalidraw's asynchronous file parser captured the initial
editor theme, then the completed load restored that stale value after the
host's theme arrived. New empty tabs did not use this load path.

Document loading now omits the parsed theme when updating the scene, allowing
Excalidraw's controlled theme property to remain authoritative. The drawing's
background color and clean revision are preserved.

## Validation

- Both new regression cases failed before the fix and passed afterward. They
  use Excalidraw's real file parser and change the theme during the asynchronous
  load, checking both light-to-dark and dark-to-light transitions.
- All 93 web tests and TypeScript checks passed.
- The Debug title-bar smoke checks both native and rendered editor themes. Its
  startup regression holds a real WebView file read, delivers the dark theme,
  releases the load, and verifies the rendered editor remains dark.
- Debug build and self-contained publish passed with zero warnings/errors.

The correction is included in desktop version 0.1.6.
