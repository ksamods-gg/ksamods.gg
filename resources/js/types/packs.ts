// A mod pack from the content index: mods at pinned versions, which Borea installs together.
export type PackSummary = {
    id: string;
    name: string;
    authors: string[];
    abstract: string | null;
    tags: string[];
    icon: string | null;
    version: string;
    game_min: string | null;
    mod_count: number;
    updated_at: string | null;
    // "3 days ago", worked out on the server.
    updated_ago: string | null;
};

export type PackDetail = PackSummary & {
    // Rendered from the index's markdown, with raw HTML stripped.
    description: string;
    license: string | null;
    links: Record<string, string>;
    mods: {
        id: string;
        version: string;
        name: string;
        abstract: string | null;
        icon: string | null;
        // Whether the index lists the mod, so its page exists.
        listed: boolean;
    }[];
    versions: { version: string; date: string | null; mods: number }[];
};
