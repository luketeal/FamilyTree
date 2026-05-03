# Implementation Plan: FamilyTree — Blazor Server App

## Context

The domain layer is complete: all entities, value objects, EF Core configurations, the initial SQLite migration, repository interfaces, and 558 lines of unit tests. Nothing above the data layer exists yet — no Blazor web project, no application services, no repository implementations, no UI.

This plan breaks the remaining work into 14 PRs ordered so each is independently mergeable and builds on the last. The tree visualization and two utility libraries (photo crop, PDF) each require a short spike ADR before their implementation PR.

All work is done by Claude. Tests are non-optional per CLAUDE.md.

---

## PR Sequence

### PR 1 — Blazor Server project scaffolding and infrastructure wiring
**Stories:** None (foundation)  
**Depends on:** nothing

- Create `src/FamilyTree.Web/` Blazor Server project; add to `FamilyTree.slnx`
- `Program.cs`: `AddRazorComponents().AddInteractiveServerComponents()`, `AddDbContext<FamilyTreeDbContext>` (SQLite `familytree.db`), `db.Database.Migrate()` at startup, register all four repository implementations
- `App.razor`, `Routes.razor`, `_Imports.razor`, `MainLayout.razor` — minimal shell with nav placeholders (People | Tree | Search)
- Implement the four EF Core repository concrete classes in `src/FamilyTree.Infrastructure/Persistence/Repositories/`:
  - `PersonRepository.cs` (implements `IPersonRepository`)
  - `BiologicalRelationshipRepository.cs`
  - `AdoptiveRelationshipRepository.cs`
  - `MarriageRepository.cs`
- Create `tests/FamilyTree.Infrastructure.Tests/` — smoke tests for each repository using an in-memory SQLite `:memory:` database (Add/GetById round-trip)

---

### PR 2 — Application service layer and CircularReferenceChecker
**Stories:** US-040 delivered at service layer  
**Depends on:** PR 1

- Create `src/FamilyTree.Application/FamilyTree.Application.csproj` (references `FamilyTree.Domain` only)
- `Common/Result.cs` — `Result<T>` with `IsSuccess`, `Value`, `Error`, `IsWarning`; returned by every service method instead of throwing
- `Common/PersonSummaryDto.cs`, `PersonDetailDto.cs` — flat DTOs; UI never touches domain entities directly
- `Services/CircularReferenceChecker.cs` — BFS from `proposedParentId` traversing both bio and adoptive ancestor links; returns `true` if `childId` is reached
- `Services/PersonService.cs` — wraps `IPersonRepository`; enforces required fields, birth-before-death ordering, and duplicate name+date warning (returns `IsWarning = true`, not blocked)
- Create `tests/FamilyTree.Application.Tests/` — tests for `CircularReferenceChecker` (no cycle, direct cycle, indirect cycle, disconnected root) and `PersonService` (required fields, date ordering, duplicate warning)

---

### PR 3 — Person CRUD, people list, and shared UI components
**Stories:** US-001, US-002, US-003, US-004, US-006, US-041, US-044, US-052  
**Depends on:** PR 2

Key pages/components in `src/FamilyTree.Web/Components/`:
- `Pages/People/PeopleListPage.razor` — card/table of all people; empty state with "Add your first person" (US-052)
- `Pages/People/PersonProfilePage.razor` — all person fields; "(née …)" birth surname; "Deceased" badge; age with "~" for approximate; relationship section stubs ("None recorded")
- `Pages/People/AddPersonPage.razor` — required first/last name; duplicate warning inline (non-blocking)
- `Pages/People/EditPersonPage.razor` — pre-populated; `EditContext` "unsaved changes" indicator
- `Shared/DeleteConfirmModal.razor` — reusable two-step confirmation dialog
- `Shared/PartialDateInput.razor` — **stub** supporting year-only for now; `InputBase<PartialDate?>` subclass; extended in PR 4
- `Shared/ToastNotification.razor` — success/error toast, auto-dismiss 3s

Create `tests/FamilyTree.Web.Tests/` (bUnit); test AddPersonPage validation and empty-state rendering.

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

Spike ADR-004 evaluates crop library options (Cropper.js interop, BlazorCropperjs NuGet, pure-CSS) and confirms streaming approach for Blazor Server's SignalR file size limits.

- `docs/decisions/ADR-004-photo-crop-library.md`
- `Shared/PhotoUpload.razor` — JPEG/PNG/WebP, max 5 MB; JS interop for crop; saves to `wwwroot/photos/{personId}.jpg`; emits `OnPhotoSaved EventCallback<string>`
- `Application/Services/PhotoService.cs` — validates format/size, saves bytes, handles deletion
- Tests: rejects >5 MB, rejects wrong MIME, saves correct path, delete sets null

---

### PR 6 — Biological relationships
**Stories:** US-007, US-008, US-009, US-010, US-011, US-012, US-013, US-037, US-039 (bio section), US-051  
**Depends on:** PR 2 (CircularReferenceChecker), PR 3 (profile stubs)

- `Application/Services/BiologicalRelationshipService.cs` — `AddParentAsync`: circular check, two-parent cap, duplicate check; `RemoveAsync`; `ReplaceParentAsync` (US-009); `GetSiblingsAsync` — classifies full vs half siblings
- Fill in profile page bio parents (max 2, "Unknown" slots), bio children (sorted by birth date), and siblings sections
- **`Shared/PersonSearchSelect.razor`** — reusable search-and-select dialog used by all Add flows; debounced name search; "Create new person" inline option (slide-over modal); returns `Guid` via `EventCallback<Guid>`; parameterized with exclusion list
- `AddBioParentModal.razor` — wrapper around `PersonSearchSelect`; shows birth-date-order warning
- Tests: happy path, circular blocked with error message, two-parent cap, duplicate blocked, replace updates both parties, sibling classification

---

### PR 7 — Adoptive relationships
**Stories:** US-014, US-015, US-016, US-017, US-018, US-019, US-020, US-039 (adoptive section alongside bio)  
**Depends on:** PR 6 (reuses `PersonSearchSelect`, `DeleteConfirmModal`)

- `Application/Services/AdoptiveRelationshipService.cs` — same guards as bio (circular check covers both bio+adoptive ancestors), no upper cap; `UpdateAdoptionDateAsync`; `ReplaceAdoptiveParentAsync`
- Fill in profile page adoptive parents and adoptive children sections; adoption date or "Date unknown"; sorted by adoption date then birth date
- `Shared/AdoptionDateInput.razor` — `PartialDateInput` wrapper with label
- Tests mirror biological service tests; adoption date optional; unlimited count

---

### PR 8 — Marriage management and stepparent relationships
**Stories:** US-021, US-022, US-023, US-024, US-025, US-026, US-027, US-038, US-042, US-044  
**Depends on:** PR 6 (reuses `PersonSearchSelect`), PR 7 (stepparent tied to a marriage)

- `Domain/Repositories/IStepparentRelationshipRepository.cs` — new interface: `AddAsync`, `DeleteAsync`, `GetForPersonAsync`, `GetForMarriageAsync`
- `Infrastructure/Persistence/Repositories/StepparentRelationshipRepository.cs`
- `Application/Services/MarriageService.cs` — self-reference check, active-duplicate check, overlap warning (non-blocking), end-date-after-start validation, `SuggestEndDateFromSpouseDeathAsync` (US-026 auto-fill)
- `Application/Services/StepparentService.cs` — validates marriage involves a parent of the stepchild; prevents duplicate label
- Fill in profile page: "Marriages / Partnerships" section (spouse link, dates, end reason badge, ongoing highlighted), "Stepchildren" section, "Label as Stepparent" action
- `Shared/AddMarriageModal.razor` — `PersonSearchSelect` for spouse, `PartialDateInput` for dates, end reason dropdown, location field, widowhood auto-fill
- Tests: self-reference blocked, active-duplicate blocked, overlap warning returned, widowhood auto-fill, stepparent validates marriage, duplicate stepparent label blocked

---

### PR 9 — Tree visualization spike and ADR-005
**Stories:** None (spike only)  
**Depends on:** can run in parallel with PRs 6–8

Spike evaluates three options against: DAG rendering (not just binary tree — some people have both bio and adoptive parents), pan/zoom, distinguishable edge styles, node with photo+text, performance at ~500 nodes, Blazor Server SignalR compatibility.

Options: D3.js via JS interop, GoJS (commercial), Z.Blazor.Diagrams or similar pure-.NET library.

Output: `docs/decisions/ADR-005-tree-visualization-library.md` with winner, rejection reasons, and critical integration patterns.

---

### PR 10 — Full tree view, focus view, and generation navigation
**Stories:** US-028, US-029, US-030, US-031  
**Depends on:** PR 9 (ADR-005 decided), PRs 6–8 (relationship data)

- `Application/Services/TreeGraphService.cs` — builds `TreeGraphDto` (nodes + edges); accepts optional `focusPersonId` and `generationDepth`; edge types: Biological, Adoptive, Marriage, Stepparent
- `Application/Common/TreeGraphDto.cs`, `PersonNodeDto.cs`, `RelationshipEdgeDto.cs`
- `Pages/Tree/TreePage.razor` — hosts diagram; "Fit to screen" button; breadcrumb trail; empty state (US-052); focus stored in URL query string (`/tree?focus=<guid>`) for browser back/forward (US-030)
- `Pages/Tree/FamilyTreeDiagram.razor` — diagram component; node click dispatches to `TreePage`; edge styles per US-031; legend overlay
- `wwwroot/js/tree-interop.js` — JS bridge if using D3/JS library
- Tests: empty → empty graph, correct node/edge count, correct edge types, focus+depth=1 returns only immediate family

---

### PR 11 — Pedigree chart and descendant chart
**Stories:** US-032, US-033  
**Depends on:** PR 10

- `Application/Services/PedigreeChartService.cs` — ancestor-only, up to 4 generations, Ahnentafel positional structure
- `Application/Services/DescendantChartService.cs` — descendant tree down 4 generations, includes bio+adoptive children with edge type
- `Pages/Tree/PedigreeChartPage.razor` — left-to-right layout; name, birth year, death year per box; click → profile
- `Pages/Tree/DescendantChartPage.razor` — top-down layout; relationship type label on edges
- Tests: generation count correct, unknown parent slots as null nodes, adoptive children included in descendant chart

---

### PR 12 — Search and browse people
**Stories:** US-034, US-035, US-036  
**Depends on:** PR 3

- `Application/Services/PersonSearchService.cs` — case-insensitive partial match on first+last name; birth/death year range filters against flat `BirthDate_Year`/`DeathDate_Year` columns; returns `IReadOnlyList<PersonSummaryDto>` sorted by last name
- `Layout/GlobalSearchBar.razor` — added to `MainLayout`; debounced 300ms; result dropdown with photo thumbnail, name, birth–death years; "No match — Add a new person?" (US-034)
- Extend `PeopleListPage.razor` from PR 3: sortable columns, pagination (20/page), name filter, birth/death year range inputs, result count label
- Tests: partial match, case-insensitive, birth year filter, combined filter, empty tree returns empty list

---

### PR 13 — Undo last relationship change and edge-case UI polish
**Stories:** US-038 (stepparent UI polish), US-040 (circular error in UI), US-042 (overlap warning in UI), US-046  
**Depends on:** PR 8

- `Application/Services/UndoService.cs` — `AddScoped` (one per Blazor circuit); stores last relationship mutation as `IUndoableAction`; cleared on person edit/delete; supports: add/remove bio link, add/remove adoptive link, add/remove marriage
- `Layout/UndoButton.razor` — added to `MainLayout`; enabled only when undoable action exists; calls `UndoService.UndoAsync()` and refreshes
- `Shared/ErrorAlert.razor` — consistent inline error/warning alert used across all forms
- Tests: add then undo removes it, remove then undo re-adds it, second undo unavailable, undo unavailable after person edit

---

### PR 14 — Export, import, and PDF (after ADR-006 spike)
**Stories:** US-048, US-049, US-050  
**Depends on:** PRs 3–8; requires ADR-006 spike for PDF library

Spike ADR-006 evaluates: QuestPDF (pure .NET), PuppeteerSharp (headless Chrome), iText7. Must verify SVG/diagram rendering alongside structured text.

- `docs/decisions/ADR-006-pdf-generation.md`
- `Application/Services/ExportService.cs` — `ExportToJsonAsync()` and `ExportToGedcomAsync()` (GEDCOM 5.5.5 INDI+FAM records)
- `Application/Services/ImportService.cs` — JSON and basic GEDCOM import; `ImportConflictResolution` enum (Skip/Overwrite/Merge); returns `ImportResultDto` with counts and errors
- `Application/Services/PdfService.cs` — profile PDF and tree-portion PDF
- `Pages/Export/ExportPage.razor`, `Pages/Import/ImportPage.razor` — file download via JSRuntime, preview pane, conflict radio buttons, progress indicator, summary report
- Extend `TreePage.razor` and `PersonProfilePage.razor` with "Export PDF" button
- Tests: JSON round-trip fidelity, GEDCOM INDI records, partial dates exported correctly, duplicate Skip vs Overwrite behavior

---

## Dependency Graph

```
PR 1 (scaffold + repos)
  └─ PR 2 (app services + CircularChecker)
       ├─ PR 3 (Person CRUD + shared components)
       │    ├─ PR 4 (PartialDate full component)
       │    ├─ PR 5 (photo upload — needs ADR-004 spike)
       │    ├─ PR 6 (bio relationships)
       │    │    └─ PR 7 (adoptive relationships)
       │    │         └─ PR 8 (marriage + stepparent)
       │    │              └─ PR 13 (undo + edge-case UI polish)
       │    └─ PR 12 (search + browse)
       └─ (feeds PR 6 directly via CircularChecker)

PR 9 (viz spike ADR) ─ run in parallel with PRs 6–8
  └─ PR 10 (full tree + focus) ─ also needs PRs 6–8
       └─ PR 11 (pedigree + descendant charts)

PR 14 (export/import/PDF) ─ needs PRs 3–8 + ADR-006 spike
```

PRs 4, 5, 9, and 12 have no ordering dependency on each other after their respective prerequisites — they can proceed in parallel.

---

## ADRs Still to Write

| ADR | When | Topic |
|-----|------|-------|
| ADR-004 | PR 5 spike | Photo crop library |
| ADR-005 | PR 9 spike | Tree visualization library |
| ADR-006 | PR 14 spike | PDF generation library |

---

## Cross-Cutting Requirements (Every PR)

- Tests in the matching test project (`Domain.Tests`, `Infrastructure.Tests`, `Application.Tests`, `Web.Tests`)
- All service methods return `Result<T>` — no exceptions for business rules
- UI never references domain entities directly (use DTOs from Application layer)
- `IStepparentRelationshipRepository` is added to `FamilyTree.Domain` in PR 8 (does not exist yet)
- Branch naming: `claude/<short-kebab-description>-<4-char-suffix>` per CLAUDE.md
