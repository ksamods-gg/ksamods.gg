// A Laravel paginator, as the admin pages receive it.
export type Paginated<T> = {
    data: T[];
    current_page: number;
    last_page: number;
    total: number;
    prev_page_url: string | null;
    next_page_url: string | null;
};

export type AdminPost = {
    type: 'review' | 'comment';
    id: number;
    body: string | null;
    rating: number | null;
    author: { name: string; avatar: string | null };
    mod: { id: string; name: string };
    // The post on its mod's page.
    url: string;
    created_at: string;
};

export type AdminReport = {
    id: number;
    reason: string;
    details: string | null;
    reporter: string;
    created_at: string;
    resolved_at: string | null;
    // Null once the post is deleted.
    post: AdminPost | null;
};

export type AdminUser = {
    id: number;
    name: string;
    avatar: string | null;
    github_login: string | null;
    discord_username: string | null;
    role: 'moderator' | 'admin' | null;
    reviews: number;
    comments: number;
    created_at: string;
};
