export type ShareType = 'vehicle' | 'save';

export type ShareCheck = { name: string; passed: boolean; message: string };

export type ShareDependency = {
    id: string | null;
    kind: string;
    min: string | null;
    max: string | null;
    // The mod's name and icon when the index lists it.
    name: string | null;
    icon: string | null;
};

export type ShareSummary = {
    id: string;
    type: ShareType;
    name: string;
    authors: string[];
    abstract: string;
    tags: string[];
    version: string | null;
    game_version: string | null;
    // The newest game build it works on, when the index caps it, and whether that is older
    // than the current build.
    game_max: string | null;
    outdated: boolean;
    download_url: string | null;
    // Players who downloaded it, each counted once a day.
    downloads: number;
    updated_at: string;
    // "3 days ago", worked out on the server.
    updated_ago: string;
    // The cover screenshot's square crop.
    icon: string | null;
};

export type ShareReleaseItem = {
    id: number;
    version: string;
    status: 'stable' | 'testing' | 'dev';
    size: number;
    sha256: string;
    game_version: string;
    game_max: string | null;
    outdated: boolean;
    date: string;
    // Null once taken down.
    url: string | null;
    taken_down: boolean;
};

export type ShareDetail = ShareSummary & {
    description: string | null;
    license: string;
    dependencies: ShareDependency[];
    pull_request_url: string | null;
    releases: ShareReleaseItem[];
    // Screenshots, cover first.
    images: { id: number; url: string }[];
};
