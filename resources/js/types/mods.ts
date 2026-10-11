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
    // "3 days ago", worked out on the server.
    updated_ago: string;
    version: string | null;
    // What the newest release needs.
    loader: string | null;
    game_min: string | null;
    game_min_revision: number | null;
    // Null until someone reviews the mod.
    rating?: Rating | null;
};

export type Rating = { average: number; count: number };

// A review or comment, with what the viewer may do with it.
export type Post = {
    id: number;
    // Null on a review without text, and on a deleted comment.
    body: string | null;
    // Null on a deleted comment, which stays only to keep its replies in context.
    author: { name: string; avatar: string | null } | null;
    created_at: string;
    edited: boolean;
    deleted: boolean;
    mine: boolean;
    canDelete: boolean;
    canReport: boolean;
};

// A comment and the replies to it, which nest the same way.
export type CommentNode = Post & { replies: CommentNode[] };

export type Reviews = {
    average: number | null;
    count: number;
    mine: { rating: number; body: string | null } | null;
    items: (Post & { rating: number; replies: CommentNode[] })[];
};

export type Comments = {
    count: number;
    items: CommentNode[];
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

export type NotificationData = {
    kind: 'reply' | 'review_reply' | 'mention';
    actor: { name: string; avatar: string | null };
    mod: { id: string; name: string };
    excerpt: string;
    comment_id: number;
    tab: 'comments' | 'reviews';
};

export type AppNotification = {
    id: string;
    data: NotificationData;
    read: boolean;
    created_at: string;
};
