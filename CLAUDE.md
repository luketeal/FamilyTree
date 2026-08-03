# FamilyTree — Claude Instructions

## Project
Client-only Blazor WebAssembly family tree app. **There is no backend** — see `docs/decisions/ADR-004-client-only-wasm-local-first.md` and `docs/implementation-plan.md`.

Tech stack: Blazor WebAssembly deployed to GitHub Pages, all data local-first in the browser via IndexedDB, UI in a `FamilyTree.UI` Razor Class Library, business rules in `FamilyTree.Application`, domain model and repository interfaces in `FamilyTree.Domain`.

Do not add EF Core, SQLite, ASP.NET Core hosting, or a server project. They were removed deliberately.

**Keep the API seam intact.** A server backend is not built, but must stay swappable — a future `FamilyTree.Storage.Api` implementing the same repository interfaces over `HttpClient` should be a DI change, nothing more:
- Data access only through `FamilyTree.Domain` repository interfaces — never touch the store directly from a component
- **No N+1 access patterns.** Fetch in bulk; never loop a per-id read. Free against IndexedDB, catastrophic over HTTP
- Mutations that span multiple records go through one repository call so they can map to one request later
- JS interop via `IJSRuntime` async only; no synchronous blocking (`.Result`, `.Wait()`)
- No multi-threading assumptions — WASM is single-threaded

**Data durability is a product requirement, not a feature.** The browser is the only copy. Export/import is the backup mechanism; treat anything that risks silent data loss as a bug.

## Branch Naming
Always use: `claude/<short-kebab-description>-<4-char-random-suffix>`
Example: `claude/add-person-form-k9xQ`

## Commit Messages
- Imperative mood, sentence case, no trailing period
- No conventional-commit prefixes (no `feat:`, `fix:`, etc.)
- Single line for simple changes; add a blank line + body for context if needed
- Good: `Add birth date validation to person form`
- Bad: `feat(form): add birth date validation`

## Testing

Always write unit tests alongside any code change. Tests are not optional.

### Coverage requirements
- **Business logic:** Every function with conditional branches, calculations, or data transformations must have tests covering the happy path and all meaningful edge cases
- **API endpoints:** Test success responses, validation errors, and not-found cases
- **UI validation:** Test that invalid input (empty required fields, wrong formats, out-of-range values) is caught and that valid input is accepted — test the validation logic, not just that a component renders

### Conventions
- Place tests in a separate `*.Tests` xUnit project mirroring the source project structure
- Use descriptive method names that read as sentences: `RejectsPerson_WhenFirstNameIsEmpty`
- One assertion per test where practical; avoid mega-tests that cover multiple behaviors
- Mock external dependencies (DbContext, services) at the boundary using Moq — don't hit real infrastructure in unit tests

### When code is modified
If you change existing code, update or add tests to cover the modified behavior. Never delete tests to make a PR pass.

## Pull Requests

### When to create vs update
- Create a new PR when starting work on a new branch
- Update an existing PR (via `mcp__github__update_pull_request`) when:
  - New commits are pushed to the same branch
  - The scope of the work changes
  - Reviewer feedback is addressed
- Never close and re-open a PR to make edits — always update it

### Updating PRs
- When updating a PR body, preserve all already-checked Test Plan items (`- [x]`) — never uncheck them
- Only add or revise unchecked items or new sections

### Title format
- Imperative mood, sentence case, no trailing period
- No ticket numbers or prefixes
- Describe the change, not the files touched
- 72 characters max
- Good: `Add sibling display to person profile page`
- Bad: `feat(profile): US-051 sibling view changes`

### Required body sections
Every PR body must contain these sections in this order:

```
## Summary
One sentence describing what this PR does and why.

## Changes
Bullet list of concrete changes (not file names).
- Add birth place field to person creation form (US-001)
- Guard against duplicate biological parent relationships (US-007)

## Test Plan
Specific actions a reviewer must take to verify the change. Each item must
describe a concrete step and the expected outcome — not a generic todo.

Items fully covered by unit tests (including UI validation tests) are
pre-checked and marked as validated by Claude. Only behaviors that cannot
be verified by unit tests (visual layout, real browser interaction,
multi-system flows) are left unchecked for manual review.

- [x] Submitting the form with no first name shows an inline error — validated by unit tests
- [x] A valid person saves and appears in the list — validated by unit tests
- [ ] Open the app in a browser, create a person, and confirm the name renders correctly in the tree view

## User Stories
List any user story IDs delivered by this PR (omit section if none).
- US-001, US-007
Closes #58, #59
```

### Session URL
Always append the Claude session URL as the last line of the PR body, on its own line with no label. Claude Code appends this automatically.

### User story traceability
`USER_STORIES.md` is the single source of truth for the backlog — do not maintain a parallel set of open GitHub issues.

When creating a PR, infer which stories from `USER_STORIES.md` are fully delivered by the changes. For each fully delivered story:
1. Create a GitHub issue using `mcp__github__issue_write` with the story title, user story text, and acceptance criteria from `USER_STORIES.md` as the body
2. Add a `Closes #N` line for each created issue in the PR body's **User Stories** section
3. The issue will be auto-closed when the PR merges — do not close it manually

Only create issues for stories that are **fully** delivered by the PR. Partial implementations do not get an issue.

### Draft PRs
Open as draft when the branch is not yet ready for review. Convert to ready with `mcp__github__update_pull_request` when complete.

## Architecture Decisions

Significant technical decisions — especially spike results — are recorded in `docs/decisions/` as lightweight ADRs. See `docs/decisions/README.md` for the format and index.

Write an ADR whenever:
- A spike (throwaway prototype) produces a conclusion
- You choose between two non-trivial technical approaches
- The decision would be hard or expensive to reverse
