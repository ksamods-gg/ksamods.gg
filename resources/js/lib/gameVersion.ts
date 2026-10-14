const KEY = 'ksamods.gameVersion';

// The game build the player said they run, remembered in this browser only. Storage can be
// unavailable (private windows, blocked site data), and then nothing is remembered.

export function rememberedGameVersion(): string | null {
    try {
        return localStorage.getItem(KEY);
    } catch {
        return null;
    }
}

export function rememberGameVersion(label: string | null): void {
    try {
        if (label === null) {
            localStorage.removeItem(KEY);
        } else {
            localStorage.setItem(KEY, label);
        }
    } catch {
        // Not remembered, which only costs picking it again.
    }
}
