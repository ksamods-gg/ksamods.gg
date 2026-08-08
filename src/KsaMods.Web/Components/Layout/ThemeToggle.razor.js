// Theme choice: applying it, flipping it, and keeping it applied.
//
// The first paint is handled by a blocking snippet in App.razor, not here. A module runs after
// first paint, so doing it in this file would show every dark-theme visitor a white flash.
//
// What this file exists for beyond the click: enhanced navigation swaps in server-rendered HTML
// whose <html> element carries no theme class, and Blazor's diff then strips the class off the
// live document. The theme is still stored, so it survives a reload, but it visibly reverts the
// moment you follow a link, which reads as "the setting does not save". So the class has to be
// re-applied after every enhanced navigation, not only on first load.

const STORAGE_KEY = 'ksamods.theme';

// Private browsing and blocked storage throw on access rather than returning null.
function read() {
    try {
        return localStorage.getItem(STORAGE_KEY);
    } catch {
        return null;
    }
}

function write(theme) {
    try {
        localStorage.setItem(STORAGE_KEY, theme);
    } catch {
        /* The choice still holds for this page. */
    }
}

/// Puts the stored choice back on the root element. No stored choice means both classes come
/// off, which hands the decision back to prefers-color-scheme.
function applyStored() {
    const stored = read();
    const root = document.documentElement;

    root.classList.toggle('dark', stored === 'dark');
    root.classList.toggle('light', stored === 'light');
}

function currentTheme() {
    const root = document.documentElement;
    if (root.classList.contains('dark')) return 'dark';
    if (root.classList.contains('light')) return 'light';

    // Nothing chosen yet, so the effective theme is whatever the machine asked for.
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function wire() {
    const button = document.querySelector('[data-theme-toggle]');
    if (!button || button.dataset.themeWired === 'true') return;

    button.dataset.themeWired = 'true';
    button.addEventListener('click', () => {
        write(currentTheme() === 'dark' ? 'light' : 'dark');
        applyStored();
    });
}

applyStored();
wire();

// window.Blazor rather than a bare Blazor: optional chaining does not save you from a
// ReferenceError on an undeclared identifier, only from a null property.
window.Blazor?.addEventListener?.('enhancedload', () => {
    applyStored();
    wire();
});
