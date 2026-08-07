// Keeps a modal overlay inside the part of the screen the user can actually see.
//
// The soft keyboard shrinks the *visual* viewport without changing the *layout*
// viewport. A position:fixed overlay is sized against the layout viewport, so
// with the keyboard up it is roughly twice the height of the visible area and
// most of it sits off screen. Reaching the rest means panning the visual
// viewport, which is a browser-level gesture — it drags the whole app,
// including the bottom rail, and no amount of overflow:hidden prevents it.
//
// So the overlay is sized and offset to the visual viewport instead. There is
// then nothing off screen to pan towards.

const trackers = new WeakMap();

let lockCount = 0;
let restoreScrollY = 0;

/**
 * Pins the document. overflow:hidden alone is unreliable on iOS Safari, which
 * is why this takes the position:fixed route and restores the scroll offset on
 * release.
 */
export function lockScroll() {
    if (lockCount++ > 0) {
        return;
    }

    restoreScrollY = window.scrollY;
    const style = document.body.style;
    style.position = 'fixed';
    style.top = `-${restoreScrollY}px`;
    style.left = '0';
    style.right = '0';
    style.width = '100%';
}

/** Counted, so closing one of two stacked overlays does not unpin the page. */
export function unlockScroll() {
    if (lockCount === 0 || --lockCount > 0) {
        return;
    }

    const style = document.body.style;
    style.position = '';
    style.top = '';
    style.left = '';
    style.right = '';
    style.width = '';
    window.scrollTo(0, restoreScrollY);
}

export function trackViewport(element) {
    const viewport = window.visualViewport;
    if (!viewport || !element || trackers.has(element)) {
        return;
    }

    const apply = () => {
        element.style.height = `${viewport.height}px`;
        element.style.top = `${viewport.offsetTop}px`;
    };

    trackers.set(element, apply);
    apply();
    viewport.addEventListener('resize', apply);
    viewport.addEventListener('scroll', apply);
}

export function untrackViewport(element) {
    const viewport = window.visualViewport;
    const apply = element && trackers.get(element);
    if (!viewport || !apply) {
        return;
    }

    viewport.removeEventListener('resize', apply);
    viewport.removeEventListener('scroll', apply);
    trackers.delete(element);
    element.style.height = '';
    element.style.top = '';
}

/** One call per open, so a component cannot half-apply the pair. */
export function open(element) {
    lockScroll();
    trackViewport(element);
}

export function close(element) {
    untrackViewport(element);
    unlockScroll();
}
