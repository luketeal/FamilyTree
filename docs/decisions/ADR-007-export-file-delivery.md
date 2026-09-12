# ADR-007: Anchor download for export, not the File System Access API

**Date:** 2026-08-07  
**Status:** Decided

## Context

PR 5 makes export the durability mechanism for a client-only app: the browser holds the only copy of the tree (ADR-004), and a downloaded file is the only copy that survives a cleared cache, a lost device, or a browser reclaiming storage. The implementation plan asked for the File System Access API to be evaluated here, so that "the tree can live in a user-controlled file" rather than in a folder of dated downloads.

Two ways to get the JSON out of WebAssembly and onto disk:

1. **Blob plus a synthetic anchor click** — build a `Blob`, take an object URL, click an `<a download>`, revoke the URL. The file lands wherever the browser puts downloads.
2. **File System Access API** — `showSaveFilePicker()` returns a `FileSystemFileHandle` the page can keep. The user chooses the location once; later exports can write back to the same file through the same handle, without a picker and without accumulating `familytree-2026-08-07.json`, `familytree-2026-08-08.json`, and so on.

Option 2 is the better experience by a distance. It is closer to "the tree is a file you own" than "the tree is a thing you periodically download", and re-saving over a known file is exactly the gesture a backup wants.

## Decision

Ship the anchor download. Do not use the File System Access API in PR 5, and do not add it as a progressive enhancement yet.

## Reasoning

`showSaveFilePicker` is a Chromium-only API. As of this writing Safari and Firefox implement the Origin Private File System — storage the page can write to but the user cannot see — and neither implements the picker that makes a handle point at a file the user chose. Firefox's position on the picker has been negative on privacy grounds rather than merely unimplemented, so this is not obviously a matter of waiting.

That turns the choice into a question about who the backup is for. A durability mechanism that works in one browser engine is not a durability mechanism; it is a feature for Chrome users and a silent gap for everybody else — and the person on Safari has no way to notice that the app is protecting them less well. Since the anchor path has to exist regardless as the fallback, adding the picker means shipping and maintaining two write paths, two sets of failure modes (a `SecurityError` from a lost user gesture, a revoked permission on a stored handle, a handle that outlives the file it pointed at) and two things to test, for a better experience on a subset of users.

The cost of not having it is a folder of dated files rather than one file that updates. That is untidy, not dangerous — arguably it is safer, since a mistaken export cannot overwrite a good backup. So the trade is a real convenience against a real complexity, and at this stage of the project the complexity is not worth it.

Storing the handle in IndexedDB and reusing it silently was considered and rejected on the same grounds, plus one more: a page that can write to a file on disk without a visible gesture is a thing this app should be slower to become than one browser's API makes it.

## Consequences

- Export works identically in every browser the app supports, including mobile Safari, where the file goes to Files rather than to a chosen folder.
- Users accumulate one file per export. The filename is `familytree-YYYY-MM-DD.json`, date-first and invariant, so a folder of backups sorts chronologically by name in every locale.
- There is no "save over my backup" action and therefore no way for the app to destroy a good backup with a bad one.
- Export cannot be automated or scheduled: every backup needs a click, which is what the reminder in the shell exists to prompt. It triggers on the tree having changed since the last export rather than on elapsed time, so the prompt tracks whether there is unsaved work rather than how long ago the last click was.
- Revisit when two of the three major engines ship the picker. The change would be contained — `IFileDownloadInterop` is the only seam involved, and the anchor path stays as the fallback either way.

## Note on the exported payload

Two decisions about the file itself, recorded here because they are the kind of thing a future reader will ask about:

- **The format is indented JSON with enum names, not ordinals.** Indentation costs bytes on a file nobody transmits and buys a backup a person can read, diff and repair by hand. Enum names matter more: inserting a member into `Gender` would renumber every later one, silently changing the meaning of every file already written.
- **Exports excluded phantom persons and any relationship that named one**, per the cross-cutting rule that keeps unidentified ancestors out of lists, searches and exports. This was a real gap in the durability story, not a tidy-up: restoring the sample family from its own export returned 13 relationships rather than 14, because the link to the unidentified grandmother had nowhere to live in a file that did not contain her. **Resolved in PR 7** — see the amendment below.

## Amendment (PR 7): phantoms are carried in their own section

**Date:** 2026-08-23

The gap above is closed, along the lines this ADR anticipated. `ExportDocument` gains a `phantoms` array alongside `people`, and links naming a phantom are no longer filtered out. `schemaVersion` goes to 2.

A phantom is written as nothing but an id, because that is genuinely all there is — no name, no dates, nothing anybody recorded. The id is the point: it is what the links refer to.

**Why a separate section rather than an `isPhantom` flag on `ExportedPerson`.** The rule that matters is that nothing treats a placeholder as a person. A flag makes that a thing every reader has to remember to check, and the first one that forgets shows an unnamed row in a list of people. Two sections make it structural: anything reading `people` sees only people, and a reader that does not know about `phantoms` ignores them rather than mishandling them. It also keeps the count honest — the export summary reports people and phantoms separately, so the file's totals match what the app displays.

**Why not leave it.** This project treats silent data loss as a defect class, and a relationship to an unidentified ancestor is research: somebody established that Margaret had a mother without establishing who she was. Losing one relationship per phantom on every backup round trip is exactly the failure export exists to prevent, and it was being lost quietly enough that only a test constant recorded it.

**Backwards compatibility is free rather than engineered.** A version 1 file has no `phantoms` array and, by construction, no link that names one — that is what a version 1 export was. Import reads it unchanged. The version bump exists so a version 1 *app* cannot read a version 2 file and drop the placeholders and their links as orphans while reporting success.

**Consequences.** Restoring the sample family now returns 11 records' worth of people-and-placeholders and all 14 relationships. `RestoredStats` in `tests/FamilyTree.E2E.Tests/ExportImportTests.cs` is no longer a smaller number than `SampleStats`; it is the same constant, and the test that used to pin the loss now pins its absence. A file of nothing but phantoms is still refused on both sides: it restores nothing anybody can read, and accepting it would let a set of empty slots overwrite a real tree.
