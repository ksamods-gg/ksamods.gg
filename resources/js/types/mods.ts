export type ModSummary = {
    id: string;
    type: string;
    name: string;
    authors: string[];
    abstract: string | null;
    tags: string[];
    icon: string | null;
    downloads: number;
    updated_at: string;
    version: string | null;
};

export type Release = {
    version: string;
    status: string;
    date: string;
    game_min: string | null;
    download_url: string;
    download_size: number | null;
    mirrors: string[];
    loader: { id: string; name: string | null; min: string | null } | null;
    dependencies: {
        id: string;
        name: string | null;
        kind: string;
        min: string | null;
        max: string | null;
    }[];
    changelog: string | null;
    changelog_url: string | null;
};

// A listing's editable fields, as the listing form shows them.
export type ListingValues = {
    id: string;
    type: string;
    name: string;
    authors: string;
    abstract: string | null;
    description: string;
    license: string | null;
    tags: string[];
    github: string | null;
    spacedock: number | null;
    authority: string | null;
    forums: string | null;
    repository: string | null;
    bugtracker: string | null;
    game_min: string | null;
    loader_id: string | null;
    loader_min: string | null;
};

export type Mod = Omit<ModSummary, 'version'> & {
    description: string;
    license: string | null;
    links: Record<string, string>;
    published_at: string;
    releases: Release[];
};
