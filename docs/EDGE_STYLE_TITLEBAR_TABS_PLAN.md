# Edge-style WinUI title-bar tabs plan

Status: Implemented September 2, 2026; expanded packaged automation passes and
the hardware-dependent manual input, mixed-DPI, contrast, RTL, and
assistive-technology matrix remains open.
Created: September 2, 2026
Related plan: [`TABBED_DESKTOP_PLAN.md`](TABBED_DESKTOP_PLAN.md)

Implementation decision: `TitleBarHeightOption.Tall` is retained because its
caption height aligns with the native `TabView` strip in the packaged build;
the automated layout check passes with the 188-DIP drag footer and DPI-adjusted
caption insets.

## Outcome

Move the existing native WinUI `TabView` into the window title-bar area so the
tabs occupy the top edge of the window, similar to Microsoft Edge. Keep the
system-drawn minimize, maximize/restore, and close buttons, and preserve all
current document, WebView2, save, recovery, and tab-order behavior.

This is a shell-layout change, not a new tab implementation. The repository
already has the important functional pieces:

- `MainWindow.xaml` owns a native `TabView` with add, close, selection, and
  reorder handlers.
- `MainWindow.xaml.cs` maps each `TabViewItem` to a `DocumentSession` and keeps
  the active WebView2 visible.
- `DocumentSession` owns the tab header, dirty marker, tooltip, context menu,
  and editor host.
- Existing smoke tests cover per-tab origin isolation, workspace restoration,
  recovery, and startup.

The native project already targets Windows 11 (`10.0.22000.0`) and references
`Microsoft.WindowsAppSDK` 2.1.3, so this work does not require a framework
upgrade or a Windows 10 fallback path. One browser-style behavior listed in the
older tabbed plan is not present in the current code: there is no pointer
handler for middle-click close. It is included below as a small interaction gap,
not treated as existing behavior.

## Research and platform decision

Microsoft's WinUI guidance explicitly supports putting `TabView` in a window's
title bar. Its recommended pattern is to add a transparent `TabStripFooter`,
reserve part of that footer as a draggable region, set
`ExtendsContentIntoTitleBar` in code, and pass the footer to `SetTitleBar`.

The title-bar guidance adds four constraints that this implementation must
honor:

1. Interactive controls cannot be part of the drag region.
2. Space occupied by the system caption buttons must be reserved using
   `AppWindow.TitleBar.LeftInset` and `RightInset`.
3. AppWindow inset values are physical pixels, so XAML layout values must be
   divided by `XamlRoot.RasterizationScale`.
4. Layout-dependent title-bar calculations should run after the title bar is
   loaded and again when its size changes.

Use the XAML `Window.SetTitleBar(UIElement)` path rather than manually defining
passthrough rectangles with `InputNonClientPointerSource`. The only intended
drag target is a dedicated footer, so `SetTitleBar` is simpler and gives WinUI
responsibility for the non-client input plumbing. Do not make the entire
`TabView`, a tab item, the File menu, or the add-tab button draggable.

Use the existing `TabView` directly rather than nesting it in the newer WinUI
`TitleBar` control. The dedicated TabView-in-title-bar pattern is the documented
fit, and introducing another title-bar control would add layout and input
ownership without replacing any existing repository responsibility.

Primary references:

- [Tab View: display tabs in a window's title bar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/tab-view#display-tabview-tabs-in-a-windows-title-bar)
- [Title bar customization](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar?tabs=winui3)
- [Windows app title-bar design](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design)
- [Windows accessibility overview](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview)

## Intended layout

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ Recent │ Board A ● × │ Architecture × │ + │ drag space │ _  □  ×       │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│                         Active Excalidraw canvas                         │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

The right-side drag space remains present even when many tabs are open. Tabs
may shrink or enter the control's overflow behavior before the drag space and
caption buttons are sacrificed. In right-to-left layouts, the caption inset
and reserved layout must follow the values reported by `AppWindowTitleBar`, not
an assumed right-hand location.

## Scope

Included:

- Extend client content into the native title-bar area.
- Put the existing tab strip at the physical top edge of the window.
- Keep native caption buttons and their standard window behavior.
- Add a guaranteed draggable area in the tab strip footer.
- Prevent tabs, close buttons, Recent, and add-tab controls from being swallowed
  by the non-client drag surface.
- Add middle-click-to-close through the existing dirty-safe close path.
- Make the title strip responsive to caption insets, DPI, activation state,
  light/dark mode, contrast themes, maximized state, and RTL layout.
- Add focused automated and manual validation without weakening existing tab
  lifecycle tests.

Deferred:

- Edge's vertical-tabs mode, tab groups, pinned tabs, profiles, or address bar.
- Tab tear-out and multiple windows. `TabView.CanTearOutTabs` exists, but it
  changes ownership of `DocumentSession`, WebView2, close coordination, and
  workspace persistence and should be planned separately.
- Custom-drawn minimize, maximize, or close buttons.
- A deeply customized `TabViewItem` control template. Start with WinUI's native
  visuals and add only resource-level sizing if measurements require it.
- Synchronizing the native shell theme with Excalidraw's per-editor theme. The
  first implementation should remain correct under the Windows theme; shared
  theme ownership can be addressed separately.

## Proposed code changes

### 1. Reshape the title row in `ExcalidrawDesktop.App/MainWindow.xaml`

- Keep `MainLayout` and `EditorHost`; the editor row remains `Height="*"`.
- Replace the current bare row-zero `TabView` placement with a title-strip
  container that can paint under the whole non-client area while reserving
  left and right caption inset columns.
- Name the inset columns, for example `TitleBarLeftInset` and
  `TitleBarRightInset`. Their initial widths are zero and code updates them once
  XAML has a rasterization scale.
- Keep `DocumentTabs` in the center column and preserve its current event
  handlers, automation ID, accelerators, `CanReorderTabs`, add button, and
  Recent `TabStripHeader`.
- Add `TabView.TabStripFooter` containing a transparent grid named
  `WindowDragRegion`. Give it a minimum logical width of 188 px, matching the
  Microsoft TabView title-bar example, then validate that value at 100%, 150%,
  and 200% scaling and at the repository's minimum useful window width.
- Set `CloseButtonOverlayMode="Auto"` so inactive close buttons do not consume
  scarce tab-title width unless pointer/focus behavior needs a different value
  during validation.
- Use theme resources for strip/background/foreground changes. Do not hard-code
  colors that bypass light, dark, or contrast themes.

No changes should be needed in `DocumentTabContent.xaml`; its WebView2 remains
in the second row and must never overlap the title strip.

### 2. Add title-bar lifecycle code in `ExcalidrawDesktop.App/MainWindow.xaml.cs`

Add a small, isolated group of title-bar members and methods rather than mixing
them into document lifecycle code:

- `InitializeTitleBar()`
- `UpdateTitleBarInsets()`
- `OnTitleBarLoaded(...)`
- `OnTitleBarSizeChanged(...)`
- `OnWindowActivated(...)` extension for active/inactive presentation

Initialization order:

1. Call `InitializeComponent()`.
2. Set `ExtendsContentIntoTitleBar = true` immediately in the constructor to
   avoid a flash of the default title bar.
3. Call `SetTitleBar(WindowDragRegion)` so only the unused footer is treated as
   non-client drag space.
4. Set `AppWindow.TitleBar.PreferredHeightOption` to
   `TitleBarHeightOption.Tall` after extension is enabled, then verify that its
   caption-button height aligns with the native TabView strip. If the installed
   Windows App SDK renders the standard height correctly and Tall adds unwanted
   padding, record that result and use the standard option; alignment and input
   correctness are the acceptance criteria.
5. Make caption-button resting and inactive backgrounds transparent so the
   title-strip background paints continuously underneath them. Retain the
   system close-button hover/pressed behavior.
6. Register loaded/size-change handlers and calculate the inset columns after
   `XamlRoot` is available.

`UpdateTitleBarInsets()` should divide `LeftInset` and `RightInset` by
`MainLayout.XamlRoot.RasterizationScale` before assigning XAML column widths.
Do not assume the caption controls are always on the right. Recalculate when
the title strip changes size and verify monitor-to-monitor DPI transitions.

The current `OnWindowActivated` method already initializes the active session
and checks external file state. Extend this handler carefully so title-strip
active/inactive visuals update without changing those behaviors. Prefer WinUI
theme resources and opacity/state changes over fixed colors.

Keep `UpdateWindowTitle()` unchanged as the source of the accessible/taskbar
window title. A custom title bar hides the system-rendered text but the
`Window.Title` value still communicates the active document to Windows and
automation surfaces.

### 3. Preserve current tab ownership and behavior

Do not move any of these responsibilities while integrating the title bar:

- `CreateTab()` still creates `DocumentSession`, `DocumentTabContent`, and
  `TabViewItem` together.
- `OnTabSelectionChanged()` still switches the visible WebView2 and updates the
  window title.
- `OnTabCloseRequested()` still runs dirty-document protection.
- `OnTabDragCompleted()` still synchronizes persisted session order.
- The `DocumentSession.HeaderText` dirty marker, context flyout, tooltip, and
  automation name remain the tab presentation source.
- The unique per-tab origin and bridge dispatcher association remain untouched.

For Edge-style middle-click close, attach one native pointer handler when
`CreateTab()` constructs each `TabViewItem`. React only to a middle-button press,
mark the event handled, and call `RequestCloseSessionAsync(session)` so dirty
tabs still receive the normal Save/Don't Save/Cancel protection. Detach the
handler when the session closes if it is not expressed as a named handler with
the tab/session lookup already used elsewhere.

This boundary keeps the change reversible and prevents title-bar input work
from becoming a document-state refactor.

### 4. Add a focused packaged smoke check

Add `SmokeTests/Invoke-TitleBarTabsSmokeTest.ps1`, following the packaging and
process-ownership pattern in `Invoke-TabbedSmokeTest.ps1`.

The native debug-only test path should expose a small result file or title
sentinel after XAML loads. It should validate what can be asserted reliably in
process:

- `ExtendsContentIntoTitleBar` is enabled.
- `WindowDragRegion` is the element registered with `SetTitleBar` (or an
  equivalent internal initialization flag is set only after registration).
- Left and right XAML inset widths match the AppWindow inset values after DPI
  conversion within a rounding tolerance.
- The drag footer has a non-zero width.
- Two tabs can still be created, selected, reordered, and closed through the
  existing handlers.

Do not use this smoke test as a substitute for pointer validation; caption hit
testing, dragging, and system-menu gestures require a real packaged-window
manual pass or UI automation capable of non-client input.

## Delivery phases

### Phase E0: Layout and input spike

- Create the inset-aware title-strip container and footer drag region.
- Enable title-bar extension and register the footer with `SetTitleBar`.
- Compare standard and Tall title-bar height on the pinned Windows App SDK
  package; record the chosen option in this document.
- Verify that every existing title-strip control receives pointer input.

Exit criteria:

- Tabs touch the top window edge and caption buttons remain system-rendered.
- Dragging the footer moves/restores/maximizes the window normally.
- Clicking, reordering, closing, adding, and selecting tabs still work.
- Recent remains clickable and never initiates a window drag.
- The window can always be dragged when enough tabs exist to overflow.

### Phase E1: Responsive and visual polish

- Apply caption insets with DPI conversion.
- Tune tab width constraints only if the default `TabView` behavior fails the
  narrow-window/many-tab matrix.
- Add middle-click close through `RequestCloseSessionAsync`.
- Make the background continuous beneath transparent caption buttons.
- Add active/inactive presentation using theme-aware resources.
- Verify RTL placement rather than mirroring with hard-coded margins.

Exit criteria:

- Caption controls never cover a tab, the add button, Recent, or the drag
  footer.
- No one-pixel gap or overlap appears at 100%, 125%, 150%, 175%, or 200% DPI.
- Maximize, restore, Snap Layouts, right-click system menu, and double-click
  maximize/restore behave as standard Windows title-bar interactions.
- Light, dark, contrast, active, and inactive states remain legible.

### Phase E2: Regression and accessibility validation

- Add the packaged title-bar smoke test.
- Run all existing native, web, startup, tab-isolation, workspace-restore, and
  recovery tests.
- Complete the manual matrix below. Automated results and the remaining manual
  cases are recorded in
  [`validation/TITLE_BAR_ACCESSIBILITY_VALIDATION.md`](validation/TITLE_BAR_ACCESSIBILITY_VALIDATION.md).

Exit criteria:

- No document lifecycle or isolation regression is observed.
- Narrator announces tab names and dirty state, and caption buttons retain
  their standard automation behavior.
- Keyboard-only use can reach Recent, tabs, close buttons, add-tab, and the
  editor without focus becoming trapped in the non-client area.

## Validation matrix

Automated commands:

```powershell
corepack yarn --cwd .\ExcalidrawDesktop.Web typecheck
corepack yarn --cwd .\ExcalidrawDesktop.Web test
dotnet test .\ExcalidrawDesktop.sln -p:Platform=x64
.\SmokeTests\Invoke-SmokeTest.ps1 -SkipBuild
.\SmokeTests\Invoke-TabbedSmokeTest.ps1 -SkipBuild
.\SmokeTests\Invoke-WorkspaceRestoreSmokeTest.ps1 -SkipBuild
.\SmokeTests\Invoke-RecoverySmokeTest.ps1 -SkipBuild
.\SmokeTests\Invoke-TitleBarTabsSmokeTest.ps1 -SkipBuild
```

Manual title-bar cases:

- Drag from empty footer space; verify drag, Aero Snap, and restore-from-
  maximized behavior.
- Double-click the footer; verify maximize/restore.
- Right-click or press-and-hold the footer; verify the system window menu.
- Exercise minimize, maximize/restore, close, and Windows 11 Snap Layouts.
- Click Recent, add-tab, tab bodies, and tab close buttons near the drag region.
- Reorder the first, middle, and last tab with both few and overflowing tabs.
- Middle-click a tab and run every tab context-menu close command.
- Verify `Ctrl+T`, `Ctrl+W`, `Ctrl+Tab`, and `Ctrl+Shift+Tab` while focus is in
  both the native strip and WebView2.
- Test 1, 5, 10, and 20 tabs at narrow, normal, maximized, and snapped widths.
- Test 100%, 125%, 150%, 175%, and 200% display scaling, including moving the
  window between monitors with different scaling.
- Test light, dark, two contrast themes, active/inactive windows, and increased
  text scaling.
- Test a right-to-left Windows display language or a forced RTL development
  configuration.
- Use Narrator and Accessibility Insights/Inspect to verify tab names, selected
  state, close controls, add button, File menu, and caption buttons.

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| The whole strip becomes draggable and tabs stop receiving input | Register only `WindowDragRegion` with `SetTitleBar`; test every interactive strip element. |
| Caption buttons cover the final tab or add button | Reserve both AppWindow insets in layout and update them after DPI conversion. |
| No grab area remains with many tabs | Keep the footer's minimum width and accept tab shrinking/overflow first. |
| Custom colors fail in contrast mode | Use WinUI theme resources and system caption controls; avoid hard-coded foregrounds/backgrounds. |
| Title-bar work changes document behavior | Leave `DocumentSession`, WebView2 hosts, bridge routing, and save/close state machines unchanged. |
| Tall caption buttons do not align with the installed SDK's TabView | Treat standard-versus-Tall as an E0 measured decision and keep the choice local to `InitializeTitleBar()`. |
| DPI transitions leave stale inset widths | Recalculate from `XamlRoot.RasterizationScale` on title-strip load/size changes and verify cross-monitor moves. |

## Rollback boundary

The implementation should remain easy to revert: remove title-bar
initialization, remove the footer/inset wrapper, and return `DocumentTabs` to
row zero. No persisted workspace schema, bridge protocol, document format, or
WebView2 origin changes are part of this work.

## Definition of done

- The native tabs occupy the top title-bar area in a browser-like layout.
- System caption controls, Snap Layouts, drag, system menu, and maximize/restore
  gestures work normally.
- Tabs and all strip controls remain fully interactive at supported sizes and
  DPI values.
- The tab strip is usable with keyboard, Narrator, contrast themes, and RTL.
- Existing document, storage-isolation, save, close, restore, recovery, and
  external-file behavior passes unchanged.
- The new packaged title-bar smoke test and recorded manual validation pass.
