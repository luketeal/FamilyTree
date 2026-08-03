# ADR-004: Demo-first delivery on Blazor WebAssembly with a shared UI library

**Date:** 2026-08-03  
**Status:** Decided

## Context

The original implementation plan built bottom-up: domain, then persistence, then application services, then UI, with the first user-visible screen arriving several PRs in and the tree — the centrepiece of the product — arriving at PR 10 of 14. Nobody outside the project could try anything until most of the backend existed, so every UI and interaction-design assumption stayed unvalidated until very late.

We want a deployable demo early: real screens, real interactions, throwaway data, shared with family testers to tune the user experience while the persistent backend is still being built.

The intended host was GitHub Pages. This forces a constraint that turned out to be decisive: **Blazor Server cannot be deployed to static hosting.** It requires a live ASP.NET Core process holding a SignalR circuit per connected user; the browser receives an HTML shell and every DOM update round-trips to the server. There is no static-export mode. GitHub Pages serves static files only.

Options considered for the demo client:

1. **Blazor WebAssembly** — compiles to static `.wasm`/`.dll` assets, runs entirely in the browser, works on GitHub Pages, and can reference the existing `FamilyTree.Domain` project directly.
2. **React prototype from the existing mockups** — `.design/Family Tree Application.zip` already contains mid-fidelity React mockups (`mid-shell.jsx`, `mid-profile.jsx`, `mid-dialogs.jsx`, `mid-tokens.jsx`), so a clickable demo would come fastest.
3. **Host the Blazor Server app somewhere non-static** — Azure App Service, Fly.io, a container host.

## Decision

Build the demo as a **Blazor WebAssembly** application deployed to GitHub Pages, with all UI living in a **shared Razor Class Library** (`FamilyTree.UI`) that is host-agnostic.

Data in the demo is persisted to **browser localStorage** through the existing `FamilyTree.Domain` repository interfaces. The `FamilyTree.Application` service layer sits between the UI and those interfaces exactly as originally planned, so the demo enforces the **real business rules** — circular-reference prevention, the two-biological-parent cap, date ordering, duplicate detection — rather than stubs.

**The final production host is explicitly left undecided.** Two paths remain open:

- return to **Blazor Server** with EF Core and SQLite (the existing `FamilyTree.Web` and `FamilyTree.Infrastructure` projects), or
- stay on **WebAssembly** and add a JSON API backend.

Because the host is undecided, `FamilyTree.UI` must compile and behave identically under either. Components are written to the common subset of both models:

- JS interop through `IJSRuntime` **async only** — never `IJSInProcessRuntime` or `IJSInProcessObjectReference`
- No `HttpContext`, `IHttpContextAccessor`, or any server-only DI registration
- No direct `System.IO` filesystem access; file upload via `InputFile`, download via JS interop
- No synchronous blocking (`.Result`, `.Wait()`) — deadlock behaviour differs between hosts
- No multi-threading assumptions (`Task.Run`, `Thread.Sleep`) — WASM is single-threaded
- All data access through `FamilyTree.Domain` repository interfaces; components never construct a store directly

A separate ADR will record the host decision when it is made.

## Reasoning

**Why WebAssembly over a React prototype.** The React mockups would reach a clickable demo fastest, but every screen would then be built twice — once in React to gather feedback, again in Blazor to ship it — and the feedback would validate a prototype whose behaviour is disconnected from the domain model. Blazor WASM can reference `FamilyTree.Domain` directly, so the demo exercises the real `Person`, `PartialDate`, `RelationshipCertainty`, and phantom-person types. Feedback then validates the actual model, and defects found in the demo are defects fixed in the product.

**Why this does not fight the existing architecture.** ADR-001 already decided that the domain and application layers stay ignorant of the storage provider, with all access behind repository interfaces and EF Core confined to `Infrastructure`. A localStorage-backed repository is precisely the substitution ADR-001 was designed to permit. The inversion exercises that decision rather than contradicting it.

**Why not simply host Blazor Server on a real server.** That would work, but it keeps the bottom-up ordering that delayed feedback in the first place, adds hosting cost and deployment surface before there is anything worth hosting, and gives testers a URL that is only up when the backend is. A static demo is free, always up, and shareable by link.

**Why the host decision is deferred rather than made now.** The demo will tell us things we do not yet know — how large real trees get, whether the tree view needs server-side layout computation, whether offline use matters to testers, whether first-load payload is a problem on the devices people actually use. Those are the inputs to the host choice, and they arrive during the demo phase. Committing now would be guessing. The cost of deferring is the portability discipline listed above; the cost of guessing wrong is rewriting the UI layer.

## Consequences

**Easier**

- A shareable, always-on demo URL exists from PR 1, before any persistent backend work is finished.
- The tree-visualization spike (ADR-005) moves to the front of the queue instead of PR 9, resolving the project's largest technical unknown early.
- UI work is done once. The RCL is consumed by the demo host today and by whichever host wins later.
- Business rules are validated by real testers against the real domain model.
- The repository gains its first CI workflow, since GitHub Pages deployment requires one.

**Harder or foreclosed**

- **Photo upload (US-005) is cut from the demo.** localStorage caps at roughly 5 MB per origin; a few hundred people as JSON fits comfortably, base64-encoded images do not. Photos return when a real backend exists, or via IndexedDB if testers ask for them.
- **PDF generation (US-050) is cut from the demo.** The candidate libraries (QuestPDF/SkiaSharp, PuppeteerSharp) depend on native binaries or a headless browser and are a poor fit for WASM. This is server-side work.
- **Domain entities cannot be serialized directly.** `Person` has a private constructor, private setters, and bidirectional navigation collections, so `System.Text.Json` can neither round-trip it nor avoid circular references. The browser store must persist flat records mirroring the EF table shapes and rehydrate through domain factory methods — a second persistence mapping to keep in sync with the EF configurations.
- **Every component carries a portability tax.** The constraints above must hold for as long as the host is undecided. A component that reaches for synchronous JS interop will work in the demo and break under Blazor Server.
- **First load is a multi-megabyte download.** Acceptable for a demo, and reducible by trimming, but real on slow mobile connections. `InvariantGlobalization` must stay **off** despite the payload saving — this application formats dates heavily and needs ICU.
- **GitHub Pages needs project-page plumbing**: a `.nojekyll` marker (Jekyll strips Blazor's `_framework` directory because it begins with an underscore), a rewritten `<base href>` for the repository subpath, and a `404.html` fallback so deep links resolve.
- **No feedback backend.** The demo is static, so tester feedback arrives out-of-band — a GitHub issue link, or testers exporting their tree and sending the file.
