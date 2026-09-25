# AI Collaboration Workflow

## Roles

- Codex coordinates, plans, assigns ownership, and reviews.
- Cursor is the primary implementation agent, especially for WPF/XAML and routine CRUD work.
- Antigravity implements independent secondary tasks that do not overlap active ownership.

## Task Lifecycle

1. Codex analyzes the requested feature against the actual repository and `.ai/PROJECT.md`.
2. Codex breaks the work into independently buildable tasks with non-overlapping file ownership.
3. Codex creates task files under `.ai/tasks/todo/` from `TASK_TEMPLATE.md`.
4. Every task is assigned to Cursor or Antigravity.
5. Before starting, the assigned agent confirms that Allowed Files do not overlap any task in `.ai/tasks/doing/`.
6. Each agent works in a separate Git branch/worktree.
7. The task file moves from `todo` to `doing` when work starts.
8. The agent changes only Allowed Files and runs the task's verification/build commands.
9. The agent commits the completed work on its branch.
10. The task file moves from `doing` to `done`, including implementation notes and verification results.
11. When requested, Codex reviews the branch/diff and saves notes under `.ai/reviews/` using `REVIEW_TEMPLATE.md`.
12. The implementation agent fixes valid review issues on the same task branch.
13. Final code is merged only after required review issues are resolved and the build succeeds.

Task files are coordination state. Moving a task means moving the same Markdown file between status directories, not creating duplicate copies.

## Ownership Check

Before creating or activating a task:

1. Read all files in `.ai/tasks/doing/`.
2. Compare path patterns in Allowed Files.
3. If any path can refer to the same file, do not activate the new task.
4. If overlap is unavoidable, document the exact shared files, edit order, why parallel work remains safe, and who integrates the result. Approval must appear in both task files.

Shared files such as `App.xaml`, `App.xaml.cs`, `ShellViewModel.cs`, Core contracts, solution/build files, and shared theme dictionaries should normally have only one owner at a time.

## Recommended Git Worktrees

The current directory was observed without Git metadata. A maintainer must initialize or connect the repository before using branches/worktrees. Do not initialize Git merely to execute this workflow unless explicitly requested.

Recommended layout:

```text
Main repository:        <repo-name>
Cursor worktree:        <repo-name>-Cursor
Antigravity worktree:   <repo-name>-Antigravity
```

Generic examples only; review branch names and paths before running:

```bash
git worktree add ../<repo-name>-Cursor -b ai/cursor/<task-id>
git worktree add ../<repo-name>-Antigravity -b ai/antigravity/<task-id>
```

After a branch has been merged and the worktree is no longer needed:

```bash
git worktree remove ../<repo-name>-Cursor
git worktree remove ../<repo-name>-Antigravity
```

Do not remove a worktree with uncommitted changes. Worktree creation/removal is performed by the maintainer or an agent explicitly authorized to manage Git state.

## Repository-Specific Assignment Guidance

- Assign Cursor a WPF screen end-to-end when its files are isolated within one module project.
- Assign Antigravity an independent service, validator, test project, or separate module whose Allowed Files do not touch Cursor's module.
- Do not have both agents edit `Kazuko.Shared.UI/Themes/*` while also building separate screens. Give shared-style ownership to one dedicated task, then let screen tasks consume the finalized resources.
- Do not parallelize initial Firebird infrastructure and CRUD repositories until the connection, transaction, and ID-generation conventions have been reviewed and documented.
