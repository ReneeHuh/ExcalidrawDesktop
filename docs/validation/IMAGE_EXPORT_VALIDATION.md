# Whole-map PNG export validation

Date: September 3, 2026

Status: Automated web, core, build, and packaged integration validation passed.
Manual visual and failure-environment checks remain.

## Packaged result

Command:

```powershell
.\SmokeTests\Invoke-ImageExportSmokeTest.ps1 -SkipBuild
```

Observed result:

```text
Result                    : Passed
PngBytes                  : 1076992
Width                     : 14400
Height                    : 3880
FullSceneBounds           : True
EmbeddedImageInput        : True
WebViewBinaryTransfer     : True
TransactionalNativeWrite : True
```

The test loads two shapes separated by 7,000 canvas units plus an embedded PNG,
then exercises the production Excalidraw renderer, isolated WebView2 HTTP
upload, PNG validation, and transactional native writer. It verifies the PNG
signature and IHDR dimensions, proving export bounds are not limited to the
visible viewport.

The test also exposed and drove correction of the original endpoint design:
WebView2 does not raise `WebResourceRequested` for a mapped virtual host. The
implemented endpoint therefore uses a distinct unmapped per-session HTTPS
origin with validated CORS preflight, source origin, URL, and export ID.
This limitation is documented in Microsoft's
[WebView2 network-request guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/webresourcerequested).

## Remaining manual matrix

| Area | Required coverage | Result |
| --- | --- | --- |
| Visual fidelity | Text, arrows, bindings, frames, backgrounds, cropping, and embedded images | Not run |
| Editor state | Zoomed, scrolled, and selected scenes | Full bounds automated; visual comparison not run |
| Theme | Export under light and dark application themes | Not run |
| Filenames | Unicode and long suggested names | Filename policy unit coverage only |
| Failure paths | Picker cancel, denied destination, full disk, and configured limits | Limits automated; environment failures not run |
| Lifecycle | Close, move, hibernate, or tear out during an export | Operations are guarded; timed packaged race matrix not run |
| Performance | Large-scene time, peak memory, output size, and responsiveness | Not run |
