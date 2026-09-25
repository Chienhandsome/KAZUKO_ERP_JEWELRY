# General Rules

- Read `.ai/PROJECT.md` and `.ai/RULES.md` before modifying code.
- Read the relevant existing documents under `docs/`; `AGENTS.md` defines their required order.
- Follow the existing multi-project and module architecture.
- Prefer existing patterns over introducing new patterns.
- Do not install NuGet packages unless explicitly requested.
- Do not change database schema unless explicitly requested.
- Do not rename public classes, methods, contracts, navigation keys, module IDs, or database fields unless explicitly requested.
- Do not silently fix unrelated code.
- Keep implementation simple.
- Avoid over-engineering CRUD features.
- Reuse existing utilities, services, helpers, semantic resources, styles, and controls where appropriate.
- Keep `Kazuko.Core` independent of WPF, Firebird-specific implementations, and other infrastructure details.
- Do not describe planned architecture as already implemented. Update `.ai/PROJECT.md` when the repository gains a real convention that other agents must follow.

# WPF Rules

- Preserve the current partial-MVVM architecture.
- Use ViewModels for screen state, commands, validation state, and orchestration when that matches current patterns.
- Code-behind is acceptable for View-only behavior such as `PasswordBox` synchronization; do not put business logic or SQL in code-behind.
- Prefer Binding and commands when they match the existing architecture.
- Do not force a full MVVM migration of static or simple screens unless a task requires it.
- Do not modify shared `ResourceDictionary` files, `App.xaml`, global styles, or the appearance service unless the task explicitly allows those files.
- Do not change business logic when the task is UI-only.
- Use semantic `DynamicResource` keys for theme-sensitive colors, brushes, and font sizes.
- Maintain consistent Button, TextBox, PasswordBox, ComboBox, card, spacing, margin, and typography patterns.
- Verify UI changes in Light and Dark themes and at 90%, 100%, 125%, and 150% font scale.
- The repository has no approved WPF DataGrid convention yet. Do not create a global DataGrid style as an unrelated side effect of a single CRUD screen.

# Firebird Rules

- The current repository has no Firebird implementation. Never invent or bypass a missing connection/repository convention.
- Always use parameterized SQL.
- Never concatenate user input into SQL.
- Once an approved Firebird connection helper exists, reuse it rather than opening connections ad hoc.
- Dispose connections, commands, transactions, and readers correctly.
- Use transactions for multi-step writes when needed.
- Follow the existing ID-generation strategy after one is established; until then, the task must explicitly define and review the strategy.
- Do not assume Firebird SQL syntax is identical to SQL Server.
- Do not modify database schema unless the task explicitly requires it.
- Do not add a Firebird NuGet provider unless the task explicitly allows package installation.
- Keep SQL out of Views, ViewModels, and code-behind.

# Agent Rules

## Codex

- Acts as coordinator, planner, and reviewer by default.
- Does not implement feature code unless explicitly requested.
- Inspects the current repository before planning.
- Creates tasks with clear, non-overlapping file ownership.
- Prevents overlapping active tasks.
- Reviews completed branches/diffs when requested and records findings under `.ai/reviews/`.

## Cursor

- Primary implementation agent.
- Preferred for WPF UI/XAML.
- Preferred for normal CRUD implementation.
- Preferred for quick fixes and iterative UI changes.
- Must stay within the task's Allowed Files unless ownership is amended before editing.

## Antigravity

- Secondary implementation agent.
- Preferred for independent modules or supporting services that can be worked on in parallel.
- Must not be assigned files currently owned by another active task.
- Must stay within the task's Allowed Files unless ownership is amended before editing.

# Task Ownership Rules

- Every implementation task must specify Allowed Files.
- Every implementation task must specify Forbidden / Do Not Modify files.
- Every implementation task must specify acceptance criteria and build/verification steps.
- A task must NOT be created if its Allowed Files overlap with another active task in `.ai/tasks/doing/`, unless the overlap, reason, edit order, and responsible integrator are explicitly documented and approved in both task files.
- Before moving a task from `todo` to `doing`, re-check all active Allowed Files for overlap.
- Each agent works in its own Git branch/worktree.
- Build the solution after implementation.
- Only fix build errors caused by the assigned task. Report unrelated failures instead of modifying unrelated code.
- Do not edit another active task's files to make a local build pass.
