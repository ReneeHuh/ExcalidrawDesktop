# Multi-language validation

Date: September 3, 2026  
Configuration: Debug x64, packaged WinUI 3 application

## Implemented coverage

The application supports `en-US`, `es-ES`, `fr-FR`, `de-DE`, `pt-BR`, `ja-JP`,
`zh-CN`, and `ar-SA`. The language preference defaults to the Windows language,
persists application-wide, and applies after restart. The resolved language is
sent to every Excalidraw WebView together with `ltr` or `rtl` direction.

The normal desktop build runs `tools/Test-Localization.ps1`. The check validates:

- exact key parity and non-empty values across all eight native catalogs;
- placeholder parity, including reordered placeholders;
- every `DesktopResources` code reference against the English catalog;
- production C# UI assignments that bypass resources;
- visible XAML defaults and their `x:Uid` resources;
- package-manifest `ms-resource` references.

Current result: 8 locales, 171 native keys, and 121 native code references pass.
The TypeScript catalog is structurally typed and its fallback/error mappings are
covered by web unit tests.

## Automated results

| Check | Result |
| --- | --- |
| `tools/Test-Localization.ps1` | Passed |
| Web TypeScript check | Passed |
| Web tests | 29 passed |
| .NET tests | 59 passed |
| Debug x64 desktop build | Passed, 0 warnings and 0 errors |
| Normal packaged startup using the system language | Passed |
| German packaged localization smoke | Passed |
| Arabic packaged localization smoke | Passed |
| Complete packaged smoke suite | Passed under the current Spanish Windows app language |

`SmokeTests/Invoke-LocalizationSmokeTest.ps1` injects a debug-only startup locale
without saving over the user's normal preference. For both German and Arabic it
verified localized XAML and dynamic resources, the native flow direction, the
web document/Excalidraw language and direction, a newly created editor, and an
editor recreated through Retry. The Arabic run reported `RightToLeft` and `rtl`;
the German run reported `LeftToRight` and `ltr`.

The packaged checks exposed three issues that were fixed during validation:

- applying an `x:Uid` resource directly to `Window.Title` caused a localized
  startup `XamlParseException`, so the initial title now uses a runtime resource;
- the default dynamic resource context did not reliably follow the forced smoke
  locale, so runtime lookups now use an explicit MRT language context; and
- some Windows App SDK runtime combinations reject an empty primary-language
  override for **Use system language**. Startup now falls back to resolving the
  current Windows preferred-language list on every launch, preserving system
  language behavior without blocking application startup.

## Remaining release validation

- Human linguistic review of all seven translated catalogs.
- Visual clipping and interaction review in English, German, Japanese or
  Simplified Chinese, and Arabic.
- Mixed-direction filenames and title-bar interactions under Arabic.
- Representative Japanese/Chinese IME input.
- High contrast, text scaling, and accessibility review in localized UI.
- A packaged run with network access disabled.

These remaining checks do not block compilation or the implemented language
selection path, but they remain release criteria.
