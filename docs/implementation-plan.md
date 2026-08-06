# Implementation Plan: FamilyTree — Client-Only Blazor WebAssembly

## Context

Per **ADR-004**, this is a client-only Blazor WebAssembly application deployed to GitHub Pages, with all data stored local-first in the browser via IndexedDB. **There is no backend.** Delivery is UI-first and continuous, so real user feedback shapes the interaction design as it is built.

The domain layer is complete — all entities, the `PartialDate` value object, enums, repository interfaces, and unit tests — and is reused directly. Nothing is mocked at the domain level, and the real business rules are enforced from the first screen.

All UI decisions follow the design system in `.design/Family Tree Application.zip` (wireframes through mid-fidelity mockups).

---

## Project Structure

```
src/
  FamilyTree.Domain/          existing — entities, PartialDate, repository interfaces (the API seam)
  FamilyTree.Application/     new — services, DTOs, Result<T>; no storage or UI dependencies
  FamilyTree.Storage.Browser/ new — IndexedDB implementations of the repository interfaces
  FamilyTree.UI/              new — Razor Class Library; all components, pages, CSS
  FamilyTree.App/             new — Blazor WASM host; DI wiring; deploys to GitHub Pages
tests/
  FamilyTree.Domain.Tests/       existing
  FamilyTree.Application.Tests/  new — service and business-rule tests
  FamilyTree.Storage.Tests/      new — IndexedDB round-trip tests
  FamilyTree.UI.Tests/           new — bUnit component tests
  FamilyTree.E2E.Tests/          new — Playwright against the published static site
```

**Deleted by ADR-004:** `FamilyTree.Infrastructure`, `FamilyTree.Infrastructure.Tests`, `FamilyTree.Web`, `FamilyTree.Web.E2E.Tests`. Do not reintroduce EF Core, SQLite, or an ASP.NET Core host. The schema thinking is preserved in ADR-003 and recoverable from git history.

`FamilyTree.Storage.Browser` is named for the swap: a future `FamilyTree.Storage.Api` implementing the same interfaces over `HttpClient` would be a DI registration change and nothing more.

---

## API-Seam Discipline (binding)

A server backend is not built, but must remain swappable. The interfaces survive contact with an API; naive *usage* does not.

- Data access only through `FamilyTree.Domain` repository interfaces — components never touch the store
- **No N+1 access patterns.** Fetch in bulk; never loop a per-id read. Building a 500-node tree via `GetByIdAsync` per node is free against IndexedDB and catastrophic over HTTP. Prefer `GetByIdsAsync(IEnumerable<Guid>)` and whole-set reads.
- Multi-record mutations go through a single repository call, so they can map to one request later rather than a half-failing sequence
- All service methods return `Result<T>` — no exceptions for business rules
- JS interop via `IJSRuntime` async only; no `.Result`, no `.Wait()`
- No multi-threading assumptions — WASM is single-threaded
- `InvariantGlobalization` stays **off** — this app formats dates heavily and needs ICU

---

## Durability Requirements (binding)

The browser is the only copy of the user's data. Genealogy data can represent years of irreplaceable research, so data loss is treated as a defect class, not a user error.

- `navigator.storage.persist()` is called at startup so the origin is exempt from routine eviction
- Export and import land **early** (PR 5), before most features — until they exist, every user is one cache-clear from total loss
- Every later PR that adds a persisted shape extends export coverage in the same PR
- The UI surfaces "last exported N days ago" and prompts when stale
- A `schemaVersion` is stamped on the stored payload so a shape change detects and migrates (or safely resets) rather than crashing
- File System Access API is evaluated in PR 5 so the tree can live in a user-controlled file

---

## Responsive Scope

The mid-fidelity mockups specify a desktop shell only. Rather than invent a mobile design that does not exist yet, the position is:

- **The shell is responsive now.** Below 768px the icon rail becomes a bottom bar and the top bar compacts. Every later page nests inside the shell, so it is the single most expensive thing to retrofit and the cheapest to get right up front.
- **No fixed pixel widths on containers** in any later PR. Use flex/grid with `max-width`, and remember that a grid item's default `min-width: auto` will refuse to shrink — `minmax(0, 1fr)` is usually what you want.
- **Every PR checks 390px.** The E2E suite asserts no horizontal scroll at 390/768/1440 and captures both viewports as screenshots, so regressions surface without anyone remembering to look.
- **Genuinely mobile-specific UX is deferred** until the design exists: the tree canvas on a phone, and the 360px profile panel becoming a bottom sheet. These need design decisions, not media queries, and they cost the same later as now.

The reasoning is that the cost curve is asymmetric. Shell responsiveness is cheap now and expensive later because everything nests in it; touch-first tree interaction is expensive whenever it happens. Testers for a family tree app will open the link on a phone, and the feedback loop is the whole point of shipping UI first — so the shell must not be broken for them.

## Design System Reference

**Shell**
- Left icon rail (64px): Tree, People, Relate, Import, Settings
- Top bar (52px): logo + tree stats + centered search bar + Undo button + "Add person" (coral primary)

**Color tokens** (defined in `mid-tokens.jsx`, implemented as CSS custom properties)
- `--paper: #fbfaf7` · `--paper2: #f3f0e9` · `--ink: #1c1a17` · `--ink3: #6f6a5e`
- `--coral: #c96442` (primary action/focus) · `--coral-bg: #fbeee7`
- `--teal: #3f7d6e` (adoptive relationships) · `--teal-bg: #e6efeb`
- `--line: #e3ddce` · `--line2: #d8d2c4`

**Typography**: Inter (UI), Source Serif 4 (headings/names), JetBrains Mono (dates)

**Edge styles in tree**
- Biological: solid 1.5px ink
- Adoptive: dashed 1.5px teal, "(A)" label
- Marriage: double line (two 1.2px lines, 4px apart)
- Stepparent: dotted 1.5px neutral
- Phantom/inferred: dashed ink5

**Key reusable components**
- `PersonChip` — avatar + name + subtitle, four variants: bio (solid), adoptive (dashed teal), step (dotted), phantom (hatched)
- `PersonSearchSelect` — debounced search dialog with "Create new person" inline option
- `AddRelationshipDialog` — unified 3-step wizard (kind → person → details) used for all relationship types

---

## Story Scope

**All 53 stories are in scope.** IndexedDB removes the storage ceiling that would have deferred photos (US-005), and browser print-to-PDF with a print stylesheet covers US-050 without a server-side PDF library.

---

## PR Sequence

**One PR open at a time.** The deploy workflow publishes to GitHub Pages from
any branch, so concurrent PRs overwrite each other's deployment and there is no
longer a single answer to "what does the live site do right now". Reviewing a
change in the browser is the point of the demo-first ordering, so the sequence
below is worked one entry at a time even where the dependency graph would allow
parallelism. Only relax this if the workflow is changed to give PR builds their
own preview URL.

### PR 1 — WASM app, shared RCL, and GitHub Pages deployment
**Stories:** None (foundation)

- Create `src/FamilyTree.UI/` (Razor Class Library) and `src/FamilyTree.App/` (Blazor WASM host).
- Carry over from PR #65 (the only surviving content): `wwwroot/css/tokens.css`, `wwwroot/css/app.css`, and the `MainLayout`, `IconRail`, `TopBar` components with their `.razor.css` and `data-testid` selectors.
- Remove `FamilyTree.Infrastructure` and rewrite `FamilyTree.slnx` for the new project set.
- `.github/workflows/deploy.yml` — the repository's first CI workflow. On PR: `dotnet build` + `dotnet test`. On push to `main`: `dotnet publish -c Release`, rewrite `<base href>` to `/FamilyTree/`, copy `index.html` to `404.html`, write `.nojekyll`, deploy via `actions/deploy-pages`.
- Create `tests/FamilyTree.UI.Tests/` (bUnit) and `tests/FamilyTree.E2E.Tests/` (Playwright against the published static output) with a boot smoke test asserting the rail and top bar render.

**Outcome: a live URL exists.** Shell only, no data yet.

### PR 2 — Application layer and IndexedDB storage
**Stories:** US-040 at service layer

- `src/FamilyTree.Application/` (references `FamilyTree.Domain` only)
  - `Common/Result.cs` — `IsSuccess`, `Value`, `Error`, `IsWarning`
  - `Common/PersonSummaryDto.cs`, `PersonDetailDto.cs` — flat DTOs; UI never touches domain entities
  - `Services/CircularReferenceChecker.cs` — BFS from `proposedParentId` across bio **and** adoptive ancestor links
  - `Services/PersonService.cs` — required fields, birth-before-death ordering, duplicate name+date warning (non-blocking), phantom person creation (US-054)
- `src/FamilyTree.Storage.Browser/` — IndexedDB implementations of all four repository interfaces via `IJSRuntime` and a small JS module.
  - **Persist flat records, not the object graph.** `Person` has a private constructor, private setters, and bidirectional navigation collections; `System.Text.Json` can neither round-trip it nor escape the circular references. Define `PersonRecord`, `BiologicalLinkRecord`, `AdoptiveLinkRecord`, `MarriageRecord`, `StepparentRecord`, and rehydrate through domain factory methods.
  - `PartialDateJsonConverter` for the value object.
  - Bulk read paths from the start per the API-seam discipline.
  - `schemaVersion` stamp; `navigator.storage.persist()` on startup.
- Seeded sample family exercising the hard cases: half-siblings via a shared parent, an adoption, a remarriage after widowhood, a phantom grandparent, one speculative-certainty link. Plus "Reset to sample data" in Settings.
- Tests: `Application.Tests` (circular checker — no cycle, direct, indirect, disconnected root; `PersonService` rules) and `Storage.Tests` (round-trip per entity type; `PartialDate` precision and `IsApproximate` preserved).

### PR 3 — Tree visualization spike and ADR-005
**Stories:** None (spike) — **deferred until after PR 4**

> Has no code dependency on PRs 4–9 and could be built at any point, but is
> **not run concurrently with them**. The deploy workflow publishes to GitHub
> Pages from any branch, so two open PRs contend for the live site and
> "check the deployed version" stops having a single answer. Sequence it into
> a gap when nothing else is awaiting review — it only blocks PR 10.

The project's largest unknown, resolved before the tree is built. Evaluate against: DAG rendering (a person may have both biological and adoptive parents — a directed acyclic graph, not a strict tree), pan/zoom, five distinct edge styles, nodes ~172×70px, performance at ~500 nodes, phantom node styling, mini-map.

**Must work under WebAssembly**, and payload size counts against the download budget. D3 via JS interop is the reference option.

Output: `docs/decisions/ADR-005-tree-visualization-library.md`.

### PR 4 — Person CRUD, people list, and shared components
**Stories:** US-001, US-002, US-003, US-004, US-006, US-041, US-044, US-052, US-053

- `Pages/People/PeopleListPage.razor` — empty state with "Add your first person" (US-052); excludes phantom persons
- `Pages/People/PersonProfilePage.razor` — all fields; "(née …)"; "Deceased" badge; age with "~" when approximate; relationship section stubs; also renders as a 360px right panel from the tree
- `Pages/People/AddPersonPage.razor`, `EditPersonPage.razor` — required names, non-blocking duplicate warning, unsaved-changes indicator
- `Shared/QuickAddPersonPopover.razor` (US-053), `PersonChip.razor` (four variants), `DeleteConfirmModal.razor`, `PartialDateInput.razor` (year-only stub), `ToastNotification.razor`, `ErrorAlert.razor`

E2E: empty state → add form; missing first name shows inline error; a valid person **survives a page reload** (IndexedDB round-trip); quick-add opens, accepts, closes.

### PR 5 — Export and import
**Stories:** US-048, US-049

Deliberately early — this is the durability mechanism, not a feature. Covers everything that exists at this point; every later PR extends it.

- `Application/Services/ExportService.cs` — `ExportToJsonAsync()`; phantom persons excluded
- `Application/Services/ImportService.cs` — JSON import; `ImportConflictResolution` (Skip/Overwrite/Merge); returns `ImportResultDto`
- `Pages/Export/ExportPage.razor`, `Pages/Import/ImportPage.razor` — download via JS interop, upload via `InputFile`, preview pane, conflict radios, summary report
- "Last exported N days ago" indicator with a stale-backup prompt
- Evaluate the File System Access API for user-controlled file storage; record the finding inline if it changes the approach
- Read and validate the stored `schemaVersion` on import — import is the first code to consume a payload it did not write, and so the first place the stamp has to be checked rather than merely written. Until then the Durability Requirement above is only half met: PR 2 stamps the version, nothing reads it
- GEDCOM deferred to PR 15 — JSON round-trip is what protects the data

E2E: Playwright captures the download, parses it, asserts seeded persons present; a fixture import with Skip produces expected counts.

### PR 6 — PartialDate input: full precision and circa
**Stories:** US-045

Extends `PartialDateInput.razor` in place. Year → Month → Day → Circa; clearing month clears day; converts to/from `PartialDate?` only on `ValueChanged`. Tests cover each precision level, circa, and cascade clearing.

### PR 7 — Biological relationships
**Stories:** US-007 – US-013, US-037, US-039 (bio), US-051

- `BiologicalRelationshipService` — `AddParentAsync` (circular check, two-parent cap, duplicate check), `RemoveAsync`, `ReplaceParentAsync`, `GetSiblingsAsync` classifying full vs half
- Profile sections: bio parents (max 2, "Unknown" phantom slots), bio children (birth-date sorted), siblings
- `Shared/PersonSearchSelect.razor` — debounced search, "Create new person" inline, exclusion list
- `Shared/AddRelationshipDialog.razor` — unified 3-step wizard (kind → person → details incl. subtype, certainty, dates, note); replaces all per-type modals
- Extend export coverage

### PR 8 — Adoptive relationships
**Stories:** US-014 – US-020, US-039 (adoptive)

Same guards as biological, **no upper cap**; adoption date optional ("Date unknown"); dashed-teal chips with "(A)". Extend export coverage.

### PR 9 — Marriage and stepparent relationships
**Stories:** US-021 – US-027, US-038, US-042, US-044

- `IStepparentRelationshipRepository` — **new interface** (does not exist yet) plus its IndexedDB implementation
- `MarriageService` — self-reference check, active-duplicate check, overlap warning, end-after-start validation, `SuggestEndDateFromSpouseDeathAsync` (US-026)
- `StepparentService` — validates the marriage involves a parent of the stepchild
- Profile: "Marriages / Partnerships" and "Stepchildren"; dialog step 3 extended for marriage fields
- Give `TreeStatsService` a cached read invalidated by `TreeDataNotifier`, before a third component subscribes. Each subscriber currently reads all four repositories in full, so every save costs one complete read of the store per listener — free against IndexedDB, four HTTP round trips each once the seam is swapped, which is the pattern the service's own docstring exists to watch for
- Extend `TreeStatsService` to count stepparent links — the relationship total omits them by design until this PR, and starts silently under-reporting the moment they are written
- Extend export coverage

### PR 10 — Full tree view, focus, and phantom nodes
**Stories:** US-028 – US-031, US-054  
**Depends on:** PR 3 and PRs 7–9

- `TreeGraphService` — builds `TreeGraphDto` in **one bulk read**; optional `focusPersonId` and `generationDepth`; nodes carry `IsPhantom`; edges typed and carry certainty
- `Pages/Tree/TreePage.razor` — view-mode toggle, fit-to-screen, zoom, breadcrumbs, mini-map, empty state, focus in URL query string (US-030)
- `Pages/Tree/FamilyTreeDiagram.razor` — five edge styles, legend, hatched phantom nodes
- `Shared/IdentifyPhantomDialog.razor` (US-054)

E2E: expected node count and one edge of each style; node click opens the panel; empty state instead of a blank canvas; phantom identify flow; baseline screenshot for visual regression.

### PR 11 — Pedigree and descendant charts
**Stories:** US-032, US-033

Ahnentafel pedigree (4 generations, unknown slots as nulls) and descendant chart (bio + adoptive with edge type), both left-to-right / top-down per the design.

### PR 12 — Search and browse
**Stories:** US-034, US-035, US-036

`PersonSearchService` (case-insensitive partial match, year-range filters, phantoms excluded), `GlobalSearchBar` in the top bar with 300ms debounce, and `PeopleListPage` extended with sorting, pagination, and filters.

### PR 13 — Undo and certainty UI
**Stories:** US-046, US-055, plus UI surfacing for US-038, US-040, US-042

`UndoService` storing the last relationship mutation, cleared on person edit/delete; `UndoButton` in the top bar; certainty segmented control with lighter strokes and badges for Likely/Speculative.

### PR 14 — Profile photos
**Stories:** US-005  
**Requires:** `docs/decisions/ADR-006-photo-crop.md` spike

Back in scope because IndexedDB stores blobs. Circular crop dialog per the design (drag to reposition, scroll to zoom, zoom slider); JPEG/PNG/WebP; photos stored as blobs keyed by person id; included in export.

### PR 15 — GEDCOM and print-to-PDF
**Stories:** US-050, plus GEDCOM in US-048/049

- GEDCOM 5.5.5 INDI + FAM export and basic import, extending PR 5
- Print stylesheet for profile and tree, driving browser print-to-PDF via `window.print()` — no server-side PDF library needed

### PR 16 — Feedback hardening
**Stories:** determined by feedback

Deliberately reserved and unplanned. The point of shipping early is to learn things not currently known; this is where that gets absorbed.

---

## Dependency Graph

```
PR 1 (RCL + WASM app + GH Pages CI)   ← live URL
  ├─ PR 2 (Application services + IndexedDB + sample data)
  │    └─ PR 4 (Person CRUD + shared components)
  │         ├─ PR 5 (export/import — durability baseline)
  │         ├─ PR 6 (PartialDate input)
  │         ├─ PR 12 (search + browse)
  │         └─ PR 7 (bio relationships + AddRelationshipDialog)
  │              └─ PR 8 (adoptive)
  │                   └─ PR 9 (marriage + stepparent)
  │                        ├─ PR 13 (undo + certainty UI)
  │                        └─ PR 10 (tree) ← also needs PR 3
  │                                  └─ PR 11 (pedigree + descendant)
  └─ PR 3 (viz spike → ADR-005) ── no code dependency, but not run
                                   concurrently: one Pages deployment,
                                   one PR under review at a time

PR 14 (photos, ADR-006) and PR 15 (GEDCOM + print PDF) after PR 5
PR 16 (feedback hardening) after the rest is deployed
```

---

## ADR Ledger

| ADR | Status | Topic |
|-----|--------|-------|
| ADR-001 | Decided | Storage-agnostic domain design |
| ADR-002 | Decided | PartialDate value object |
| ADR-003 | Decided | Separate tables per relationship type |
| ADR-004 | Decided | Client-only Blazor WASM with local-first storage |
| ADR-005 | PR 3 spike | Tree visualization library (DAG, 5 edge styles, phantom nodes, WASM-compatible) |
| ADR-006 | PR 14 spike | Photo crop approach (circular mask, drag + zoom) |

ADR-004 resolved the production-host question outright, so no host ADR is needed. PDF is handled by a print stylesheet rather than a library, so no PDF ADR is needed.

---

## Cross-Cutting Requirements (Every PR)

- Tests in the matching project (`Domain.Tests`, `Application.Tests`, `Storage.Tests`, `UI.Tests`, `E2E.Tests`)
- Every PR shipping UI adds at least one Playwright test against the published site; bUnit alone is not sufficient
- **Every PR is reviewed against the API-Seam Discipline and Durability Requirements above**
- Any PR adding a persisted shape extends export coverage and bumps `schemaVersion` in the same PR
- UI never references domain entities directly; DTOs only
- Phantom persons (`IsPhantom = true`) are excluded from all lists, search results, and exports — visible only in the tree and the Identify dialog
- Certainty defaults to `Confirmed`; Likely and Speculative are visually indicated everywhere relationships appear
- Branch naming: `claude/<short-kebab-description>-<4-char-suffix>` per CLAUDE.md
- All UI matches the design tokens and component conventions in `.design/Family Tree Application.zip`
