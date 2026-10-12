# Review - TASK-002

## Summary

The guide is well-structured, written in Vietnamese, based on local/legacy read-only inspection, and covers the requested runtime layout, multi-machine limitation, and troubleshooting. The task stayed documentation-only.

## Critical Issues

None.

## Functional Issues

None.

## Database / Firebird Issues

1. The guide labels a long list of individual files as a minimum runtime requirement. The supported operational rule should be to preserve the complete user-provided Firebird 5.0.4 x64 bundle; individual plugin/dependency contents can vary by distribution and deployment target.
2. The production layout says `ProgramData` should be writable by every user. Revise this to least-privilege write access for the application's authorized local users/service account; do not recommend broad Full Control.

## WPF / UI Issues

None.

## Architecture Issues

1. The development-output example is not current: the executable is `KAZUKO_ERP_JEWELRY.exe`, and normal Debug output is under `src\\Kazuko.App\\bin\\Debug\\net8.0-windows\\`, not the documented root `bin\\Debug\\net8.0-windows\\win-x64\\` path.
2. Section 1 should state that TASK-001 is in progress and readers must check its status before assuming whether runtime copying/authentication is implemented. Do not leave a static claim that will immediately become stale when TASK-001 is merged.

## Code Quality Issues

The guide is more detailed than necessary for an operations quick guide. Keep the valuable troubleshooting content, but phrase unverified platform-specific requirements (for example a separately installed VC++ redistributable) as conditional checks, not unconditional deployment requirements.

## Unrelated Changes Detected

The repository currently has uncommitted source/project changes within TASK-001's allowed scope. They are outside TASK-002's allowed files and are not treated as changes from this documentation task.

## Required Fixes

1. Correct the executable name and Debug output path.
2. Make the TASK-001 implementation status time-safe.
3. Replace the hard-coded "minimum file" prescription with a complete-bundle rule, while retaining examples of commonly required files/folders.
4. Replace broad ProgramData permissions with least-privilege guidance.
5. Mark VC++ runtime guidance as conditional on the chosen bundle/machine.

## Optional Improvements

Add a short note that a release installer has not been selected or implemented yet, so the documented production layout is a target layout rather than an installation procedure.

## Follow-up Review

All required fixes were verified in `docs/05-EMBEDDED-FIREBIRD-OPERATIONS.md`. The guide now uses the correct executable/output path, distinguishes TASK-001's active status from merged behavior, preserves the complete-bundle rule, and applies least-privilege/conditional dependency guidance. The optional installer note was also added.

## Review Result

Ready to merge
