# Multi-Language Support Plan

Status: Proposed; implementation has not started
Created: September 2, 2026
Tracked by: [`DESKTOP_BACKLOG.md`](DESKTOP_BACKLOG.md)

## Goal

Add consistent, offline multi-language support across both parts of Excalidraw
Desktop:

- the native WinUI shell, settings, dialogs, titles, status messages, and
  accessibility text;
- the embedded Excalidraw React editor and the desktop-specific controls
  rendered around it.

The selected language must persist across launches, apply consistently to every
window and editor tab, and fall back safely when a locale is unavailable.

## Current State

The application has two separate localization surfaces:

1. `ExcalidrawDesktop.App` contains native WinUI text in XAML and C#.
2. `ExcalidrawDesktop.Web` embeds `@excalidraw/excalidraw` and adds custom menu,
   status, error, and accessibility strings.

Most desktop strings are currently hard-coded. The saved desktop preferences
contain theme, startup, and tab-lifecycle settings, but no language preference.
Each window also loads preferences independently, so preference changes are not
currently broadcast application-wide.

Excalidraw already accepts a `langCode` property and publishes its supported
language list. The desktop host should select the locale and pass it into every
editor rather than maintaining translations for Excalidraw's built-in UI.

## Product Decisions

### Language selection

- Default to **Use system language**.
- Store explicit selections as BCP-47 language tags.
- Limit the picker to locales supported by both the desktop translation catalog
  and the pinned Excalidraw package.
- Show language names in their native form, such as `Deutsch`, `Français`, and
  `日本語`.

Recommended initial languages:

- English
- Spanish
- French
- German
- Brazilian Portuguese
- Japanese
- Simplified Chinese
- Arabic

Before implementation, verify the exact Excalidraw locale codes exposed by the
pinned `@excalidraw/excalidraw` version. Do not assume that Windows and
Excalidraw use identical tags.

### Applying a changed language

Language changes will take effect after restarting Excalidraw Desktop.

WinUI language overrides must be established before XAML resources are loaded,
and already-created controls may not refresh reliably. A restart-to-apply policy
avoids mixed-language windows and WebViews. Settings should save the choice
immediately and display a localized restart-required message in every open
window.

### Fallback behavior

- Resolve **Use system language** from the Windows preferred-language list.
- Try an exact supported locale, then an explicitly defined parent or regional
  fallback.
- Fall back to `en-US` for the native shell and `en` for Excalidraw.
- Never allow an invalid saved value to prevent application startup.

## Architecture

### 1. Language model and resolver

Add a language preference to `DesktopPreferences` and persist it through
`DesktopSettingsStore`.

Introduce a central supported-language table. Each entry should contain:

- the saved BCP-47 preference tag;
- the WinUI resource tag;
- the Excalidraw `langCode`;
- the language's native display name;
- left-to-right or right-to-left direction;
- any explicit fallback mapping.

Keep resolution and validation in a testable class independent of UI controls.
The resolved locale, rather than the raw preference, is what the native and web
layers should consume.

### 2. Application-level preferences

Move ownership of shared preferences to an application-level service or extend
`ApplicationWorkspaceCoordinator` to distribute preference changes.

All windows should receive the same preferences so that:

- language restart notifications remain consistent;
- newly created windows do not diverge from existing windows;
- the existing immediate theme behavior remains intact.

### 3. Early WinUI language initialization

At application startup, before `InitializeComponent()` causes resources to be
loaded:

1. Load and validate the saved language preference.
2. Resolve it to a supported native locale.
3. Set `ApplicationLanguages.PrimaryLanguageOverride` for an explicit choice.
4. Clear or omit the override for **Use system language**.
5. Continue startup with English fallback resources available.

The override must only receive a language declared by the packaged application.

### 4. Native resource catalogs

Create the standard resource layout:

```text
ExcalidrawDesktop.App/
└── Strings/
    ├── en-US/Resources.resw
    ├── es-ES/Resources.resw
    ├── fr-FR/Resources.resw
    └── ...
```

Treat `en-US/Resources.resw` as the canonical source catalog.

- Add `x:Uid` values to localizable elements in `MainWindow.xaml`,
  `SettingsPage.xaml`, and `DocumentTabContent.xaml`.
- Move dynamically generated window titles, dialogs, tab state, status text,
  drag captions, recent-file placeholders, and accessibility names into
  resource lookups.
- Use parameterized resources for values such as `Untitled {0}`, recovery
  times, and unsaved-drawing counts.
- Do not assemble translated sentences from independently translated fragments.
- Preserve filenames, paths, application names, and keyboard shortcuts where
  appropriate.
- Keep internal logs, exception diagnostics, protocol method names, error codes,
  and smoke-test sentinels in English unless they are presented to users.

### 5. Package manifest localization

Replace user-facing package manifest values with `ms-resource:` references,
including:

- application display name and description;
- publisher display name when appropriate;
- Excalidraw file-type display name.

Verify that all supported languages appear in the generated package resource
index and that installation metadata falls back to English.

### 6. Excalidraw bridge integration

Extend the typed bridge with a host-to-web event:

```text
app.languageChanged
{
  langCode: string,
  direction: "ltr" | "rtl"
}
```

Implement the event in both `BridgeProtocol.ts` and `DesktopBridge.ts`.

For each editor session:

- send the resolved language after the WebView reports ready;
- follow the same pending-event approach used for theme changes so an early
  event is not lost;
- resend the language whenever an editor is recreated after unloading or a
  startup retry;
- validate the payload before updating React state.

The bridge protocol version does not need to change for an additive event.

### 7. React and Excalidraw localization

In `ExcalidrawDesktop.Web/src/main.tsx`:

- hold the resolved Excalidraw locale in React state;
- pass it to `<Excalidraw langCode={...}>`;
- set `ownerDocument.documentElement.lang`;
- set `ownerDocument.documentElement.dir` from the language metadata;
- follow the repository rule to use `ownerDocument` and `ownerWindow`, not DOM
  globals.

Let Excalidraw translate its own built-in tools and default menu items. Add a
small desktop-web catalog for custom content owned by this project, including:

- New Tab, Open, Save, and Save As menu labels;
- desktop wrapper ARIA labels;
- desktop-only fallback errors;
- initial document status before the native host sends its status.

Avoid depending on undocumented internal Excalidraw translation keys for
desktop-owned UI.

### 8. Settings experience

Add a Language settings card near Appearance:

- a picker beginning with **Use system language**;
- supported languages listed using their native names;
- a localized explanation that the setting affects both the app frame and
  editor;
- an accessible localized restart-required notice after a change;
- no automatic process restart or forced closure of unsaved drawings.

The currently effective language and pending next-launch language should remain
distinguishable until restart.

### 9. Formatting and bidirectional layout

- Format times, dates, and counts using the resolved culture.
- Keep file names and serialized document data unchanged.
- Allow WinUI to derive `FlowDirection` from the selected resource language.
- Explicitly set the web document direction for the wrapper UI.
- Confirm that Excalidraw receives an RTL locale and handles its internal
  controls appropriately.
- Check punctuation, icons, chevrons, dialog button order, title-bar layout, and
  mixed-direction filenames under Arabic.

## Translation Workflow

1. Add or change English source strings first.
2. Use stable semantic identifiers rather than English phrases as keys.
3. Run an automated key-parity check for every native and web catalog.
4. Reject duplicate keys, missing values, and unsupported locale mappings in CI.
5. Preserve placeholders and document their meaning for translators.
6. Review translations in context; do not treat machine translation as final.
7. Use pseudo-localization during development to expose clipping and hard-coded
   text before adding more production languages.

## Testing Plan

### Native unit tests

- Default preference is **Use system language**.
- Explicit language values round-trip through settings persistence.
- Invalid or obsolete values fall back safely.
- Exact, parent, regional, and English fallback resolution works.
- Every supported entry has a valid native tag and Excalidraw mapping.
- Parameterized resource formatting preserves placeholders.

Where application-data APIs make direct settings tests difficult, extract the
serialization and resolution logic into platform-independent units.

### Web tests

- `app.languageChanged` accepts valid payloads and rejects invalid ones.
- A language event received before listener registration is delivered later.
- React passes the new `langCode` to Excalidraw.
- The document `lang` and `dir` attributes update correctly.
- Desktop-owned labels fall back to English when a catalog is incomplete.
- Existing document and bridge tests continue to pass.

### Resource validation

- Native catalogs have the same required keys as `en-US`.
- Web catalogs have the same keys as English.
- All supported-language mappings refer to actual catalogs.
- No visible hard-coded English remains in production XAML, React markup, or
  user-facing C# paths, except explicitly allow-listed values.

### Packaged smoke tests

Add a debug-only localization smoke mode that starts the packaged application
with a controlled locale without changing the user's normal preference.

At minimum, verify:

- one non-English left-to-right language;
- one right-to-left language;
- native menu, Settings page, tab accessibility, and title resources load;
- the WebView reports the expected Excalidraw language and document direction;
- creating, opening, saving, restoring, suspending, and recreating a tab does
  not revert its editor to English;
- all tests run without network access.

### Manual validation matrix

Exercise at least:

- English as the fallback baseline;
- German or another long-text locale for clipping;
- Japanese or Simplified Chinese for CJK layout and input;
- Arabic for RTL and mixed-direction file paths;
- a system language unsupported by the app;
- Windows text scaling, high contrast, and representative IME input.

## Delivery Phases

### Phase 1: Infrastructure and preferences

- Add the language model, supported-language table, resolver, and persistence.
- Establish application-level preference propagation.
- Set the WinUI override early in startup.
- Add the Language settings card and restart-required state.
- Add resolver and persistence tests.

### Phase 2: Native WinUI localization

- Create English resources and migrate all native production strings.
- Localize manifest metadata.
- Add the first translated catalogs and key-parity validation.
- Validate native LTR and RTL layouts.

### Phase 3: Web and Excalidraw integration

- Add the language bridge event and pending-event handling.
- Pass `langCode` into Excalidraw.
- Add desktop-web translation catalogs and document language metadata.
- Add bridge and React tests.

### Phase 4: Packaged verification and rollout

- Add packaged localization smoke coverage.
- Run the full existing test and smoke suite.
- Complete manual CJK, RTL, accessibility, and offline validation.
- Document how to add and maintain future languages.

## Acceptance Criteria

- All user-facing desktop strings are resource-backed or explicitly
  allow-listed.
- **Use system language** is the default and resolves predictably.
- An explicit language selection persists and applies after restart.
- Every open window shows consistent preference and restart state.
- Every new, restored, resumed, retried, or recreated editor receives the same
  resolved Excalidraw locale.
- Unsupported and invalid locales fall back to English without blocking startup.
- The application remains fully functional offline.
- English, a long-text locale, a CJK locale, and an RTL locale pass visual and
  interaction validation.
- Web tests, TypeScript checks, .NET tests, the desktop build, and existing
  packaged smoke tests continue to pass.

## Key Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Windows and Excalidraw locale codes differ | Maintain one explicit, tested mapping table. |
| Language changes create mixed-language live UI | Apply after restart and show a consistent application-wide notice. |
| Hard-coded strings remain in C# dialogs and accessibility properties | Add a source scan with a narrow allow list and perform manual review. |
| Core or bridge errors expose English messages | Map stable error codes to presentation-layer resources while retaining diagnostic detail in logs. |
| Translations lag behind new English keys | Enforce catalog key parity in CI and fall back per key to English. |
| RTL affects the custom title bar or wrapper unexpectedly | Include Arabic in packaged and manual validation from the first release. |
| An Excalidraw upgrade changes locale support | Validate the mapping table against the package's exported language list during upgrades. |

## Recommended Change Sequence

Implement the work as three reviewable pull requests:

1. Localization infrastructure, language preferences, and early startup
   resolution.
2. Native WinUI and package-manifest string migration.
3. WebView/Excalidraw integration, translated catalogs, and packaged tests.
