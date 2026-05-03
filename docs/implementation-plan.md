# Implementation Plan: FamilyTree — Blazor Server App

## Context

The domain layer is complete: all entities, value objects, EF Core configurations, migrations, repository interfaces, and unit tests. Nothing above the data layer exists yet — no Blazor web project, no application services, no repository implementations, no UI.

This plan reflects the design system documented in `.design/Family Tree Application.zip` (wireframes through mid-fidelity mockups). All UI decisions — layout, components, visual language — must follow that design.

The plan breaks the remaining work into 14 PRs ordered so each is independently mergeable and builds on the last. Three PRs (5, 9, 14) require a short spike ADR before implementation. All work is done by Claude. Tests are non-optional per CLAUDE.md.

---

## Design System Reference

All implementation must follow these conventions from the mid-fidelity mockups:

**Shell**
- Left icon rail (64px): Tree, People, Relate, Import, Settings
- Top bar (52px): logo + tree stats + centered search bar + Undo button + "Add person" (coral primary)

**Color tokens** (defined in `mid-tokens.jsx`, implement as CSS custom properties)
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

## PR Sequence

### PR 1 — Blazor Server project scaffolding and infrastructure wiring
**Stories:** None (foundation)  
**Depends on:** nothing

- Create `src/FamilyTree.Web/` Blazor Server project; add to `FamilyTree.slnx`
- `Program.cs`: `AddRazorComponents().AddInteractiveServerComponents()`, `AddDbContext<FamilyTreeDbContext>` (SQLite `familytree.db`), `db.Database.Migrate()` at startup, register all four repository implementations
- `App.razor`, `Routes.razor`, `_Imports.razor`, `MainLayout.razor` — shell with left icon rail (Tree, People, Relate, Import, Settings) and top bar (logo, stats, search placeholder, Add Person button)
- `wwwroot/css/tokens.css` — CSS custom properties for the full design token set (colors, fonts, spacing, shadows, radii) matching `mid-tokens.jsx`
- `wwwroot/css/app.css` — imports tokens; base resets; button variants (primary/secondary/ghost/destructive/teal); tag/badge styles; field label conventions
- Implement the four EF Core repository concrete classes in `src/FamilyTree.Infrastructure/Persistence/Repositories/`:
  - `PersonRepository.cs`, `BiologicalRelationshipRepository.cs`, `AdoptiveRelationshipRepository.cs`, `MarriageRepository.cs`
- Create `tests/FamilyTree.Infrastructure.Tests/` — smoke tests for each repository using an in-memory SQLite `:memory:` database (Add/GetById round-trip)

---

### PR 2 — Application service layer and CircularReferenceChecker
**Stories:** US-040 delivered at service layer  
**Depends on:** PR 1

- Create `src/FamilyTree.Application/FamilyTree.Application.csproj` (references `FamilyTree.Domain` only)
- `Common/Result.cs` — `Result<T>` with `IsSuccess`, `Value`, `Error`, `IsWarning`; returned by every service method instead of throwing
- `Common/PersonSummaryDto.cs`, `PersonDetailDto.cs` — flat DTOs; UI never touches domain entities directly
- `Services/CircularReferenceChecker.cs` — BFS from `proposedParentId` traversing both bio and adoptive ancestor links; returns `true` if `childId` is reached
- `Services/PersonService.cs` — wraps `IPersonRepository`; enforces required fields, birth-before-death ordering, and duplicate name+date warning (returns `IsWarning = true`, not blocked); handles phantom person creation for US-054
- Create `tests/FamilyTree.Application.Tests/` — tests for `CircularReferenceChecker` (no cycle, direct cycle, indirect cycle, disconnected root) and `PersonService` (required fields, date ordering, duplicate warning, phantom creation)

---

### PR 3 — Person CRUD, people list, and shared UI components
**Stories:** US-001, US-002, US-003, US-004, US-006, US-041, US-044, US-052, US-053  
**Depends on:** PR 2

Key pages/components in `src/FamilyTree.Web/Components/`:
- `Pages/People/PeopleListPage.razor` — card/table of all people; empty state with "Add your first person" (US-052); excludes phantom persons from listing
- `Pages/People/PersonProfilePage.razor` — all person fields; "(née …)" birth surname; "Deceased" badge; age with "~" for approximate; relationship section stubs; also rendered as a 360px right panel when opened from the tree (panel vs full-page controlled by a parameter)
- `Pages/People/AddPersonPage.razor` — full form; required first/last name; duplicate warning inline (non-blocking)
- `Pages/People/EditPersonPage.razor` — pre-populated; `EditContext` "unsaved changes" indicator
- `Shared/QuickAddPersonPopover.razor` — compact popover (name + years + gender); duplicate warning; "Open full form" link; used from top bar and from `AddRelationshipDialog` (US-053)
- `Shared/PersonChip.razor` — `avatar + name + subtitle` chip in four variants: bio (solid border), adoptive (dashed teal), step (dotted), phantom (hatched); clickable; accepts optional delete/edit action
- `Shared/DeleteConfirmModal.razor` — reusable two-step confirmation dialog; for person delete shows affected relationships and offers Replace-with-phantom / Detach-all / Merge options
- `Shared/PartialDateInput.razor` — **stub** supporting year-only for now; extended in PR 4
- `Shared/ToastNotification.razor` — success/error toast, auto-dismiss 3s
- `Shared/ErrorAlert.razor` — inline error/warning alert with consistent styling

Create `tests/FamilyTree.Web.Tests/` (bUnit); test AddPersonPage and QuickAddPersonPopover validation and empty-state rendering.

---

### PR 4 — PartialDate input component: full precision and circa
**Stories:** US-045  
**Depends on:** PR 3

Extends `PartialDateInput.razor` in-place (all callers get it automatically):
- Internal state: `int? year`, `int? month`, `int? day`, `bool isApproximate`
- UI: Year input → Month dropdown (Jan–Dec or blank) → Day dropdown (1–N for chosen month or blank) → Circa checkbox
- Clearing month also clears day; converts to/from `PartialDate?` only on `ValueChanged`
- Tests cover: year-only → `FromYear`, year+month → `FromYearMonth`, full → `FromYearMonthDay`, circa sets `IsApproximate`, clearing month clears day, invalid year/day shows error

---

### PR 5 — Profile photo upload (after ADR-004 spike)
**Stories:** US-005  
**Depends on:** PR 3; requires ADR-004 spike first

Spike ADR-004 evaluates crop library options (Cropper.js interop, BlazorCropperjs NuGet, pure-CSS) and confirms streaming approach for Blazor Server's SignalR file size limits. The crop UI must match the "Position photo" dialog in the design: circular mask, drag-to-reposition, scroll-to-zoom, zoom slider.

- `docs/decisions/ADR-004-photo-crop-library.md`
- `Shared/PhotoUpload.razor` — JPEG/PNG/WebP, max 5 MB; circular crop dialog; saves to `wwwroot/photos/{personId}.jpg`; emits `OnPhotoSaved EventCallback<string>`; "Remove Photo" restores avatar placeholder
- `Application/Services/PhotoService.cs` — validates format/size, saves bytes, handles deletion
- Tests: rejects >5 MB, rejects wrong MIME, saves correct path, delete sets null

---

### PR 6 — Biological relationships
**Stories:** US-007, US-008, US-009, US-010, US-011, US-012, US-013, US-037, US-039 (bio section), US-051  
**Depends on:** PR 2 (CircularReferenceChecker), PR 3 (profile stubs, PersonChip, PersonSearchSelect)

- `Application/Services/BiologicalRelationshipService.cs` — `AddParentAsync`: circular check, two-parent cap, duplicate check; `RemoveAsync`; `ReplaceParentAsync` (US-009); `GetSiblingsAsync` — classifies full vs half siblings
- Fill in profile page bio parents section (max 2, "Unknown" slots using `PersonChip` phantom variant), bio children (sorted by birth date), and siblings sections
- **`Shared/PersonSearchSelect.razor`** — reusable search-and-select dialog; debounced name search; "Create new person" inline option (opens `QuickAddPersonPopover`); returns `Guid` via `EventCallback<Guid>`; parameterized with exclusion list
- **`Shared/AddRelationshipDialog.razor`** — unified 3-step wizard: (1) kind selector (Parent/Child/Sibling/Spouse as radio cards); (2) `PersonSearchSelect`; (3) details (relationship subtype: Bio/Adoptive/Step, certainty segmented, date range, note); replaces all per-type add modals; the kind + subtype selection drives which service is called
- Tests: happy path, circular blocked, two-parent cap, duplicate blocked, replace updates both parties, sibling classification (full vs half), certainty stored correctly

---

### PR 7 — Adoptive relationships
**Stories:** US-014, US-015, US-016, US-017, US-018, US-019, US-020, US-039 (adoptive section alongside bio)  
**Depends on:** PR 6 (reuses `AddRelationshipDialog`, `PersonChip`)

- `Application/Services/AdoptiveRelationshipService.cs` — same guards as bio (circular check covers both bio+adoptive ancestors), no upper cap; `UpdateAdoptionDateAsync`; `ReplaceAdoptiveParentAsync`; certainty handled via `UpdateCertainty`
- Fill in profile page adoptive parents (dashed `PersonChip`) and adoptive children sections; adoption date or "Date unknown"
- Tests mirror biological service tests; adoption date optional; unlimited count; certainty stored correctly

---

### PR 8 — Marriage management and stepparent relationships
**Stories:** US-021, US-022, US-023, US-024, US-025, US-026, US-027, US-038, US-042, US-044  
**Depends on:** PR 6 (reuses `AddRelationshipDialog`)

- `Domain/Repositories/IStepparentRelationshipRepository.cs` — new interface: `AddAsync`, `DeleteAsync`, `GetForPersonAsync`, `GetForMarriageAsync`
- `Infrastructure/Persistence/Repositories/StepparentRelationshipRepository.cs`
- `Application/Services/MarriageService.cs` — self-reference check, active-duplicate check, overlap warning (non-blocking), end-date-after-start validation, `SuggestEndDateFromSpouseDeathAsync` (US-026 auto-fill); certainty handled via `UpdateCertainty`
- `Application/Services/StepparentService.cs` — validates marriage involves a parent of the stepchild; prevents duplicate label
- Fill in profile page: "Marriages / Partnerships" section (spouse `PersonChip`, dates, end reason badge, ongoing highlighted); "Stepchildren" section; "Label as Stepparent" action on child's profile
- `AddRelationshipDialog` step 3 extended for marriage-specific fields: start/end dates, location, end reason dropdown, widowhood auto-fill suggestion, certainty
- Tests: self-reference blocked, active-duplicate blocked, overlap warning, widowhood auto-fill, stepparent validates marriage

---

### PR 9 — Tree visualization spike and ADR-005
**Stories:** None (spike only)  
**Depends on:** can run in parallel with PRs 6–8

Spike evaluates three options against: DAG rendering (persons can have both bio and adoptive parents — this is a directed acyclic graph, not a strict binary tree), pan/zoom, five distinct edge styles (solid/dashed/double/dotted/dashed-faint), node with photo+text at ~172×70px, performance at ~500 nodes, Blazor Server SignalR compatibility.

Options: D3.js via JS interop, GoJS (commercial), Z.Blazor.Diagrams or similar pure-.NET library.

Must also confirm the library can render phantom nodes (distinct visual style) and mini-map.

Output: `docs/decisions/ADR-005-tree-visualization-library.md` with winner, rejection reasons, and critical integration patterns.

---

### PR 10 — Full tree view, focus view, and generation navigation
**Stories:** US-028, US-029, US-030, US-031, US-054 (phantom nodes in tree)  
**Depends on:** PR 9 (ADR-005 decided), PRs 6–8 (relationship data)

- `Application/Services/TreeGraphService.cs` — builds `TreeGraphDto` (nodes + edges); accepts optional `focusPersonId` and `generationDepth`; node includes `IsPhantom` flag; edge types: Biological, Adoptive, Marriage, Stepparent; certainty included on each edge (Speculative/Likely edges rendered differently)
- `Application/Common/TreeGraphDto.cs`, `PersonNodeDto.cs`, `RelationshipEdgeDto.cs`
- `Pages/Tree/TreePage.razor` — hosts diagram; view mode toggle (Explore/Pedigree/Descendants); "Fit to screen" + zoom overlay; breadcrumb trail; mini-map; empty state (US-052); focus stored in URL query string for back/forward (US-030)
- `Pages/Tree/FamilyTreeDiagram.razor` — diagram component; node click dispatches to profile panel; five edge styles; legend; phantom node renders with hatched fill + dashed border + "?" avatar; clicking phantom opens "Identify" dialog (US-054)
- `Shared/IdentifyPhantomDialog.razor` — search for existing person or create new; on confirm, calls service to replace phantom with real person
- Tests: empty → empty graph, correct node/edge count, correct edge types, phantom node included when `IsPhantom = true`, focus+depth=1 returns only immediate family, speculative edge flagged

---

### PR 11 — Pedigree chart and descendant chart
**Stories:** US-032, US-033  
**Depends on:** PR 10

- `Application/Services/PedigreeChartService.cs` — ancestor-only, up to 4 generations, Ahnentafel positional structure; phantom ancestor slots included as null nodes
- `Application/Services/DescendantChartService.cs` — descendant tree down 4 generations, bio+adoptive children with edge type
- `Pages/Tree/PedigreeChartPage.razor` — left-to-right layout; name, birth year, death year per box; click → profile panel
- `Pages/Tree/DescendantChartPage.razor` — top-down layout; relationship type label; click → profile panel
- Tests: generation count, unknown parent slots as null, adoptive children included

---

### PR 12 — Search and browse people
**Stories:** US-034, US-035, US-036  
**Depends on:** PR 3

- `Application/Services/PersonSearchService.cs` — case-insensitive partial match on first+last name; birth/death year range filters; excludes phantom persons (`IsPhantom = false` filter); returns `IReadOnlyList<PersonSummaryDto>` sorted by last name
- `Layout/GlobalSearchBar.razor` — added to top bar in `MainLayout`; debounced 300ms; result dropdown with `PersonChip`, birth–death years; "No match — Add a new person?" links to `QuickAddPersonPopover`
- Extend `PeopleListPage.razor` from PR 3: sortable columns, pagination (20/page), name filter, birth/death year range inputs, result count label
- Tests: partial match, case-insensitive, birth year filter, combined filter, phantom persons excluded, empty tree returns empty list

---

### PR 13 — Undo last relationship change and certainty UI
**Stories:** US-038 (stepparent UI polish), US-040 (circular error messaging in UI), US-042 (overlap warning in UI), US-046, US-055  
**Depends on:** PR 8

- `Application/Services/UndoService.cs` — `AddScoped` (one per Blazor circuit); stores last relationship mutation as `IUndoableAction`; cleared on person edit/delete; supports: add/remove bio link, add/remove adoptive link, add/remove marriage
- `Layout/UndoButton.razor` — added to top bar in `MainLayout`; enabled only when undoable action exists
- Certainty UI: `AddRelationshipDialog` step 3 certainty segmented control (Confirmed/Likely/Speculative); Likely and Speculative edges rendered with a lighter stroke + certainty badge on profile `PersonChip`
- Tests: add then undo removes it, remove then undo re-adds it, second undo unavailable, undo unavailable after person edit, certainty badge shown for non-Confirmed relationships

---

### PR 14 — Export, import, and PDF (after ADR-006 spike)
**Stories:** US-048, US-049, US-050  
**Depends on:** PRs 3–8; requires ADR-006 spike for PDF library

Spike ADR-006 evaluates: QuestPDF (pure .NET), PuppeteerSharp (headless Chrome), iText7. Must verify SVG/diagram rendering alongside structured text.

- `docs/decisions/ADR-006-pdf-generation.md`
- `Application/Services/ExportService.cs` — `ExportToJsonAsync()` and `ExportToGedcomAsync()` (GEDCOM 5.5.5 INDI+FAM records); phantom persons excluded from export
- `Application/Services/ImportService.cs` — JSON and basic GEDCOM import; `ImportConflictResolution` enum (Skip/Overwrite/Merge); returns `ImportResultDto`
- `Application/Services/PdfService.cs` — profile PDF and tree-portion PDF
- `Pages/Export/ExportPage.razor`, `Pages/Import/ImportPage.razor` — two-column layout matching design; file download via JSRuntime; preview pane; conflict radio buttons; progress indicator; summary report
- Extend `TreePage.razor` and `PersonProfilePage.razor` with "Export PDF" button
- Tests: JSON round-trip fidelity, GEDCOM INDI records, partial dates exported correctly, phantom persons excluded, duplicate Skip vs Overwrite behavior

---

## Dependency Graph

```
PR 1 (scaffold + repos + CSS tokens)
  └─ PR 2 (app services + CircularChecker)
       ├─ PR 3 (Person CRUD + PersonChip + QuickAddPopover)
       │    ├─ PR 4 (PartialDate full component)
       │    ├─ PR 5 (photo upload — needs ADR-004 spike)
       │    ├─ PR 6 (bio relationships + AddRelationshipDialog)
       │    │    └─ PR 7 (adoptive relationships)
       │    │         └─ PR 8 (marriage + stepparent)
       │    │              └─ PR 13 (undo + certainty UI polish)
       │    └─ PR 12 (search + browse)
       └─ (feeds PR 6 directly via CircularChecker)

PR 9 (viz spike ADR) ─ run in parallel with PRs 6–8
  └─ PR 10 (full tree + focus + phantom nodes) ─ also needs PRs 6–8
       └─ PR 11 (pedigree + descendant charts)

PR 14 (export/import/PDF) ─ needs PRs 3–8 + ADR-006 spike
```

PRs 4, 5, 9, and 12 have no ordering dependency on each other after their respective prerequisites.

---

## ADRs Still to Write

| ADR | When | Topic |
|-----|------|-------|
| ADR-004 | PR 5 spike | Photo crop library (must support circular mask, drag + zoom) |
| ADR-005 | PR 9 spike | Tree visualization library (DAG, 5 edge styles, phantom nodes, mini-map) |
| ADR-006 | PR 14 spike | PDF generation library |

---

## Cross-Cutting Requirements (Every PR)

- Tests in the matching test project (`Domain.Tests`, `Infrastructure.Tests`, `Application.Tests`, `Web.Tests`)
- All service methods return `Result<T>` — no exceptions for business rules
- UI never references domain entities directly (use DTOs from Application layer)
- Phantom persons (`IsPhantom = true`) excluded from all lists, search results, and exports — only visible in tree and Identify dialog
- Certainty defaults to `Confirmed`; Likely/Speculative must be visually indicated wherever relationships appear
- `IStepparentRelationshipRepository` is added to `FamilyTree.Domain` in PR 8 (does not exist yet)
- Branch naming: `claude/<short-kebab-description>-<4-char-suffix>` per CLAUDE.md
- All UI must match the design tokens and component conventions in `.design/Family Tree Application.zip`
