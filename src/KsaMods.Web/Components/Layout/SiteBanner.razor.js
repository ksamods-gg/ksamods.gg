// Dismissal for the site notice strip.
//
// The banner is rendered hidden and revealed here, so someone who closed it never sees it flash
// on the next page load. Dismissal is per-browser and keyed by a hash of the banner text, which
// means editing the message brings it back for everyone instead of staying silently dismissed.

const STORAGE_PREFIX = 'ksamods.banner.';

function apply() {
    const banner = document.getElementById('site-banner');
    if (!banner || banner.dataset.bannerReady === 'true') return;

    const key = STORAGE_PREFIX + banner.dataset.bannerKey;

    if (read(key) === 'dismissed') {
        banner.remove();
        return;
    }

    banner.hidden = false;
    banner.dataset.bannerReady = 'true';

    banner.querySelector('[data-banner-dismiss]')?.addEventListener('click', () => {
        banner.remove();
        write(key, 'dismissed');
    });
}

// Private browsing and blocked storage both throw on access rather than returning null, and a
// banner that cannot remember a dismissal is still better than a page that fails to render.
function read(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

function write(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        /* dismissal lasts for this page only */
    }
}

apply();

// Enhanced navigation swaps the document without re-running module scripts, so the banner has to
// be re-checked after each one.
Blazor?.addEventListener?.('enhancedload', apply);
