# Implementation Plan: FamilyTree — Demo-First on Blazor WebAssembly

## Context

This plan replaces the earlier bottom-up sequence. Per **ADR-004**, delivery is inverted: a deployable Blazor WebAssembly demo backed by browser localStorage ships first, so real user feedback shapes the interaction design while the persistent backend is still being built.

The domain layer is complete — all entities, the `PartialDate` value object, enums, EF Core configurations, migrations, repository interfaces, and unit tests. The demo reuses it directly. Nothing is mocked at the domain level.

**The production host is deliberately undecided** (ADR-004). All UI lives in a host-agnostic Razor Class Library so that either Blazor Server or WebAssembly-plus-API can be chosen later without rewriting components.

All UI decisions follow the design system in `.design/Family Tree Application.zip` (wireframes through mid-fidelity mockups).

---

## Project Structure

```
src/
  FamilyTree.Domain/          existing, unchanged — entities, PartialDate, repository interfaces
  FamilyTree.Application/     new — services, DTOs, Result<T>; host-agnostic, no infrastructure refs
  FamilyTree.UI/              new — Razor Class Library; ALL components, pages, CSS. Host-agnostic.
  FamilyTree.Demo/            new — Blazor WASM host; localStorage repositories; deploys to GH Pages
  FamilyTree.Infrastructure/  existing — EF Core + SQLite; off the demo critical path
  FamilyTree.Web/             from PR #65 — Blazor Server host; parked but kept compiling
tests/
  FamilyTree.Domain.Tests/          existing
  FamilyTree.Application.Tests/     new — service and business-rule tests
  FamilyTree.UI.Tests/              new — bUnit component tests; host-agnostic
  FamilyTree.Infrastructure.Tests/  from PR #65 — EF repository tests
  FamilyTree.Demo.E2E.Tests/        new — Playwright against the published static demo
```

`FamilyTree.Web` and `FamilyTree.Infrastructure` stay in the solution and stay green, but no demo-phase PR depends on them. They are the on-ramp for the Blazor Server path if it wins.

---

## Host-Portability Rules (binding while the host is undecided)

Every component in `FamilyTree.UI` must satisfy all of these. A violation typically works fine in the demo and breaks under Blazor Server, so these are enforced in review, not discovered later.

- JS interop via `IJSRuntime` **async only** — never `IJSInProcessRuntime` / `IJSInProcessObjectReference`
- No `HttpContext`, `IHttpContextAccessor`, or server-only DI
- No direct `System.IO` access — upload via `InputFile`, download via JS interop
- No synchronous blocking: no `.Result`, no `.Wait()`
- No multi-threading assumptions — WASM is single-threaded; no `Task.Run`, no `Thread.Sleep`
- Data access only through `FamilyTree.Domain` repository interfaces; components never construct a store
- `InvariantGlobalization` stays **off** — this app formats dates heavily and needs ICU

---

## Design System Reference

Unchanged from the previous plan. All implementation follows these conventions from the mid-fidelity mockups:

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

## Story Scope in the Demo

**51 of 53 stories are reachable in the demo.** Two are deferred to the backend phase by ADR-004:

| Story | Why deferred |
|-------|--------------|
| US-005 — Profile photo upload | localStorage caps near 5 MB per origin; base64 images do not fit |
| US-050 — Print / generate a PDF | Candidate libraries need native binaries or headless Chrome; server-side work |

Export and import (US-048, US-049) **stay in the demo** — JSON and GEDCOM are pure text generation and work entirely client-side. Testers exporting real trees is among the most valuable feedback available.

---

## PR Sequence

### Phase 1 — Demo foundation

#### PR 1 — Restructure into shared RCL, WASM demo host, and GitHub Pages deployment
**Stories:** None (foundation)  
**Depends on:** merging PR #65 first

- Merge PR #65 as-is. Its EF repository implementations and `Infrastructure.Tests` are needed under either host; its shell layout and CSS tokens are migrated by this PR.
- Create `src/FamilyTree.UI/` (Razor Class Library). Move from `FamilyTree.Web`: `wwwroot/css/tokens.css`, `wwwroot/css/app.css`, `MainLayout.razor`, `IconRail.razor`, `TopBar.razor` and their `.razor.css` files. `FamilyTree.Web` then references the RCL and keeps compiling as a parked host.
- Create `src/FamilyTree.Demo/` — Blazor WASM host referencing `FamilyTree.UI`; `Program.cs` registers DI; `index.html` with the design system's fonts served as static assets.
- Add both projects plus the new test projects to `FamilyTree.slnx`.
- `.github/workflows/deploy-demo.yml` — the repository's first CI workflow. On push to `main`: `dotnet publish -c Release`, rewrite `<base href>` to `/FamilyTree/`, copy `index.html` to `404.html`, write `.nojekyll`, deploy via `actions/deploy-pages`. Also runs `dotnet test` on every PR.
- Create `tests/FamilyTree.Demo.E2E.Tests/` — xUnit + `Microsoft.Playwright` against the published static output (no host fixture needed; simpler than the Blazor Server equivalent). One boot smoke test asserting the left rail and top bar render.
- Create `tests/FamilyTree.UI.Tests/` — bUnit project, scaffold plus one layout render test.

**Outcome: a live demo URL exists.** Shell only, no data yet.

#### PR 2 — Application service layer and browser-backed storage
**Stories:** US-040 at service layer  
**Depends on:** PR 1

- `src/FamilyTree.Application/` (references `FamilyTree.Domain` only)
  - `Common/Result.cs` — `IsSuccess`, `Value`, `Error`, `IsWarning`; returned by every service method instead of throwing
  - `Common/PersonSummaryDto.cs`, `PersonDetailDto.cs` — flat DTOs; UI never touches domain entities
  - `Services/CircularReferenceChecker.cs` — BFS from `proposedParentId` across bio **and** adoptive ancestor links; returns `true` if `childId` is reached
  - `Services/PersonService.cs` — required fields, birth-before-death ordering, duplicate name+date warning (`IsWarning`, non-blocking), phantom person creation (US-054)
- `src/FamilyTree.Demo/Storage/` — localStorage implementations of all four `FamilyTree.Domain` repository interfaces.
  - **Persist flat records, not the object graph.** `Person` has a private constructor, private setters, and bidirectional navigation collections; `System.Text.Json` can neither round-trip it nor escape the circular references. Define `PersonRecord`, `BiologicalLinkRecord`, `AdoptiveLinkRecord`, `MarriageRecord`, `StepparentRecord` mirroring the EF table shapes, and rehydrate through domain factory methods. Keep these in sync with the `Infrastructure` EF configurations.
  - `PartialDateJsonConverter` — the value object needs explicit conversion.
  - Stamp a `schemaVersion` on the stored payload so a shape change can detect and reset stale tester data rather than crashing.
- Seeded sample family fixture, deliberately exercising the hard cases: half-siblings via a shared parent, an adoption, a remarriage after widowhood, a phantom grandparent, and one speculative-certainty link. Plus a "Reset to sample data" action in Settings.
- Tests: `Application.Tests` (circular checker — no cycle, direct, indirect, disconnected root; `PersonService` — required fields, date ordering, duplicate warning, phantom creation) and storage round-trip tests (every entity type survives save/load; `PartialDate` precision and `IsApproximate` preserved).

#### PR 3 — Tree visualization spike and ADR-005
**Stories:** None (spike)  
**Depends on:** PR 1; runs in parallel with PRs 4–8

Moved from PR 9 in the old plan. This is the project's largest unknown and the tree is the centrepiece of any feedback session, so it resolves before the tree is built rather than after everything else.

Evaluate against: DAG rendering (a person may have both biological and adoptive parents — this is a directed acyclic graph, not a strict tree), pan/zoom, five distinct edge styles, nodes ~172×70px with photo and text, performance at ~500 nodes, phantom node styling, mini-map.

**Criteria changed by ADR-004:** the library must work under **WebAssembly**, and ideally under Blazor Server too, since the host is undecided. D3 via JS interop satisfies both; some pure-.NET diagram libraries assume a Server circuit. Payload size now counts against the WASM download budget.

Output: `docs/decisions/ADR-005-tree-visualization-library.md`.

### Phase 2 — User-facing functionality

#### PR 4 — Person CRUD, people list, and shared components
**Stories:** US-001, US-002, US-003, US-004, US-006, US-041, US-044, US-052, US-053  
**Depends on:** PR 2

In `src/FamilyTree.UI/Components/`:
- `Pages/People/PeopleListPage.razor` — all people; empty state with "Add your first person" (US-052); excludes phantom persons
- `Pages/People/PersonProfilePage.razor` — all fields; "(née …)" birth surname; "Deceased" badge; age with "~" when approximate; relationship section stubs; also renders as a 360px right panel when opened from the tree (controlled by a parameter)
- `Pages/People/AddPersonPage.razor` — required first/last name; inline non-blocking duplicate warning
- `Pages/People/EditPersonPage.razor` — pre-populated; `EditContext` unsaved-changes indicator
- `Shared/QuickAddPersonPopover.razor` — compact popover (name, years, gender); duplicate warning; "Open full form" link (US-053)
- `Shared/PersonChip.razor` — four variants: bio (solid), adoptive (dashed teal), step (dotted), phantom (hatched)
- `Shared/DeleteConfirmModal.razor` — two-step confirmation; for person delete shows affected relationships and offers Replace-with-phantom / Detach-all / Merge
- `Shared/PartialDateInput.razor` — **stub**, year-only for now; extended in PR 5
- `Shared/ToastNotification.razor`, `Shared/ErrorAlert.razor`

bUnit (`UI.Tests`): AddPersonPage and QuickAddPersonPopover validation, empty-state rendering, `PersonChip` variant classes.

E2E: empty DB shows the empty state and its button navigates to the add form; submitting with no first name shows the inline error and stays on the form; a valid person persists **and survives a page reload** (localStorage round-trip); quick-add popover opens, accepts a person, closes.

#### PR 5 — PartialDate input: full precision and circa
**Stories:** US-045  
**Depends on:** PR 4

Extends `PartialDateInput.razor` in place, so all callers inherit it.
- Internal state: `int? year`, `int? month`, `int? day`, `bool isApproximate`
- Year input → Month dropdown → Day dropdown (1–N for the chosen month) → Circa checkbox
- Clearing month clears day; converts to/from `PartialDate?` only on `ValueChanged`
- Tests: year-only → `FromYear`, year+month → `FromYearMonth`, full → `FromYearMonthDay`, circa sets `IsApproximate`, clearing month clears day, invalid year/day shows error

E2E: year-only birth date saves and the profile shows just the year; full date plus circa renders with the "~" prefix; selecting then clearing a month also clears the day.

#### PR 6 — Biological relationships
**Stories:** US-007, US-008, US-009, US-010, US-011, US-012, US-013, US-037, US-039 (bio section), US-051  
**Depends on:** PR 2 (circular checker), PR 4 (profile stubs, `PersonChip`)

- `Application/Services/BiologicalRelationshipService.cs` — `AddParentAsync` (circular check, two-parent cap, duplicate check), `RemoveAsync`, `ReplaceParentAsync` (US-009), `GetSiblingsAsync` classifying full vs half
- Fill in profile sections: bio parents (max 2, "Unknown" slots as phantom `PersonChip`), bio children (sorted by birth date), siblings
- `Shared/PersonSearchSelect.razor` — debounced name search; "Create new person" inline (opens `QuickAddPersonPopover`); returns `Guid`; accepts an exclusion list
- `Shared/AddRelationshipDialog.razor` — unified 3-step wizard: kind (Parent/Child/Sibling/Spouse as radio cards) → person → details (subtype Bio/Adoptive/Step, certainty segmented control, date range, note). Replaces all per-type modals; kind + subtype drive which service is called.
- Tests: happy path, circular blocked, two-parent cap, duplicate blocked, replace updates both parties, sibling classification, certainty stored

E2E: walk the dialog end to end and confirm the new parent appears as a chip; adding a shared parent to two people makes each appear in the other's siblings; a cycle-creating parent surfaces the error and blocks save.

#### PR 7 — Adoptive relationships
**Stories:** US-014, US-015, US-016, US-017, US-018, US-019, US-020, US-039 (adoptive alongside bio)  
**Depends on:** PR 6

- `Application/Services/AdoptiveRelationshipService.cs` — same guards as bio (the circular check already spans both link types), **no upper cap**; `UpdateAdoptionDateAsync`; `ReplaceAdoptiveParentAsync`
- Profile: adoptive parents (dashed teal chips) and adoptive children; adoption date or "Date unknown"
- Tests mirror the biological service; adoption date optional; unlimited count; certainty stored

E2E: an adoptive parent renders as a dashed-teal chip with the "(A)" label; a third adoptive parent is accepted and all three render; omitting the date renders "Date unknown".

#### PR 8 — Marriage and stepparent relationships
**Stories:** US-021, US-022, US-023, US-024, US-025, US-026, US-027, US-038, US-042, US-044  
**Depends on:** PR 6

- `Domain/Repositories/IStepparentRelationshipRepository.cs` — **new interface** (does not exist yet): `AddAsync`, `DeleteAsync`, `GetForPersonAsync`, `GetForMarriageAsync`. Implemented in `Demo/Storage/` now and in `Infrastructure/` during Phase 3.
- `Application/Services/MarriageService.cs` — self-reference check, active-duplicate check, overlap warning (non-blocking), end-after-start validation, `SuggestEndDateFromSpouseDeathAsync` (US-026)
- `Application/Services/StepparentService.cs` — validates the marriage involves a parent of the stepchild; prevents duplicate labels
- Profile: "Marriages / Partnerships" (spouse chip, dates, end-reason badge, ongoing highlighted) and "Stepchildren"; "Label as Stepparent" action on a child's profile
- `AddRelationshipDialog` step 3 extended: start/end dates, location, end reason, widowhood auto-fill suggestion
- Tests: self-reference blocked, active duplicate blocked, overlap warning, widowhood auto-fill, stepparent marriage validation

E2E: a marriage appears on both spouses' profiles with the start date; ending a marriage by death offers and accepts the widowhood suggestion; "Label as Stepparent" puts the child in the stepparent's Stepchildren section.

#### PR 9 — Full tree view, focus, and phantom nodes
**Stories:** US-028, US-029, US-030, US-031, US-054  
**Depends on:** PR 3 (ADR-005 decided) and PRs 6–8

- `Application/Services/TreeGraphService.cs` — builds `TreeGraphDto` (nodes + edges); optional `focusPersonId` and `generationDepth`; nodes carry `IsPhantom`; edges typed Biological / Adoptive / Marriage / Stepparent and carry certainty
- `Application/Common/TreeGraphDto.cs`, `PersonNodeDto.cs`, `RelationshipEdgeDto.cs`
- `Pages/Tree/TreePage.razor` — diagram host; view-mode toggle (Explore/Pedigree/Descendants); fit-to-screen and zoom overlay; breadcrumb trail; mini-map; empty state (US-052); focus in the URL query string for back/forward (US-030)
- `Pages/Tree/FamilyTreeDiagram.razor` — five edge styles; legend; phantom nodes hatched with dashed border and "?" avatar; node click opens the profile panel
- `Shared/IdentifyPhantomDialog.razor` — search existing or create new; replaces the phantom (US-054)
- Tests: empty → empty graph; node/edge counts; edge types; phantom included; focus with depth 1 returns immediate family only; speculative edge flagged

E2E: seeded tree renders the expected node count and at least one edge of each style; node click opens the panel; empty DB shows the empty state rather than a blank canvas; identifying a phantom re-renders it without phantom styling; baseline screenshot captured for visual regression.

#### PR 10 — Pedigree and descendant charts
**Stories:** US-032, US-033  
**Depends on:** PR 9

- `Application/Services/PedigreeChartService.cs` — ancestors only, 4 generations, Ahnentafel positions, unknown slots as nulls
- `Application/Services/DescendantChartService.cs` — descendants 4 generations, bio + adoptive children with edge type
- `Pages/Tree/PedigreeChartPage.razor` (left-to-right) and `Pages/Tree/DescendantChartPage.razor` (top-down); name, birth year, death year per box; click opens the profile panel
- Tests: generation counts, unknown parent slots null, adoptive children included

E2E: two known parents and two unknown grandparents render empty boxes in the correct Ahnentafel positions; a descendant chart shows one bio and one adoptive child with their edge labels; clicking a box opens the panel.

#### PR 11 — Search and browse
**Stories:** US-034, US-035, US-036  
**Depends on:** PR 4

- `Application/Services/PersonSearchService.cs` — case-insensitive partial match on first + last name; birth/death year range filters; excludes phantom persons; sorted by last name
- `Layout/GlobalSearchBar.razor` — into the top bar; 300ms debounce; dropdown of `PersonChip` results with years; "No match — Add a new person?" opens `QuickAddPersonPopover` pre-filled
- Extend `PeopleListPage` — sortable columns, pagination (20/page), name filter, year-range inputs, result count
- Tests: partial match, case-insensitivity, birth-year filter, combined filters, phantoms excluded, empty tree

E2E: partial name shows matching results after debounce; no-match link opens the popover pre-filled; a birth-year range narrows the list and updates the count.

#### PR 12 — Undo and certainty UI
**Stories:** US-046, US-055, plus UI surfacing for US-038, US-040, US-042  
**Depends on:** PR 8

- `Application/Services/UndoService.cs` — scoped; stores the last relationship mutation as `IUndoableAction`; cleared on person edit/delete; covers add/remove for bio, adoptive, and marriage
- `Layout/UndoButton.razor` — into the top bar; enabled only when an undoable action exists
- Certainty UI: segmented control in dialog step 3 (Confirmed/Likely/Speculative); Likely and Speculative render with lighter strokes and a certainty badge on chips
- Tests: add-then-undo removes; remove-then-undo restores; second undo unavailable; undo cleared by person edit; badge shown for non-Confirmed

E2E: adding a parent enables Undo, clicking it removes the chip and disables the button; editing a person disables Undo even after a relationship change; a Speculative relationship renders the badge and a lighter tree edge.

#### PR 13 — Export and import (client-side)
**Stories:** US-048, US-049  
**Depends on:** PRs 4–8

- `Application/Services/ExportService.cs` — `ExportToJsonAsync()` and `ExportToGedcomAsync()` (GEDCOM 5.5.5 INDI + FAM); phantom persons excluded
- `Application/Services/ImportService.cs` — JSON and basic GEDCOM; `ImportConflictResolution` enum (Skip/Overwrite/Merge); returns `ImportResultDto`
- `Pages/Export/ExportPage.razor`, `Pages/Import/ImportPage.razor` — two-column layout per the design; download via JS interop; upload via `InputFile`; preview pane; conflict radios; progress; summary report
- Tests: JSON round-trip fidelity, GEDCOM INDI records, partial dates preserved, phantoms excluded, Skip vs Overwrite behaviour

E2E: Playwright captures the JSON download, parses it, asserts seeded persons present; a fixture import with Skip produces the expected summary counts and updates the People list.

Doubles as the tester feedback channel — testers export and send their file.

#### PR 14 — Feedback hardening
**Stories:** determined by feedback  
**Depends on:** PRs 4–13 deployed and exercised by testers

Deliberately reserved and left unplanned. The point of the demo is to learn things not currently known; this is where that gets absorbed before backend work locks behaviour in.

### Phase 3 — Backend convergence

Not blocking the demo. Runs once feedback has settled the interaction design.

#### PR 15 — EF repositories and infrastructure parity
Bring `FamilyTree.Infrastructure` up to the interface set the demo proved out: the four repository implementations from PR #65 plus `StepparentRelationshipRepository` from PR 8, and a migration for any schema change the demo surfaced. Verify the flat browser records and the EF configurations still describe the same shapes.

#### PR 16 — Host decision and ADR-008
Decide Blazor Server versus WebAssembly-plus-API, using what the demo taught: real tree sizes, whether layout needs server-side computation, whether offline use matters, measured first-load payload on testers' actual devices. Record as `ADR-008-production-host.md`. Wire the winning host to `FamilyTree.UI` — which requires no component changes if the portability rules held.

#### PR 17 — Deferred features
US-005 (photo upload, needs `ADR-006-photo-storage-and-crop.md`) and US-050 (PDF, needs `ADR-007-pdf-generation.md`). Both depend on the host decision.

---

## Dependency Graph

```
PR #65 (merge: EF repos + infra tests)
  └─ PR 1 (RCL split + WASM demo host + GH Pages CI)   ← demo URL live
       ├─ PR 2 (Application services + localStorage store + sample data)
       │    ├─ PR 4 (Person CRUD + PersonChip + QuickAdd)
       │    │    ├─ PR 5 (PartialDate input)
       │    │    ├─ PR 11 (search + browse)
       │    │    └─ PR 6 (bio relationships + AddRelationshipDialog)
       │    │         └─ PR 7 (adoptive)
       │    │              └─ PR 8 (marriage + stepparent)
       │    │                   ├─ PR 12 (undo + certainty UI)
       │    │                   └─ PR 13 (export/import)
       │    └─ (CircularReferenceChecker feeds PR 6)
       └─ PR 3 (viz spike → ADR-005) ── parallel with PRs 4–8
            └─ PR 9 (tree + focus + phantom)  ← also needs PRs 6–8
                 └─ PR 10 (pedigree + descendant)

PR 14 (feedback hardening) ← after 4–13 are deployed

Phase 3: PR 15 (EF parity) → PR 16 (host decision, ADR-008) → PR 17 (photos, PDF)
```

---

## ADR Ledger

ADR-004 took the number previously reserved for the photo-crop spike, so the pending spikes are renumbered.

| ADR | Status | Topic |
|-----|--------|-------|
| ADR-001 | Decided | Storage-agnostic domain design |
| ADR-002 | Decided | PartialDate value object |
| ADR-003 | Decided | Separate tables per relationship type |
| ADR-004 | Decided | Demo-first delivery on Blazor WASM with a shared UI library |
| ADR-005 | PR 3 spike | Tree visualization library (DAG, 5 edge styles, phantom nodes, mini-map, **WASM-compatible**) |
| ADR-006 | PR 17 | Photo storage and crop |
| ADR-007 | PR 17 | PDF generation |
| ADR-008 | PR 16 | Production host: Blazor Server vs WASM + API |

---

## Cross-Cutting Requirements (Every PR)

- Tests in the matching project (`Domain.Tests`, `Application.Tests`, `UI.Tests`, `Infrastructure.Tests`, `Demo.E2E.Tests`)
- Every PR shipping UI adds at least one Playwright test against the published demo; bUnit alone is not sufficient for UI work
- **Every PR shipping UI is reviewed against the Host-Portability Rules above**
- All service methods return `Result<T>` — no exceptions for business rules
- UI never references domain entities directly; DTOs only
- Any change to a persisted shape updates **both** the browser flat records and the EF configurations, and bumps `schemaVersion`
- Phantom persons (`IsPhantom = true`) are excluded from all lists, search results, and exports — visible only in the tree and the Identify dialog
- Certainty defaults to `Confirmed`; Likely and Speculative are visually indicated everywhere relationships appear
- Branch naming: `claude/<short-kebab-description>-<4-char-suffix>` per CLAUDE.md
- All UI matches the design tokens and component conventions in `.design/Family Tree Application.zip`
