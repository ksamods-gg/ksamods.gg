<?php

/*
 * ksamods.gg as a share host for vehicles and saves (RFC 0094, RFC 0104, spec/share-host.md).
 *
 * ksamods.gg keeps its archives and data files outside GitHub ("own storage, signed"): every data
 * file is signed with an Ed25519 key, and both data files and archives live in Hetzner Object
 * Storage, which serves them; the signing key stays on the app, apart from that server (rule 24).
 * `php artisan share:host` prints the hosts.toml table these settings describe.
 */
return [

    // The table name in hosts.toml, which listings name under [releases].
    'key' => env('SHARE_HOST_KEY', 'ksamods'),

    // The content types this host takes, and the largest archive of each (max_bytes).
    'types' => ['vehicle', 'save'],
    'max_bytes' => [
        'vehicle' => (int) env('SHARE_MAX_VEHICLE_BYTES', 10 * 1024 * 1024),
        'save' => (int) env('SHARE_MAX_SAVE_BYTES', 100 * 1024 * 1024),
    ],

    // How many new ids one account may claim per day (new_listings_per_day).
    'new_listings_per_day' => (int) env('SHARE_NEW_LISTINGS_PER_DAY', 5),

    // How long an upload waits to be published before it is discarded (at least 24 hours, rule 15).
    'upload_hours' => 48,

    /*
     * Storage. `disk` holds the archives (archives/...), the data files (releases/, owners/,
     * attestations/ with .sig beside each) and pending uploads (uploads/, never public).
     * `public_url` is the address that disk serves its public files at; it is `data` in hosts.toml,
     * and archives are served under it too. In development the public disk serves both.
     */
    'disk' => env('SHARE_DISK', 'public'),
    'public_url' => rtrim((string) env('SHARE_PUBLIC_URL', rtrim((string) env('APP_URL', 'http://localhost'), '/').'/storage'), '/').'/',

    // The Ed25519 secret key (base64 of libsodium's 64-byte secret key) that signs every data file.
    // `php artisan share:key` makes one. Keep it secret; hosts.toml gets only the public half.
    'signing_key' => env('SHARE_SIGNING_KEY'),

    // Earlier public keys, for hosts.toml's retired_keys after a rotation (rule 24).
    'retired_keys' => array_values(array_filter(explode(',', (string) env('SHARE_RETIRED_KEYS', '')))),

    /*
     * Pull requests. The authored repository the listings go to: a test repository you own until
     * ksamods is registered in hosts.toml (rule 1), then KSAModding/content-index. The bot is a
     * GitHub user used by this host only (rule 4), with a classic token of scope public_repo.
     */
    'authored_repository' => env('SHARE_AUTHORED_REPOSITORY', 'KSAModding/content-index'),
    'bot_token' => env('SHARE_BOT_TOKEN'),
    'bot_id' => (int) env('SHARE_BOT_ID', 0),

    // The policy page (rule 2).
    'abuse_email' => env('SHARE_ABUSE_EMAIL', 'abuse@ksamods.gg'),
    'discord' => env('SHARE_DISCORD_URL'),
    'response_hours' => 72,

];
