# Excalidraw Desktop Future Product Roadmap

Status: Active; F0 is partially implemented and later phases remain proposed
Created: September 1, 2026
Originally sequenced after: [`TABBED_DESKTOP_PLAN.md`](TABBED_DESKTOP_PLAN.md)

## Outcome

Grow Excalidraw Desktop from a reliable tabbed drawing application into a complete local-first visual workspace for Windows.

The product should retain Excalidraw's fast, approachable canvas while adding capabilities that make sense on the desktop: dependable files, fast navigation, reusable workflows, presentation tools, searchable local knowledge, and optional connected features.

## Starting gate

F0 implementation began before every tabbed desktop validation gate was
closed. Those gates still block a stable release. In particular:

- Multiple documents open, save, close, restore, and recover reliably.
- Each tab has isolated browser storage and session-bound bridge routing.
- File operations cannot cross document boundaries.
- The packaged application passes offline multi-tab smoke tests.
- Memory behavior has been measured at the supported tab count.

Features in this roadmap must not weaken those guarantees.

## Product principles

1. Local drawings remain useful without an account or network connection.
2. Normal `.excalidraw` files remain portable and compatible with upstream Excalidraw.
3. Desktop features should reduce friction without crowding the canvas.
4. Metadata, indexing, and recovery must never silently alter a user's drawing file.
5. Connected services and AI remain optional, transparent, and removable.
6. Reliability, accessibility, and performance are release requirements rather than final polish.
7. Upstream Excalidraw is consumed through an exact-version npm package and updated only after desktop compatibility validation.

## Product horizons

```text
Tabbed workspace
      ↓
Desktop completion and release quality
      ↓
Power-user workflows and reusable content
      ↓
Presentation and publishing
      ↓
Local visual knowledge workspace
      ↓
Optional intelligence and extensibility
      ↓
Optional sync and collaboration
```

Each horizon must be valuable as a shippable product increment. Later horizons do not block releasing the earlier ones.

## Phase F0: Desktop completion and release quality

### Goal

Make the tabbed application feel dependable and native enough to use every day.

### Features

- [ ] Complete file associations: `.excalidraw` is implemented;
  `.excalidrawlib` remains.
- [x] Route Explorer activation into a new or existing tab across all windows.
- [ ] Complete recent and pinned drawings: recent files are implemented;
  pinning remains.
- [ ] Complete Windows jump-list actions: recent-file integration is
  implemented; New Drawing and pinned-file actions remain.
- [x] Support multi-file drag and drop.
- [x] Reveal the active file in Explorer and copy its path.
- [x] Detect files changed, renamed, moved, or deleted outside the application.
- [x] Restore window size, position, tab order, active tab, and multi-window
  membership safely.
- [x] Add clear WebView2 runtime failure and repair guidance.
- [ ] Add signed MSIX packaging and the selected update channel.
- [ ] Add structured diagnostic logs that exclude drawing content.
- [ ] Complete keyboard, screen-reader, high-contrast, touch, pen, and
  mixed-DPI validation.

### Exit criteria

- A user can install, update, and remove the app without manual cleanup.
- Opening a drawing from Explorer behaves predictably whether the app is closed or running.
- External file conflicts are detected before overwrite.
- Recovery, file activation, and update behavior pass packaged tests.
- The app is suitable for a stable public desktop release.

## Phase F1: Power-user workflow

### Goal

Help frequent users create and navigate drawings faster without making the basic editor harder to understand.

### Features

- Add a native command palette covering tabs, files, export, view, and application commands.
- Add configurable keyboard shortcuts where they do not conflict with Excalidraw editing shortcuts.
- Add tab search and a quick-open list for recent and currently open drawings.
- Add duplicate tab, reopen closed tab, and reopen previous workspace commands.
- Add optional split-view comparison after normal tabs are stable.
- Add saved workspace layouts for frequently used groups of drawings.
- Add native notifications only for actionable background results or failures.
- Add a focused settings experience for appearance, files, recovery, input, privacy, and updates.

### Command-routing rule

The native shell owns application, tab, and file commands. The active editor owns drawing commands. Shortcut routing must have a documented precedence order and must not depend on which internal WebView element currently has focus.

### Exit criteria

- Every native command is reachable by keyboard and discoverable in the command palette.
- Switching among at least ten open drawings is fast and understandable.
- Shortcut conflicts are covered by automated or manual acceptance tests.
- Settings changes propagate consistently to all live and future tabs.

## Phase F2: Templates, components, and export workflows

### Goal

Turn repeated drawing tasks into reusable desktop workflows.

### Templates

- Ship a small curated starter set: blank board, flowchart, architecture diagram, wireframe, retrospective, meeting notes, and presentation.
- Allow users to save a drawing as a personal template.
- Show template previews without executing remote content.
- Keep user templates in an application-managed folder that can be opened and backed up normally.
- Allow importing and exporting template packs.

### Reusable content

- Provide a native library manager for `.excalidrawlib` files.
- Support pinned libraries and recently used components.
- Keep library import behavior compatible with upstream Excalidraw.
- Avoid inventing a second incompatible component format.

### Export center

- Export PNG, SVG, and PDF through a consistent desktop interface.
- Remember non-destructive export preferences per document or workspace.
- Support transparent backgrounds, scale, padding, selected elements, and embedded scene data.
- Add batch export for multiple frames or multiple open documents.
- Copy common export formats directly to the clipboard.
- Run expensive exports asynchronously with cancellation and progress.

### Exit criteria

- A user can create, preview, use, back up, import, and export personal templates.
- Library files still round-trip with upstream Excalidraw.
- Batch export does not block editing in unrelated tabs.
- Export results match agreed visual reference fixtures.

## Phase F3: Presentation mode

### Goal

Let users turn Excalidraw frames into a useful presentation without leaving the desktop app.

### Features

- Treat an ordered set of frames as slides without changing their Excalidraw meaning.
- Add a presentation navigator and reorderable slide list.
- Add full-screen presentation mode with keyboard, mouse, touch, and pen navigation.
- Add presenter notes stored outside the portable drawing unless an interoperable format is agreed.
- Add a presenter view with current slide, next slide, notes, and elapsed time.
- Export frames as a PDF or numbered image sequence.
- Support links between frames for interactive walkthroughs.
- Keep drawing tools available through an explicit annotate mode.

### Data rule

Presentation metadata must use one of these approaches, selected through a compatibility spike:

1. Existing Excalidraw frame order and links only.
2. A separate application metadata record keyed to the file.
3. A documented optional custom-data field that upstream Excalidraw preserves safely.

Do not introduce presentation metadata that excalidraw.com silently destroys or that makes ordinary drawings unreadable.

### Exit criteria

- A frame-based drawing can be presented and exported without duplicating the source file.
- Presentation metadata survives the selected desktop round trip.
- Full-screen presentation works offline on one and multiple monitors.
- Closing presentation mode returns to the original editor state.

## Phase F4: Local visual knowledge workspace

### Goal

Help users find, connect, and organize many drawings while keeping the underlying files portable.

### Features

- Index filenames, user-approved folders, tags, frame names, and searchable text elements.
- Provide fast local search across drawings.
- Add collections that reference files without moving them.
- Add tags and favorites as application metadata.
- Show thumbnails and recent activity without opening every document in a live WebView.
- Support links between local drawings and navigation to linked files.
- Show backlinks and broken-link status.
- Add a visual home screen for recent, pinned, tagged, and recoverable drawings.
- Allow users to exclude folders and clear the index.

### Metadata and index architecture

Use an application-owned local database for derived metadata and search indexes. The database is not authoritative for drawing content.

Store:

- Canonical file identity and last observed file metadata.
- Tags, collections, favorites, and optional presentation metadata.
- Extracted searchable text and thumbnail cache references.
- Links and backlinks.

Do not store the only copy of an unsaved drawing in the index. Recovery remains a separate, explicit subsystem.

The index must be rebuildable from files and user-owned metadata. Users must be able to inspect its scope, pause indexing, remove folders, and delete cached data.

### Exit criteria

- Search returns results from an agreed large local fixture set within the performance target.
- Rebuilding the index does not alter source drawings.
- Moving or renaming a known file preserves metadata when identity can be established safely.
- Deleting the index does not delete drawings, templates, or recovery files.
- Search and thumbnails work completely offline.

## Phase F5: Optional intelligence and extensibility

### Goal

Add assistance and automation without making core drawing features depend on a provider or account.

### Candidate AI features

- Convert a written description into a proposed diagram.
- Convert Mermaid text into editable Excalidraw elements.
- Suggest layout cleanup for selected elements.
- Summarize a drawing's text content.
- Extract action items from meeting boards.
- Generate accessible descriptions for selected diagrams.

### AI rules

- AI is disabled until the user configures and enables a provider.
- Clearly preview what content will leave the device.
- Send only user-selected content when possible.
- Never upload open drawings, recovery snapshots, or indexes in the background.
- Show generated changes as a preview that can be accepted, edited, or discarded.
- Preserve normal undo behavior for accepted changes.
- Provide a fully functional local-only experience when AI is disabled.

### Extensibility

Before adding general plugins, define a narrow capability model for commands, importers, exporters, templates, and scene transformations. Extensions must not receive arbitrary filesystem or native bridge access.

Start with trusted, signed, or locally developed extensions. A public extension marketplace is a separate product and security decision.

### Exit criteria

- Every connected operation requires an explicit user action.
- Provider failures do not block local drawing or saving.
- Privacy controls are understandable and testable.
- Generated scene changes are previewable and undoable.
- Extension permissions are enforced by capability rather than convention.

## Phase F6: Optional sync, version history, and collaboration

### Goal

Allow users to move between devices and work together without compromising the local-file experience.

### Required decisions before implementation

- Whether to use existing Excalidraw collaboration services, support self-hosting, or build a separate service.
- Whether accounts are optional or required only for connected features.
- How encryption keys, identity, sharing links, retention, and deletion work.
- How local files, remote revisions, and offline edits reconcile.
- Whether collaboration documents remain normal `.excalidraw` files or use a separate workspace model.
- How comments and version history are exported or retained.

### Candidate features

- Opt-in folder or workspace synchronization.
- Version history with named checkpoints and restore-as-copy.
- Shared boards with presence and cursors.
- Comments and review mode.
- Conflict visualization and safe copy creation.
- Self-hosted endpoint configuration where feasible.

### Exit criteria

- Local-only users are not required to create an account.
- Offline edits cannot be silently discarded by synchronization.
- Encryption, retention, deletion, and recovery behavior are documented and tested.
- Collaboration failure never corrupts the last valid local document.
- Users can export or detach their work from the connected service.

## Cross-cutting engineering tracks

These tracks continue throughout every phase:

### Compatibility

- Maintain desktop-to-web and web-to-desktop round-trip fixtures.
- Test upstream Excalidraw updates before merging them into the desktop bundle.
- Keep custom metadata optional, documented, and safely preserved.

### Performance

- Measure startup, tab switching, save, export, search, and memory use.
- Move expensive indexing and export work off the UI thread.
- Establish budgets before optimization so regressions are visible.

### Accessibility and input

- Test every native surface with keyboard and screen readers.
- Preserve pen, touch, IME, RTL, high-contrast, and reduced-motion behavior.
- Do not make hover the only way to discover or invoke an action.

### Privacy and security

- Keep privileged operations in narrow native services.
- Validate exact origins and bridge payloads.
- Never record scene contents in diagnostics.
- Make network features opt-in and explain their data boundaries.

### Release engineering

- Run web tests, type checks, .NET tests, format fixtures, and packaged smoke tests in CI.
- Sign release packages.
- Maintain rollback and recovery procedures for failed updates.
- Generate third-party notices and preserve upstream licensing.

## Prioritization rules

When choosing work within or between phases, prefer features that:

1. Prevent data loss or user confusion.
2. Improve everyday local workflows for many users.
3. Reuse the native document/session architecture.
4. Work offline and preserve file portability.
5. Can be validated with clear automated or manual acceptance criteria.

Defer features that require accounts, servers, broad extension permissions, or incompatible file changes until their product and security costs are explicit.

## Roadmap definition of success

Excalidraw Desktop succeeds when it is:

- A dependable Windows application for opening and editing many local drawings.
- Faster than a browser workflow for common file, tab, export, and presentation tasks.
- Capable of organizing a large personal drawing library without locking files into a proprietary store.
- Fully useful offline.
- Safe to extend with optional intelligence or collaboration without weakening local ownership.

## Next planning action

Finish and validate the tabbed workspace first. During its final phase, convert Phase F0 into a release checklist with supported Windows versions, hardware targets, signing strategy, distribution channel, performance budgets, and accessibility matrix.

Do not begin the knowledge, AI, or collaboration phases until desktop file ownership, recovery, and packaged update behavior are proven in production-like builds.
