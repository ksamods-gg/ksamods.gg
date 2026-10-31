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
    download_url: string | null;
    updated_at: string;
    // "3 days ago", worked out on the server.
    updated_ago: string;
};

export type ShareReleaseItem = {
    id: number;
    version: string;
    status: 'stable' | 'testing' | 'dev';
    size: number;
    sha256: string;
    game_version: string;
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
};
