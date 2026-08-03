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

## Development Environment

### Installing the .NET 10 SDK

Sessions usually start without a .NET SDK. Install it before doing anything else — you cannot build, test, or verify without one.

**Preferred: the Ubuntu archive.** On Ubuntu 24.04 (noble) the SDK is packaged in `noble-updates`:

```bash
sudo apt-get update          # do not skip this
sudo apt-get install -y dotnet-sdk-10.0
dotnet --version             # expect 10.0.x
```

`apt-get update` is not optional. A stale package index resolves to `.deb` files that have already been superseded in the pool, and every download fails with a 404 that looks like the package is missing.

**Other options, if the archive is unavailable:**
- `https://dot.net/v1/dotnet-install.sh` — the official install script. It downloads binaries from `builds.dotnet.microsoft.com`, which is **often blocked by egress policy** in sandboxed sessions. A 403 on `CONNECT` is a policy denial: report it, do not try to route around it.
- The `actions/setup-dotnet@v4` action, which is what CI uses. Not available locally.

NuGet (`api.nuget.org`) is normally reachable even when the SDK download host is not, so package restore works once an SDK is present.

### Playwright browsers

Some environments pre-provision Chromium at `/opt/pw-browsers` (`StaticSiteFixture` detects this automatically). Otherwise:

```bash
pwsh tests/FamilyTree.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
```

## Verifying UI work with Playwright

**Assertions confirm that elements exist and are wired up. They do not confirm the page looks right.** Every one of these shipped with a fully green test suite:

- Rail navigation styled as `display: inline` because the scoped CSS never matched, leaving labels overflowing the rail
- A visible focus ring around the page heading from `FocusOnNavigate`
- Blazor's "An unhandled error has occurred" banner permanently visible, because the replaced stylesheet dropped its `display: none`
- The top bar forcing 29px of horizontal scroll on a phone

So when you change anything that renders:

1. **Run the app and look at it.** `dotnet test tests/FamilyTree.E2E.Tests` publishes the site, serves it exactly as GitHub Pages does, and writes screenshots to `FAMILYTREE_SCREENSHOT_DIR` (defaulting to a temp directory). Read the PNGs — do not just check the suite went green.
2. **Check both viewports.** 1440×900 and 390×844 are captured by default. Mobile breaks silently and often.
3. **Diagnose with the browser, not by reading CSS.** When something looks wrong, query `getComputedStyle` and `getBoundingClientRect` through Playwright. Reading the stylesheet and reasoning about it is how the `::deep` bug above got misdiagnosed twice — the CSS was correct, it simply was not being applied.
4. **Convert every visual bug you find into a computed-style assertion** in `tests/FamilyTree.E2E.Tests/ShellLayoutTests.cs`. Screenshots find these bugs; assertions are what stop them coming back. bUnit cannot: it renders markup without a CSS engine, so all of the above pass at the component level.
5. **Make failures name the culprit.** The horizontal-overflow test reports the widest offending selector, which turns a debugging session into a one-line fix.

Screenshots are a review artifact, not a gate — font and platform rendering differ enough that image comparison is flaky. Assert on computed values instead. The exception is the tree view (PR 10), where layout genuinely is the feature and baseline comparison earns its keep.

### Blazor CSS isolation

Scoped `.razor.css` does **not** apply to elements rendered by child components — `<NavLink class="x">` never receives the parent's scope attribute, so a plain `.x` selector silently matches nothing. Reach through with `::deep` from a scoped ancestor:

```css
.rail__items ::deep .rail__item { ... }
```

This fails silently and looks like the stylesheet did not load. Suspect it whenever styles apply to plain elements but not to components.

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
- **Application services:** Test the success path, every validation failure, and the not-found case. Services return `Result<T>` rather than throwing, so assert on the result, not on exceptions
- **UI validation:** Test that invalid input (empty required fields, wrong formats, out-of-range values) is caught and that valid input is accepted — test the validation logic, not just that a component renders
- **Rendered appearance:** Anything that renders needs an end-to-end check in a real browser. See "Verifying UI work with Playwright" above

### Conventions
- Place tests in a separate `*.Tests` xUnit project mirroring the source project structure
- Use descriptive method names that read as sentences: `RejectsPerson_WhenFirstNameIsEmpty`
- One assertion per test where practical; avoid mega-tests that cover multiple behaviors
- Mock dependencies at the repository-interface boundary using Moq — unit tests must not touch IndexedDB or a browser
- Component tests use bUnit and cover markup and behaviour; computed layout and appearance belong in `FamilyTree.E2E.Tests`, which has a real CSS engine

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
