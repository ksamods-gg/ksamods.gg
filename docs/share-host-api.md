# ksamods.gg share host API

How a client such as Borea shares a vehicle or a save through ksamods.gg, the first share host of
[RFC 0094](https://github.com/KSAModding/content-manager-design/blob/main/rfcs/0094-share-host.md).
The API follows rules 15 and 16 of
[spec/share-host.md](https://github.com/KSAModding/content-manager-design/blob/main/spec/share-host.md),
so code written against this page should work with any other share host too.

**Status:** live at the address below. Until ksamods is registered in `hosts.toml`, listing pull
requests go to a test repository instead of `KSAModding/content-index`. Everything else behaves as
it will after registration.

## Base URL

```
https://ksamods.gg/api/share/v1/
```

All paths below are relative to it. Once registered, take the base URL from `api` of the host's
table in `hosts.toml`, which the snapshot embeds as `hosts` (RFC 0104), rather than hard-coding it.

## The flow

1. **Sign in** once with the device authorization grant (RFC 8628). The player approves your client
   on ksamods.gg in their browser, and you get a bearer token. You never see a password.
2. **Upload** the archive with its dependency list. The host checks it and answers with the result
   of every check, a proposed version and the listing text to show the player.
3. **Publish** the upload, with the version the player confirmed and, for a new listing, its fields.
   The host stores the release and, when needed, opens the listing pull request on the index.

## 1. Sign in

### Start: `POST oauth/device_authorization`

Form-encoded (`application/x-www-form-urlencoded`).

| Field       | Value                                                                          |
| ----------- | ------------------------------------------------------------------------------ |
| `client_id` | Your client's name, such as `borea`: 1 to 64 letters, digits, `.`, `_` or `-`. |

There is no registration and no client secret: every client is a public client (rule 15), and the
player sees your `client_id` when approving.

```http
POST /api/share/v1/oauth/device_authorization
Content-Type: application/x-www-form-urlencoded

client_id=borea
```

`200`:

```json
{
    "device_code": "def502…",
    "user_code": "BHKMQRST",
    "verification_uri": "https://ksamods.gg/oauth/device",
    "verification_uri_complete": "https://ksamods.gg/oauth/device?user_code=BHKMQRST",
    "expires_in": 600,
    "interval": 5
}
```

Show the player `user_code` and open `verification_uri_complete` in their browser (or show
`verification_uri` for them to type the code into). The code is 8 letters from
`BCDFGHJKLMNPQRSTVWXZ` and lasts 10 minutes. The player signs in to ksamods.gg with GitHub or
Discord if they are not already, checks the code and approves.

Approving also records the player's consent that what they publish goes into the public index,
which keeps its history. The approval page tells them so; your client does not need its own notice.

### Poll: `POST oauth/token`

Form-encoded. Poll every `interval` seconds (5) until the player answers.

| Field         | Value                                          |
| ------------- | ---------------------------------------------- |
| `grant_type`  | `urn:ietf:params:oauth:grant-type:device_code` |
| `device_code` | From the start answer.                         |
| `client_id`   | The same name as at the start.                 |

While waiting, the answer is `400` with RFC 8628's errors:

| `error`                 | Meaning                                             |
| ----------------------- | --------------------------------------------------- |
| `authorization_pending` | The player has not answered yet. Keep polling.      |
| `slow_down`             | You polled too fast. Add 5 seconds to the interval. |
| `access_denied`         | The player denied your client. Stop.                |
| `expired_token`         | The 10 minutes ran out. Start again.                |

When the player approves, `200`:

```json
{
    "token_type": "Bearer",
    "expires_in": 7776000,
    "access_token": "eyJ0eXAi…",
    "refresh_token": "def502…"
}
```

The access token lasts 90 days, the refresh token 180 days. Store both like a password.

### Refresh: `POST oauth/token`

| Field           | Value                        |
| --------------- | ---------------------------- |
| `grant_type`    | `refresh_token`              |
| `refresh_token` | The last one you received.   |
| `client_id`     | The same name as at sign-in. |

The answer has the same shape, with a new refresh token; the old one stops working. When refreshing
fails, sign in again.

Send the access token on every request below:

```http
Authorization: Bearer eyJ0eXAi…
Accept: application/json
```

## 2. Upload: `POST uploads`

`multipart/form-data`.

| Field          | Required | Value                                                                                                    |
| -------------- | -------- | -------------------------------------------------------------------------------------------------------- |
| `archive`      | yes      | The `.zip`, as a file part.                                                                              |
| `dependencies` | no       | RFC 0085's dependency list as TOML, see below. Empty or absent means none.                               |
| `listing`      | no       | The id of an existing listing the player owns, to publish a new version of it. Absent for a new listing. |

### The archive

The folder the game wrote, zipped (RFC 0085). Either the folder itself at the top of the zip, or its
files at the top:

```text
Example Rover/
  meta.toml
  vehicle.xml        (a vehicle; a save has universe.xml)
```

- `meta.toml` must parse, its `name` must follow the game's name rule, and a top-level folder must
  be named exactly after `name`.
- `version` in `meta.toml` must be the full game build, such as `2026.10.10.5554`. It becomes the
  release's `game_min`.
- The game file must be well-formed XML. Exactly one of `vehicle.xml` and `universe.xml`.
- At most 10 MB for a vehicle and 100 MB for a save (`max_bytes` in `hosts.toml`).
- No absolute paths, `..`, backslashes or symlinks.
- No other vehicle or save of the same type may already install into a folder with the same name,
  compared without case. When it does, ask the player to save under another name.

The name rule's test cases are `schemas/save-names.json` in content-index; check names with them
before uploading to save the player a round trip.

### The dependency list

The `[[dependencies]]` tables your client already builds for RFC 0085:

```toml
[[dependencies]]
id = "ExamplePartsMod"
kind = "required"
min = "1.4.0"

[[dependencies]]
id = "BetterWheels"
kind = "recommends"
```

`kind` is `required`, `optional`, `recommends`, `suggests` or `conflict`; `min` and `max` are
optional version bounds, and `any_of` is accepted as in RFC 0031. A listed `id` must be a `mod`; an
id the index does not list is accepted.

### The answer

`201`:

```json
{
    "upload": "01jb2v6k8m3q9x4t7y5z0w1r2s",
    "type": "vehicle",
    "checks": [
        {
            "name": "archive",
            "passed": true,
            "message": "A zip archive with safe paths."
        },
        {
            "name": "root",
            "passed": true,
            "message": "meta.toml and the game file are in the folder Example Rover."
        },
        {
            "name": "type",
            "passed": true,
            "message": "A vehicle, with its vehicle.xml."
        },
        { "name": "size", "passed": true, "message": "The archive is 0.1 MB." },
        { "name": "meta.toml", "passed": true, "message": "meta.toml parses." },
        {
            "name": "name",
            "passed": true,
            "message": "The name \"Example Rover\" follows the game's name rule."
        },
        {
            "name": "version",
            "passed": true,
            "message": "Written by game build 2026.10.10.5554, the lowest build a player needs."
        },
        {
            "name": "game file",
            "passed": true,
            "message": "vehicle.xml is well-formed XML."
        },
        {
            "name": "folder",
            "passed": true,
            "message": "No other vehicle installs as \"Example Rover\"."
        }
    ],
    "version": "1.0.0",
    "listing": "type = \"vehicle\"\n\n[[dependencies]]\nid = \"ExamplePartsMod\"\nkind = \"required\"\nmin = \"1.4.0\"\n",
    "expires": "2026-10-27T14:03:11Z"
}
```

| Field     | Meaning                                                                                                                              |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `upload`  | The id to publish with.                                                                                                              |
| `type`    | `vehicle` or `save`, from the archive.                                                                                               |
| `checks`  | One entry per check, in order. Show the messages. Check names may be added later.                                                    |
| `version` | The proposed version: `1.0.0` for a new listing, otherwise the next minor version.                                                   |
| `listing` | The listing text (TOML) to show the player: the whole document for a new version, and the part the upload decides for a new listing. |
| `expires` | When an unpublished upload is discarded (48 hours).                                                                                  |

## 3. Publish: `POST uploads/{upload}/publish`

JSON.

| Field     | Required | Value                                                                                                                         |
| --------- | -------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `version` | yes      | SemVer with three numbers, such as `1.0.0` or `1.1.0-beta.1`. Never one the listing had before, also one that was taken down. |
| `status`  | no       | `stable` (default), `testing` or `dev`.                                                                                       |

For a **new listing**, also:

| Field         | Required | Value                                                                                                                                                                             |
| ------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`          | yes      | 1 to 64 ASCII letters, digits, `.`, `_` or `-`, starting and ending with a letter or digit, such as `ExampleRover`. It never changes, and it shares one namespace with every mod. |
| `name`        | yes      | The display name.                                                                                                                                                                 |
| `authors`     | yes      | A list of names, or one comma-separated string.                                                                                                                                   |
| `abstract`    | yes      | One sentence, at most 300 characters.                                                                                                                                             |
| `license`     | yes      | An SPDX expression, such as `CC-BY-4.0`.                                                                                                                                          |
| `description` | no       | Longer text.                                                                                                                                                                      |
| `tags`        | no       | A list, or a comma-separated string, of lowercase words joined by `-`, such as `rover`.                                                                                           |

```http
POST /api/share/v1/uploads/01jb2v6k8m3q9x4t7y5z0w1r2s/publish
Content-Type: application/json

{
  "version": "1.0.0",
  "status": "stable",
  "id": "ExampleRover",
  "name": "Example Rover",
  "authors": ["Maxi"],
  "abstract": "A six-wheel rover for the Moon.",
  "license": "CC-BY-4.0",
  "tags": ["rover"]
}
```

`201`:

```json
{
    "id": "ExampleRover",
    "type": "vehicle",
    "version": "1.0.0",
    "pull_request": "https://github.com/KSAModding/content-index/pull/123"
}
```

`pull_request` is the listing pull request when publishing opened or updated one: always for a new
listing, and for a new version only when its dependency list changed. It is `null` for a new
version with the same dependencies, which the index picks up from the release list by itself. It
can also be `null` when GitHub was unreachable; the host then retries in the background, and the
release is published regardless.

Every release is also mirrored to the Internet Archive (rule 19). Its release list entry, and for a
new listing the pull request, are written only once archive.org serves that copy, because an entry
is never edited afterwards. That takes from seconds to a few minutes, so while mirroring is on,
`pull_request` is always `null` here: link the player to the listing's page on ksamods.gg, which
shows the pull request once it is open.

The release is downloadable from ksamods.gg as soon as this answer arrives. The listing appears in
the index once its pull request merges (it merges by itself when its checks pass), and a new version
once the watcher sees it.

The listing's page is `https://ksamods.gg/vehicles/{id}` or `https://ksamods.gg/saves/{id}`.

## Errors

Every error is JSON with a 4xx or 5xx status (rule 16):

```json
{ "error": "invalid_archive", "message": "The name in meta.toml must be …", "checks": [ … ] }
```

Show `message` to the player; it is written for them. For a code you do not know, show `message`.

| `error`             | Status   | When                                                                                                                                   | Extra members              |
| ------------------- | -------- | -------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- |
| `unauthorized`      | 401      | No token, or it expired. Refresh, or sign in again.                                                                                    |                            |
| `invalid_request`   | 400, 422 | A missing or malformed field, such as no `archive`, a dependency list that is not valid TOML, or a listing field that breaks its rule. | `errors` on 422, per field |
| `type_not_accepted` | 415      | The archive's type is not one the host takes.                                                                                          | `checks`                   |
| `too_large`         | 413      | The archive is larger than `max_bytes` for its type.                                                                                   | `checks`                   |
| `invalid_archive`   | 422      | A check failed.                                                                                                                        | `checks`                   |
| `id_taken`          | 409      | The id is in the index, in an open pull request, or on the host, compared without case.                                                |                            |
| `version_taken`     | 409      | The listing already had that version.                                                                                                  |                            |
| `not_owner`         | 403, 404 | The player does not own `listing` (404 when no such listing exists), or the upload belongs to another account.                         |                            |
| `rate_limited`      | 429      | Too many new listings today (5 per account), or too many requests. Wait `retry_after` seconds.                                         | `retry_after`              |
| `upload_expired`    | 410, 404 | The upload is past `expires`, already published, or unknown. Upload again.                                                             |                            |

The sign-in endpoints answer with OAuth's own errors (`error` and `error_description`), as above.

## Limits

| What                  | Limit                                                     |
| --------------------- | --------------------------------------------------------- |
| Sign-in requests      | 30 a minute per IP address (polling every 5 seconds fits) |
| Uploads and publishes | 30 an hour per account                                    |
| New listings          | 5 a day per account (`new_listings_per_day`)              |
| Archive size          | 10 MB for a vehicle, 100 MB for a save                    |
| Unpublished uploads   | Discarded after 48 hours                                  |

## Trying it

Start a sign-in from a terminal:

```sh
curl -X POST https://ksamods.gg/api/share/v1/oauth/device_authorization -d client_id=borea-dev
```

Open `verification_uri_complete`, approve, then fetch the token:

```sh
curl -X POST https://ksamods.gg/api/share/v1/oauth/token \
  -d grant_type=urn:ietf:params:oauth:grant-type:device_code \
  -d client_id=borea-dev -d device_code=<device_code>
```

Upload and publish:

```sh
curl https://ksamods.gg/api/share/v1/uploads \
  -H "Authorization: Bearer <access_token>" -H "Accept: application/json" \
  -F archive=@"Example Rover.zip" \
  -F dependencies=$'[[dependencies]]\nid = "ExamplePartsMod"\nkind = "required"\nmin = "1.4.0"'

curl https://ksamods.gg/api/share/v1/uploads/<upload>/publish \
  -H "Authorization: Bearer <access_token>" -H "Accept: application/json" \
  -H "Content-Type: application/json" \
  -d '{"version":"1.0.0","id":"ExampleRover","name":"Example Rover","authors":["Maxi"],"abstract":"A six-wheel rover for the Moon.","license":"CC-BY-4.0"}'
```

Until registration, published test listings only reach the test repository, so testing does not
touch the real index. Questions and problems go to SafeShows on the KSAModding Discord.
