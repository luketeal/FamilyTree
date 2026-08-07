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
    // Firefox ignores a click on an anchor that is not in the document.
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();

    // Revoking synchronously cancels the download in Safari, which has not yet
    // read the blob when click() returns. A turn of the event loop is enough,
    // and leaking the object URL for that long costs nothing.
    setTimeout(() => URL.revokeObjectURL(url), 0);
}
