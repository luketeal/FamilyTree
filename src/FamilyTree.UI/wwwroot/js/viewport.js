// Whether the shell is in its narrow layout. Matches the 768px breakpoint the
// stylesheets use, so the two cannot disagree about what "mobile" means.
//
// This is a media query rather than a user-agent test on purpose: a narrow
// desktop window gets the same treatment as a phone, which is what the CSS
// already does.
export function isNarrow() {
    return window.matchMedia('(max-width: 768px)').matches;
}
