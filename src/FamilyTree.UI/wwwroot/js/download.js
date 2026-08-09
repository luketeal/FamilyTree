// Hands a generated file to the browser's download machinery.
//
// A Blob plus a synthetic anchor click, rather than the File System Access API's
// showSaveFilePicker: see ADR-007. The picker would let a user re-save over the
// same backup file, but it exists in one browser engine, and a backup mechanism
// that only works in Chromium is not a backup mechanism for the people using
// Firefox or Safari. The anchor path works everywhere.

export function downloadText(fileName, text, mimeType) {
    const blob = new Blob([text], { type: mimeType });
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;

    // Two steps below exist for engines other than Chromium, which needs
    // neither: Firefox is documented to ignore a click on a detached anchor, and
    // Safari has not read the blob by the time click() returns, so revoking
    // without yielding cancels the download. Both are unconditional rather than
    // sniffed — they cost a DOM insertion and one turn of the event loop, which
    // is less than a branch would cost to get wrong.
    //
    // Neither claim is verified here: this project's test container can only run
    // Chromium. What *is* verified, in ExportImportTests, is that both steps
    // actually happen — the anchor is in the document when clicked, and the
    // revoke lands on a later task. Chromium passes with or without them, so
    // without those assertions either could be deleted as dead weight and the
    // download would break only for the people this suite cannot reach.
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();

    setTimeout(() => URL.revokeObjectURL(url), 0);
}
