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
- The UI surfaces "last exported N days ago", and prompts whenever the tree has changed since that export. The trigger is unsaved work rather than elapsed time: a timer nags somebody who exported and then did nothing, and stays silent for somebody who exported and then entered fifty people
- A `schemaVersion` is stamped on the stored payload so a shape change detects and migrates (or safely resets) rather than crashing
- The File System Access API was evaluated in PR 5 and rejected: it is Chromium-only, so a picker-based backup would silently protect Firefox and Safari users less well than the anchor download that has to exist anyway (ADR-007)

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

**Done.** Four candidates were prototyped and measured against the same 500-person graph; the prototype and raw results are in `spikes/tree-visualization/`. The decision is to write the layout and the SVG renderer ourselves and take no visualisation library — a family tree turned out not to be a generic DAG drawing problem, and the family-aware layout is what keeps panning at 60 fps rather than merely what makes it tidier. Three findings change PR 10's shape:

- Fit-to-screen on a full 500-person tree is unreadable (~40,400 × 1,030px, about 3.5% scale in a 1440px viewport). The focused view (US-029) and `generationDepth` are the primary experience; whole-tree fit is a mini-map orientation aid, not a way to read the tree.
- Couple adjacency is unsolved for remarriage — a person in two couples cannot sit next to both, and the prototype draws a long marriage edge across the diagram as a result. PR 10 needs a deliberate ordering rule.
- Blazor should pass `TreeGraphDto` straight across `IJSRuntime`: 64 ms once at 500 nodes, and interaction never re-enters .NET.

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
- "Last exported N days ago" indicator, and a shell-wide prompt whenever the tree has changed since the last export
- File System Access API evaluated and rejected — Chromium-only, so a picker-based backup would silently protect Firefox and Safari users less well. See `docs/decisions/ADR-007-export-file-delivery.md`
- Read and validate the stored `schemaVersion` on import — import is the first code to consume a payload it did not write, and so the first place the stamp has to be checked rather than merely written. Until then the Durability Requirement above is only half met: PR 2 stamps the version, nothing reads it
- GEDCOM deferred to PR 15 — JSON round-trip is what protects the data

E2E: Playwright captures the download, parses it, asserts seeded persons present; a fixture import with Skip produces expected counts.

### PR 6 — PartialDate input: full precision and circa
**Stories:** US-045

Extends `PartialDateInput.razor` in place. Year → Month → Day → Circa; clearing month clears day; converts to/from `PartialDate?` only on `ValueChanged`. Tests cover each precision level, circa, and cascade clearing.

- Change the seed contract from `PartialDate?` to raw text while reshaping this control. `PartialDate` cannot hold a year outside 1..9999, so a value the user typed but which the type cannot represent is unrepresentable in the seed path *by construction* — the carry-over from quick add currently reports that it dropped such a year rather than preserving it. Raw text in, parsed on emit, is the same fix that resolved the quick-add `int?` case: a type that cannot express "typed something invalid" forces the caller to guess, and the guess is what loses data. Worth doing here rather than separately, because this PR already changes the control's parameter contract.

### PR 7 — Biological relationships
**Stories:** US-007 – US-013, US-037, US-039 (bio), US-051

- `BiologicalRelationshipService` — `AddParentAsync` (circular check, two-parent cap, duplicate check), `RemoveAsync`, `ReplaceParentAsync`, `GetSiblingsAsync` classifying full vs half
- Profile sections: bio parents (max 2, "Unknown" phantom slots), bio children (birth-date sorted), siblings
- `Shared/PersonSearchSelect.razor` — debounced search, "Create new person" inline, exclusion list
- `Shared/AddRelationshipDialog.razor` — unified 3-step wizard (kind → person → details incl. subtype, certainty, dates, note); replaces all per-type modals
- Extend export coverage

**Done.** Three things from this PR change what PRs 8 and 9 have to do:

- **Two repository methods were added, and both are the seam rather than convenience.** `IBiologicalRelationshipRepository.ReplaceParentAsync(oldLinkId, newLink)` exists because the interface could not express a multi-record mutation as one call, so `ReplaceParentAsync` would have been the half-failing delete-then-add the API-seam rule exists to prevent; the IndexedDB implementation does both halves in one transaction. `IPersonRepository.GetByIdsAsync(ids)` exists because a profile names a handful of relatives and the alternatives were one read each (N+1) or the whole table. **PR 8 needs the equivalent replace on `IAdoptiveRelationshipRepository`, and PR 9 on the marriage and stepparent repositories** — the same reasoning applies unchanged.
- **The phantom-export gap is closed and `schemaVersion` is 2.** Exports carry a `phantoms` section and no longer drop links naming one. Every later PR that extends export coverage inherits this: a relationship to an unidentified ancestor is exportable like any other. See the PR 7 amendment in ADR-007.
- **`AddRelationshipDialog` is built to be extended, not rewritten.** Adding a kind is an entry in its `Catalogue`, a branch in `SaveAsync`, and any extra fields in the details step. Nothing above that is shaped around biology. A free-text relationship note is deliberately *not* built: no relationship entity carries one, and a box that accepted research and discarded it on save would be worse than no box. It arrives with a field to hold it.


### PR 8 — Adoptive relationships
**Stories:** US-014 – US-020, US-039 (adoptive)

Same guards as biological, **no upper cap**; adoption date optional ("Date unknown"); dashed-teal chips with "(A)". Extend export coverage.

**Done.** Three things from this PR change what PR 9 has to do:

- **`IAdoptiveRelationshipRepository.ReplaceParentAsync` exists, for the reason PR 7 gave.** **PR 9 still needs the equivalent on the marriage and stepparent repositories** — ending one marriage record and writing another is the same multi-record mutation, and a delete-then-add there has the same half-applied failure.
- **`schemaVersion` stays at 2, and that is not an oversight.** Adoptive links have been persisted since PR 2 and exported since PR 5, so this PR adds no persisted shape — it is the first that can *create* one through the UI. "Already covered" is now pinned by an E2E test that records an adoption in the browser and finds it in the downloaded file, rather than by reading the export code. **PR 9 is different: the stepparent repository is new, so it bumps the version and extends `TreeSnapshot` in the same PR, or import silently discards every stepparent link.**
- **The wizard has two correction modes, not one.** US-009 replaces a parent; US-016 edits an adoptive link, which may change the date, the person, or both — so it opens with the person already chosen and dispatches to `UpdateAsync` or `ReplaceParentAsync` depending on what actually changed. Editing a date does not rewrite the link's identity. PR 9's "end a marriage" is a third shape again: it edits a record without touching who it names.

Two things worth knowing before extending the profile:

- **The dialog's exclusion list is scoped to the kind being recorded, not to the person.** US-039 makes a biological parent and an adoptive parent different facts about the same child, so excluding everyone already linked in any capacity would refuse to record that a biological parent later adopted their own child. PR 9 needs the same care: a spouse is not excluded from being a stepparent.
- **`AdoptiveRelationshipService` is a sibling of `BiologicalRelationshipService`, deliberately not a shared base class.** The two agree on their guards and disagree on everything the guards are for — no cap, a date of its own, no implied siblings. Factoring the agreement out would leave a base class whose every method took a flag. What is genuinely shared is shared properly: `CircularReferenceChecker` already walks both edge types together.

### PR 9 — Marriage and stepparent relationships
**Stories:** US-021 – US-027, US-038, US-042, US-044

- `IStepparentRelationshipRepository` — **new interface** (does not exist yet) plus its IndexedDB implementation
- `MarriageService` — self-reference check, active-duplicate check, overlap warning, end-after-start validation, `SuggestEndDateFromSpouseDeathAsync` (US-026)
- `StepparentService` — validates the marriage involves a parent of the stepchild
- Profile: "Marriages / Partnerships" and "Stepchildren"; dialog step 3 extended for marriage fields
- ~~Give `TreeStatsService` a cached read invalidated by `TreeDataNotifier`~~ — **already done, in PR 5.** The backup reminder became the third subscriber there, which is the condition this item was waiting for, so the cache was built at the same time rather than left to make one save cost twelve reads for four PRs
- Extend `TreeStatsService` to count stepparent links — the relationship total omits them by design until this PR, and starts silently under-reporting the moment they are written
- Extend export coverage

**Done.** Four things from this PR change what the later ones have to do:

- **`schemaVersion` is 3, and the whole durability path moved with it.** The stepparent store and record have existed since PR 2 and were exported as nothing, which was true while nothing could write one. This PR makes them writable, so `TreeSnapshot`, `ExportDocument`, `ExportService`, `ImportService` and `BrowserTreeDataAdministration` all gained the section in the same commit — `ReplaceAllAsync` clears every object store in one transaction, so a section the snapshot did not carry would have been *deleted* by every import rather than merely skipped. Version 2 files import unchanged, because no version that wrote one could contain a label. The round trip is pinned by an E2E test that records a label in the browser and finds it in the downloaded file.
- **The replace went on the marriage repository and deliberately not on the stepparent one.** `IMarriageRepository.ReplaceSpouseAsync` exists for the reason PRs 7 and 8 gave, and it takes the new details rather than copying the old ones, because the one form that reaches it can change the spouse and the dates together (US-023). There is no stepparent equivalent: that record is three ids and no fields, so changing any of them makes it a different claim rather than a corrected one, which the UI expresses as removing one label and applying another. A replace there would have been delete-plus-add under a better name with no atomicity to buy.
- **Deleting a marriage cascades, and the cascade is the delete rather than a second call.** US-038's last criterion spans two record types, so as two calls it can half-apply and leave a label pointing at a marriage that no longer exists — a step relationship nothing in the app can explain or reach to remove. `IMarriageRepository.DeleteAsync` therefore always cascades, in one IndexedDB transaction, and `ReplaceSpouseAsync` does the same: **PR 13's undo has to treat "remove a marriage" as a multi-record operation**, because restoring the marriage alone brings back a record whose labels are gone. `MarriageService` reports how many went, since the label may be on a third person's profile and nothing else would connect its disappearance to the marriage just removed.
- **Stepparent labels are not recorded through `AddRelationshipDialog`, and that is the one place this PR departed from the wizard.** Every other kind answers "who?" with a search of the whole tree. A stepparent is the person a parent married, so US-038 asks for the action next to those specific spouses — and a free search would have invited recording a step relationship nothing in the tree supports. The flow is a derived candidate list on the profile instead, with the service refusing any label whose marriage does not involve a parent of the child. **PR 10 gets its step edges from records that are guaranteed to have a marriage behind them**; the wizard gained only the spouse kind, at the same price as PR 8's adoptive kinds.

**Five things review caught that the suite did not**, all of them states the tests
asserted around rather than into:

- **An edit could create the duplicate an add refuses.** `UpdateAsync` was the one
  write path that never ran the guard, so clearing the end date on an old record
  while a current one existed left two current marriages between the same pair.
  The duplicate rule now lives in its own method that all three paths share — and
  it applies in one direction only, to an *active* record against other active
  ones, which also unblocked recording an earlier ended marriage for a couple who
  are married now. US-021 always said "active" on both sides; the code read it on
  one.
- **The end-after-start rule compared years alone**, so 15 June to 3 January
  passed while `ImportService` — which compares in full — warned about the same
  record on its own round trip. The pair is now compared at the coarsest
  precision both dates actually carry, which keeps the leniency that exists for
  vaguer records without extending it to dated ones. The first attempt at this
  fix only moved the asymmetry down a level (see below), which is the more
  useful lesson: the rule is not "compare in full when you can", it is "never
  let a field one date leaves blank decide the answer".
- **The US-026 review notice fired on every save**, opening "X is now recorded as
  dying in 1991" after an edit that touched a spelling. Whether a death date
  *changed* is the page's question rather than the service's, so `EditPersonPage`
  compares it across the save. Four people in the sample family were in the state
  that triggered it.
- **A stepparent row vanished when the parent link it ran through was removed.**
  The label, its marriage and the stepparent's own profile were all intact; only
  the stepchild's profile could not reach the marriage to describe it.
  `IMarriageRepository.GetByIdsAsync` closes it — the labels name their own
  marriages, so those are read directly rather than inferred from the parents.
- **Import accepted a label the app refuses to create**, whose marriage involved
  no parent of the child. A file is the one way into this store that never went
  through `StepparentService`, so `Justified` now applies the same rule the
  service does rather than only checking the marriage exists.

Two of the minors changed the seam rather than the surface: the cascading delete
and the spouse replace now **return how many labels went**, and the stepparent
delete returns whether it removed anything, which took three whole-collection
reads off the mutation paths — free against IndexedDB, three requests once the
seam is swapped. `MarriageService` no longer depends on the stepparent repository
at all as a result.

One design-system item is now settled rather than open:

- **A candidate chip states no relationship.** Review found the stepparent
  candidate rendered in the marriage coral, which on a child's profile read as
  the child's own spouse — the one chip in the app whose colour named a
  relationship nobody had recorded. `PersonChip` gained a `Neutral` variant for
  "a person, nothing asserted", which is what an offer is.
- **Step took the dotted stroke the design system reserved for it, which narrows the dashed overload.** The PR 8 finding below stands — adoptive dashed-teal and the phantom dashed-grey still share a stroke — but the third variant did not join them: `PersonChip`'s step variant is dotted in `--step`, with an "(S)" mark beside the adoptive "(A)" so neither depends on colour or on telling two strokes apart at 11px. Both are pinned by computed-style assertions in `ShellLayoutTests`. **PR 10 still needs the decision about dashed** before it draws adoptive and phantom edges on the same canvas; it no longer needs to find room for a third.

**Two more the follow-up review caught**, both in the fixes above rather than in
the original commit — which is the point worth keeping:

- **The precision fix moved the asymmetry rather than removing it.** Comparing
  two dates with `PartialDate.CompareTo` once both carried a month looked like
  the general form of the year-only leniency, but `CompareTo` is a *sort order*:
  it ranks "June 1970" before "15 June 1970", correctly for a list and wrongly
  for a validity test, so a marriage begun on the 15th and annulled later that
  month was refused. A sort order and a validity test are different questions,
  and the same value object can answer one well and the other badly — so
  `PartialDate` now answers both, and `IsKnownToPrecede` is the second: it
  consults a field only when both dates name it, and a blank on either side
  means "not known to be before" rather than "earlier".

  Writing it out in `MarriageService` would have fixed the refusal and left the
  original finding half-open. `ImportService` compares the same two dates, and
  the whole point of that finding was the two paths disagreeing about one
  record; a rule in the service would have had import warning about records the
  form had just accepted, the same bug pointing the other way. Both call the
  domain method now, and a test imports a record the form accepts and asserts
  the warning stays silent.
- **`Justified` ran before the two-parent cap.** A stepparent label is justified
  by a parent link, so judging it against links the cap was about to drop kept a
  label resting on a record that was never stored. Moving it last matches the
  reasoning already written above `CapParentsPerChild`. Where one filter's input
  is another's output, the order is part of the rule rather than a detail of the
  method sequence.

Both are narrow — one needs a month-precise pair, the other a hand-made file
naming three biological parents — and neither was reachable from a green suite:
the first had a test for the year/month pair and none for month/day, and the
second needs a file no export writes.

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

**Carried here so far.** Findings raised in review and deliberately not fixed in the PR that surfaced them, because none was caused by the change under review and folding them in would have widened a reviewed PR. Recorded here rather than as GitHub issues, per the traceability rule in CLAUDE.md: `USER_STORIES.md` is the backlog, and a parallel set of open issues is not maintained.

- **The desktop toast stack covers the "Add adoptive child" button while visible** (PR 8). The stack predates PR 8; the collision does not, because PR 8 put a button where the stack lands. Transient and self-dismissing, so not a blocker — but a toast covering an interactive control is a defect rather than a cosmetic complaint, and it will recur wherever a later PR adds an action in that corner. The fix is positional, not per-page: give the stack somewhere to sit that no page's controls occupy, or make it dodge them.
- **Dashed strokes now carry two meanings** (PR 8). An adoptive chip is dashed teal; the "Unknown" biological parent slot is dashed grey. They are separated by colour and by section heading, and each row also says which it is in words, so nothing is ambiguous in place — but the design system's vocabulary is now overloaded, and the phantom variant (PR 4, extended in PR 7) is the older claim on it. This wants a decision about the chip vocabulary rather than a patch, and it should be taken before PR 10 draws both kinds of edge in the same canvas.
- **Relationship row actions are a 20×16px tap target** (measured during PR 9, predates it). `.relations__action` is a link-styled button, so "Edit", "Remove", "Replace" and now "Label as stepparent" are as tall as their text — around 16px, against the 44px the platform guidelines ask for. Every relationship row in the app has been like this since PR 7; PR 9 only made it measurable, because pinning that the new actions are hit-testable meant reading their boxes. Not caused by this change and not fixable in it without restyling every relationship section at once, which is a design-system decision rather than a patch. The E2E assertion that exists now checks reachability — that a tap at the centre of each control lands on it rather than on something overlapping — which is the part a layout regression would break; it deliberately does not assert a size the design does not currently meet.
- **Death-before-birth is compared as a sort order rather than at shared precision** (found in PR 9 review, predates it). `PersonService` refuses a person born 15 June 1920 who died in 1920 with the exact date unrecorded, because `PartialDate.CompareTo` ranks a year-only date before a dated one in the same year — correct as a sort, wrong as evidence. An infant death with a known birth date and a year-only death date is an ordinary genealogy record and currently cannot be saved at all. The comment directly above that check already describes the behaviour it does not have: *"Only compares what both dates actually record."* `ImportService` compares the same two fields the same way, as a warning rather than a refusal, so a file carrying such a record imports with a warning that is not true of it.
  <br>PR 9 did not cause this and deliberately did not fix it, but it did build the method that closes it: `PartialDate.IsKnownToPrecede` has exactly the semantics that comment claims, so each site is a one-line change. Whoever takes it should also check the birth/death comparisons for the same shape elsewhere rather than fixing only the two sites named here, and should expect the existing refusal to have tests asserting the current behaviour.
- **The mobile heading sits behind the fixed backup banner** (observed during PR 8, predates it). May be an artifact of full-page capture with fixed elements rather than a real overlap — diagnose with `getBoundingClientRect` before changing any CSS, per the Playwright rules in CLAUDE.md.

**What the automated suites do not cover.** Worth knowing before this PR is planned, because it bounds what "green" has ever meant here: everything runs headless Chromium on Linux. Real-device font rendering, Safari and Firefox, a genuine browser restart or storage eviction, and screen-reader announcement of the `aria-live` toast region are human checks and have never been anything else.

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
| ADR-005 | Decided | Hand-written family layout with an SVG renderer, no visualization library |
| ADR-006 | PR 14 spike | Photo crop approach (circular mask, drag + zoom) |
| ADR-007 | Decided | Export file delivery (anchor download vs File System Access API) |

ADR-004 resolved the production-host question outright, so no host ADR is needed. PDF is handled by a print stylesheet rather than a library, so no PDF ADR is needed.

---

## Cross-Cutting Requirements (Every PR)

- Tests in the matching project (`Domain.Tests`, `Application.Tests`, `Storage.Tests`, `UI.Tests`, `E2E.Tests`)
- Every PR shipping UI adds at least one Playwright test against the published site; bUnit alone is not sufficient
- **Every PR is reviewed against the API-Seam Discipline and Durability Requirements above**
- Any PR adding a persisted shape extends export coverage and bumps `schemaVersion` in the same PR
- UI never references domain entities directly; DTOs only
- Phantom persons (`IsPhantom = true`) are excluded from all lists and search results — visible only in the tree, the Identify dialog, and a profile's parent slots. They are **included** in exports, in a separate `phantoms` section, so the relationships naming them survive a backup round trip (ADR-007, amended in PR 7)
- Certainty defaults to `Confirmed`; Likely and Speculative are visually indicated everywhere relationships appear
- Branch naming: `claude/<short-kebab-description>-<4-char-suffix>` per CLAUDE.md
- All UI matches the design tokens and component conventions in `.design/Family Tree Application.zip`
