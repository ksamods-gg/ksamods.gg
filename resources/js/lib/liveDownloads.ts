import { reactive } from 'vue';

export type DownloadsChanged = { type: string; id: string; downloads: number };

// Download totals that arrived over Reverb since the site was opened, keyed type/id. The layout
// listens; rows and pages read. The server never receives any, so its render stays the page's own.
const totals = reactive<Record<string, number>>({});

export function noteDownloads(change: DownloadsChanged): void {
    const key = `${change.type}/${change.id}`;
    totals[key] = Math.max(totals[key] ?? 0, change.downloads);
}

// Counts only go up, so the larger of the page's count and the live one is the current one.
export function liveDownloads(
    type: string,
    id: string,
    loaded: number,
): number {
    return Math.max(loaded, totals[`${type}/${id}`] ?? 0);
}
