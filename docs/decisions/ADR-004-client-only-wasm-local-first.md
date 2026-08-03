# ADR-004: Client-only Blazor WebAssembly with local-first storage

**Date:** 2026-08-03  
**Status:** Decided

## Context

The original plan built bottom-up — domain, persistence, application services, then UI — with the first user-visible screen several PRs in and the tree, the centrepiece of the product, at PR 10 of 14. Nothing could be shown to anyone until most of the backend existed, so every interaction-design assumption stayed unvalidated until very late.

We wanted a deployable demo early, hosted on GitHub Pages. That forced a constraint that turned out to be decisive: **Blazor Server cannot be deployed to static hosting.** It requires a live ASP.NET Core process holding a SignalR circuit per user; there is no static-export mode. GitHub Pages serves static files only.

Examining what the product actually needs settled the larger question. `USER_STORIES.md` states the scope as **"Single-user, single tree, no authentication required"**, and none of the 53 stories involve accounts, sign-in, sharing, collaboration, multi-device sync, or any other server-dependent behaviour. The backend was an assumption inherited from the original tech-stack line, not a requirement derived from the backlog.

Options considered:

1. **Client-only Blazor WebAssembly, local-first** — static assets on GitHub Pages, data in the browser, no server at all.
2. **Blazor WebAssembly plus a JSON API** — browser frontend, ASP.NET Core + EF Core + SQLite backend for durable server-side storage.
3. **Blazor Server on a real host** — Azure App Service, Fly.io, or a container host.
4. **React prototype from the existing mockups** — `.design/` already contains mid-fidelity React mockups.

## Decision

Build the application as a **client-only Blazor WebAssembly app** deployed to GitHub Pages, with all data persisted **local-first in the browser via IndexedDB**. There is no backend.

Delivery is inverted from the original plan: UI ships first and continuously, so real user feedback shapes the interaction design as it is built.

**The swap to a server backend is preserved as a seam, not built.** All data access goes through the existing `FamilyTree.Domain` repository interfaces, per ADR-001. The IndexedDB implementations live in `FamilyTree.Storage.Browser`, named so that a future `FamilyTree.Storage.Api` implementing the same interfaces over `HttpClient` is a DI registration change rather than a restructure. No UI or application code would change.

Consequently, these are **deleted**, not parked:

- `FamilyTree.Infrastructure` — EF Core, SQLite, migrations, entity configurations
- `FamilyTree.Infrastructure.Tests`
- `FamilyTree.Web` — the Blazor Server host
- `FamilyTree.Web.E2E.Tests`

The schema thinking they encode is preserved in ADR-003 and recoverable from git history.

**Export and import become the durability model,** not a late-stage feature. See Consequences.

## Reasoning

**Why client-only over WASM-plus-API.** No story in the backlog needs a server. An API layer would mean designing, versioning, securing, and hosting endpoints, plus adding authentication that the stated scope explicitly excludes, to serve requirements that do not exist. It can be added later behind the existing interfaces if the scope ever grows — which is cheap precisely because ADR-001 put the seam there already.

**Why not Blazor Server on a real host.** It keeps the bottom-up ordering that delayed feedback, adds hosting cost and deployment surface before there is anything worth hosting, and gives testers a URL that is only up when the backend is. A static client is free, always up, offline-capable, and shareable by link.

**Why WebAssembly over the React prototype.** The React mockups would reach a clickable demo fastest, but every screen would be built twice, and feedback would validate a prototype disconnected from the domain model. Blazor WASM references `FamilyTree.Domain` directly, so the app exercises the real `Person`, `PartialDate`, `RelationshipCertainty`, and phantom-person types, and enforces the real business rules — circular-reference prevention, the two-biological-parent cap, date ordering. Defects found by testers are defects in the product, not in a mock.

**Why IndexedDB rather than localStorage.** localStorage caps near 5 MB per origin and stores strings only. IndexedDB removes that ceiling as the binding constraint, which keeps profile photos (US-005) in scope rather than deferring them.

**Why the host decision no longer needs deferring.** An earlier draft of this ADR left the production host open pending a later decision. Committing removes the host-portability discipline that deferral required — a discipline that failed silently, since a component using synchronous JS interop works perfectly in WASM and breaks only when a Server host is wired up months later. There is now one host and one mental model.

## Consequences

**Easier**

- A shareable, always-on URL exists from the first PR, and is the product rather than a throwaway.
- The application works offline and costs nothing to host, permanently.
- The tree-visualization spike (ADR-005) moves to the front of the queue, resolving the largest technical unknown early.
- No API layer, no authentication, no server deployment, no dual-host discipline, no CI matrix.
- The `SQLitePCLRaw.lib.e_sqlite3` high-severity advisory (GHSA-2m69-gcr7-jv3q), inherited transitively from EF Core Sqlite, leaves the dependency tree with `FamilyTree.Infrastructure`.
- Photos (US-005) return to scope, and PDF output (US-050) becomes reachable through browser print-to-PDF with a print stylesheet rather than a server-side PDF library.

**Harder or foreclosed**

- **The browser is the only copy of the data.** This is the central risk and the main cost of the decision. Clearing site data, switching browsers, or moving to a new machine destroys the tree. For genealogy — data representing years of research and interviews with relatives who may no longer be available — that is a materially worse failure than for most applications. It is mitigated deliberately, not assumed away:
  - call `navigator.storage.persist()` at startup so the origin is exempt from routine eviction
  - move export and import early in the sequence; until they exist, every tester is one cache-clear from total loss
  - surface an explicit "last exported N days ago" prompt rather than treating backup as the user's problem
  - evaluate the File System Access API so the tree can live in a user-controlled file backed up by normal means
- **No multi-device access and no sharing.** Moving a tree between machines is a manual export/import. This matches the stated single-user scope but would need the API swap if that changes.
- **Domain entities cannot be serialized directly.** `Person` has a private constructor, private setters, and bidirectional navigation collections, so `System.Text.Json` can neither round-trip it nor escape the circular references. The browser store persists flat records and rehydrates through domain factory methods.
- **The API seam can rot silently.** The interfaces survive contact with an API; naive *usage* does not. Looping `GetByIdAsync` per tree node is free against IndexedDB and catastrophic over HTTP. Bulk reads and single-call mutations are required from the start, or the swap becomes a rewrite even though the interfaces never changed.
- **First load is a multi-megabyte download**, reducible by trimming but permanent. `InvariantGlobalization` must stay **off** despite the payload saving — this application formats dates heavily and needs ICU.
- **GitHub Pages needs project-page plumbing**: a `.nojekyll` marker (Jekyll strips Blazor's `_framework` directory), a rewritten `<base href>` for the repository subpath, and a `404.html` fallback for deep links.
