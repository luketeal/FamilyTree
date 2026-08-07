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
- **Exports exclude phantom persons and any relationship that names one**, per the cross-cutting rule that keeps unidentified ancestors out of lists, searches and exports. This is a real gap in the durability story, not a tidy-up: restoring the sample family from its own export returns 13 relationships rather than 14, because the link to the unidentified grandmother has nowhere to live in a file that does not contain her. It is surfaced to the user in the export summary rather than subtracted quietly, and pinned by tests so it cannot change unnoticed. It should be resolved when relationships become editable in PR 7 — most likely by carrying phantoms in a separate section of the file, so they stay out of lists without being lost.
