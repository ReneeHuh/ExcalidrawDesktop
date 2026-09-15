# Guidelines

- For new DOM/browser API usage, use `app.ownerDocument` and `app.ownerWindow` instead of globals; without `app`, derive them from the mounted node's `ownerDocument` and its `defaultView`.
- Keep native feature UI, view models, dialogs, and controls together under `Views/<Feature>/`; see `docs/CODE_ORGANIZATION.md` for ownership and folder conventions.
- Keep one `MainViewModel` per window and reuse its settings view model. Application-wide services belong to `ApplicationWorkspaceCoordinator`; live editor resources belong to `DocumentSession`.
- Keep view models focused on display state and bindings. Preserve the existing document, editor, export, and close controllers and Core's independence from WinUI.
- Use `AppLogger` with ReadPlease-style `[Component]` messages, meaningful levels, and exception arguments. Preserve caller member/line metadata when adding logging wrappers; see `docs/LOGGING.md`.
