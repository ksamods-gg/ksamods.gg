// Click handling for the theme toggle.
//
// The initial class is applied by a blocking snippet in App.razor, not here: a module runs after
// first paint, so doing it in this file would show every dark-theme visitor a white page for a
// frame. All this does is flip the choice and remember it.

const STORAGE_KEY = 'ksamods.theme';

function current() {
    const root = document.documentElement;
    if (root.classList.contains('dark')) return 'dark';
    if (root.classList.contains('light')) return 'light';

    // No explicit choice yet, so the effective theme is whatever the machine asked for.
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function apply(theme) {
    const root = document.documentElement;
    root.classList.toggle('dark', theme === 'dark');
    root.classList.toggle('light', theme === 'light');

    try {
        localStorage.setItem(STORAGE_KEY, theme);
    } catch {
        /* Storage blocked. The choice still holds for this page. */
    }
}

function wire() {
    const button = document.querySelector('[data-theme-toggle]');
    if (!button || button.dataset.themeWired === 'true') return;

    button.dataset.themeWired = 'true';
    button.addEventListener('click', () => apply(current() === 'dark' ? 'light' : 'dark'));
}

wire();

// Enhanced navigation swaps the document without re-running module scripts, so the button in the
// new DOM needs wiring again.
Blazor?.addEventListener?.('enhancedload', wire);
