export const formatNumber = (value: number) =>
    new Intl.NumberFormat().format(value);

export const formatDate = (iso: string) =>
    new Date(iso).toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
    });

export function formatBytes(bytes: number) {
    const units = ['B', 'KB', 'MB', 'GB'];
    let i = 0;

    while (bytes >= 1024 && i < units.length - 1) {
        bytes /= 1024;
        i++;
    }

    return `${bytes.toFixed(i ? 1 : 0)} ${units[i]}`;
}

// Listings may use tags outside the index's vocabulary; title-case those.
export const tagName = (tag: string, names: Record<string, string>) =>
    names[tag] ??
    tag.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
