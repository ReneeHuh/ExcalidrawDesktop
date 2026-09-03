# Title bar, keyboard, DPI, theme, and accessibility validation

Date: September 3, 2026

Status: Automated validation passed; hardware- and assistive-technology matrix
still requires manual execution.

## Test environment

- Windows 11 Pro 64-bit, version 10.0.26200 (build 26200)
- Lenovo 11A4002AUS
- Debug x64 packaged development build
- Display scale observed by the app: 125% (120 DPI)
- Native WinUI title bar with system-rendered caption buttons

## Automated packaged result

Command:

```powershell
.\SmokeTests\Invoke-TitleBarTabsSmokeTest.ps1 -SkipBuild
```

Result: Passed

The packaged test verifies:

- content is extended into the native title bar;
- physical caption insets are converted using the current XAML rasterization
  scale;
- the dedicated drag region remains present;
- tab selection, reordering, Settings-tab switching, and tab close work;
- `Ctrl+T`, `Ctrl+N`, `Ctrl+W`, `Ctrl+O`, `Ctrl+S`, `Ctrl+Shift+S`,
  `Ctrl+Shift+W`, `Ctrl+Alt+S`, `Ctrl+Tab`, and `Ctrl+Shift+Tab` remain
  registered on the tab workspace;
- both explicit light and dark app-frame themes reach the native title bar;
- the tab strip, File menu, Settings button, editor, and system minimize,
  maximize, and close buttons are present in the UI Automation tree;
- drawing-tab accessible names include a localized readable document state;
- the native status bar follows the active drawing, exposes accessible names,
  switches to a native conflict action, and hides on Settings;
- editor startup failures expose Retry, Close tab, and optional WebView2 help
  actions with accessible names;
- retry replaces the failed WebView and reaches bridge-ready state with a fresh
  owned set of subscriptions;
- closing a tab detaches CoreWebView2 events and virtual-host mapping, routed
  drag/drop handlers, and file-watcher delegates before closing the WebView;
- closing a tab deletes its recovery snapshot after pending snapshot work;
- hibernation releases the same WebView resources and recreation attaches a
  fresh owned set of handlers.

Observed packaged output:

```text
Result                   : Passed
InsetsDpiAdjusted        : True
CurrentDpi               : 120
CurrentScalePercent      : 125
TabInteractions          : Passed
KeyboardAccelerators     : Passed
LightAndDarkThemes       : Passed
AutomationNames          : Passed
NativeStatusBar          : Passed
CaptionButtonAutomation  : Passed
FailureActions           : Passed
FailureRetry             : Passed
ClosedTabResourceCleanup : Passed
RecoverySnapshotCleanup  : Passed
```

## Manual validation matrix

These cases cannot be certified by a single automated desktop session. Record
the tester, hardware, Windows build, display configuration, result, and a short
note for every row before declaring Phase T4 complete.

| Area | Required coverage | Result |
| --- | --- | --- |
| Window gestures | Drag, Aero Snap, restore-from-drag, double-click maximize/restore, and system menu | Not run |
| Caption controls | Minimize, maximize/restore, close, and Windows 11 Snap Layouts | Automation metadata passed; pointer behavior not run |
| Tab pointer input | Recent, add, select, close, reorder, middle-click, and context-menu actions near the drag region | Core interactions passed; full pointer matrix not run |
| Keyboard | Full workflow with focus in the native strip, Settings, and WebView2 | Accelerators present; keyboard-only traversal not run |
| Window sizes | 1, 5, 10, and 20 tabs at narrow, normal, maximized, and snapped widths | Not run |
| DPI | 100%, 125%, 150%, 175%, and 200%, including mixed-DPI monitor moves | 125% passed; remaining scales not run |
| Theme | Light, dark, two contrast themes, active/inactive, and increased text scaling | Light/dark passed; remaining states not run |
| Screen reader | Narrator announces tab name, selected state, dirty state, and standard caption controls | UIA names passed; spoken output not run |
| Input | Mouse, touch, pen, IME, RTL, and reduced-motion behavior | Not run |

## Release gate

The automated portion is repeatable and passing. Phase T4 remains open until
the manual matrix has been exercised on suitable hardware and all failures are
resolved or explicitly accepted.
