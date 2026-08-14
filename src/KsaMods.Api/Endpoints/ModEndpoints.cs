using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;
using KsaMods.Forge;
using KsaMods.Metadata;

namespace KsaMods.Api.Endpoints;

internal sealed record ExistingLink
{
    public string ModId { get; init; } = "";
    public DateTime? VerifiedAt { get; init; }
}

internal sealed record PendingLink
{
    public string Provider { get; init; } = "";
    public string RepoFullName { get; init; } = "";

    // Lower case deliberately: Dapper matches case-insensitively, and naming it after the column
    // keeps the one field that is a secret looking like a value rather than a property.
    public string? challenge { get; init; }

    /// <summary>How the claim came to be trusted. Null while unverified.</summary>
    public string? VerifiedBy { get; init; }

    /// <summary>Which attached file to read, when a tag carries more than one.</summary>
    public string? AssetGlob { get; init; }

    public DateTime? VerifiedAt { get; init; }
}

internal sealed record JobStatusRow
{
    public long Id { get; init; }
    public string Kind { get; init; } = "";
    public string State { get; init; } = "";
    public int Attempts { get; init; }
    public string? LastError { get; init; }
    public string? ModId { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CreateModBody(
    string Id, string Name, string Abstract, string License,
    string? Description, string[]? Tags, Dictionary<string, string>? Links,
    string? BannerUrl = null, string? IconUrl = null,
    string? GameMin = null, string? GameMax = null);

/// <summary>
/// Every field optional: a caller sending one field changes one field. No id, because the id is
/// the folder name the game loads and cannot move.
/// </summary>
public sealed record EditModBody(
    string? Name, string? Abstract, string? Description, string? License,
    string[]? Tags, Dictionary<string, string>? Links, string? BannerUrl, string? IconUrl = null,
    bool? HideAuthor = null, string? GameMin = null, string? GameMax = null);

public sealed record ConnectRepoBody(string Provider, string RepoId, string RepoFullName, string? InstallationId, string? AssetGlob);

/// <summary>A person, named the way the site names people. Used for both adding and transferring.</summary>
public sealed record MaintainerBody(string Handle);

public sealed record YankBody(string Reason);

public static class ModEndpoints
{
    public static void MapModEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting("writes");

        api.MapPost("/mods", async (
            CreateModBody body, HttpContext http, ModRepository mods,
            TagVocabulary vocabulary, GameBounds bounds, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            if (!ContentId.TryParse(body.Id, out var id, out var reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["id"] = [Explain(reason)],
                });
            }

            // Checked across mods, modlists and retired modlist aliases in one statement: the
            // namespace is global, so two lookups would leave a race between them.
            if (!await mods.IsIdAvailableAsync(id.Value.Value, ct))
            {
                return Results.Conflict(new
                {
                    error = "id_taken",
                    detail = $"'{body.Id}' is already claimed. Ids are compared case-insensitively across mods and modlists.",
                });
            }

            // The column has the same rule as a check constraint, but a constraint violation
            // surfaces as a 500. Reject it here so the author gets told what is wrong with it.
            if (!string.IsNullOrWhiteSpace(body.BannerUrl) && !IsUsableBannerUrl(body.BannerUrl))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["bannerUrl"] = ["A banner must be an https:// link to an image, under 2048 characters."],
                });
            }

            // Tags come from the site's curated list (RFC 0031 allows free-form strings and notes
            // that a vocabulary can come later; this is that vocabulary). Rejected outright rather
            // than filtered quietly: dropping a tag somebody typed leaves them believing their
            // listing is filed somewhere it is not.
            if (!string.IsNullOrWhiteSpace(body.IconUrl) && !IsUsableBannerUrl(body.IconUrl))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["iconUrl"] = ["An icon must be an https:// link to an image, under 2048 characters."],
                });
            }

            var (tags, refused) = await vocabulary.VetAsync(body.Tags ?? [], ct);

            if (refused.Count > 0) return UnknownTags(refused);

            // Not required to create - a listing exists before its author knows which build they
            // tested on - but required to export, and the same conformance note below says so.
            var (minOk, gameMin, minError) = await bounds.ResolveAsync(body.GameMin, isUpperBound: false, ct);
            if (!minOk) return BadBound("gameMin", minError!);

            var (maxOk, gameMax, maxError) = await bounds.ResolveAsync(body.GameMax, isUpperBound: true, ct);
            if (!maxOk) return BadBound("gameMax", maxError!);

            if (Inverted(gameMin, gameMax)) return BadBound("gameMax", InvertedMessage);

            var links = body.Links ?? [];

            await mods.CreateAsync(new ModRow
            {
                Id = id.Value.Value,
                Type = ContentType.Mod,
                Name = body.Name,
                Abstract = body.Abstract,
                Description = body.Description,
                License = body.License,
                Tags = tags,
                Links = System.Text.Json.JsonSerializer.Serialize(links),
                Status = "active",

                // Created as a draft. A listing is useless until it has a repository and a
                // release, and publishing the empty shell straight into browse means every
                // half-finished idea shows up in search. The author publishes it when it is
                // worth looking at.
                ListingState = "unlisted",
                BannerUrl = string.IsNullOrWhiteSpace(body.BannerUrl) ? null : body.BannerUrl.Trim(),
                IconUrl = string.IsNullOrWhiteSpace(body.IconUrl) ? null : body.IconUrl.Trim(),
                GameMinDisplay = gameMin?.Display,
                GameMinRevision = gameMin?.Revision,
                GameMaxDisplay = gameMax?.Display,
                GameMaxRevision = gameMax?.Revision,
                CreatedBy = user.AccountId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }, user.AccountId, ct);

            // Neither is required to create - a listing exists before its author has a forums
            // thread or knows which build they tested on - but both are required by RFC 0031 for
            // the listing to appear in the exported index, and an author who is not told that
            // finds out by their mod never showing up anywhere.
            var missing = new List<string>();
            if (!links.ContainsKey("forums")) missing.Add("a KSA forums thread under links.forums");
            if (gameMin is null) missing.Add("the oldest game build it works on, as gameMin");

            return Results.Created($"/api/v1/mods/{id.Value.Value}", new
            {
                id = id.Value.Value,
                note = missing.Count == 0
                    ? null
                    : $"Add {string.Join(" and ", missing)}; RFC 0031 requires {(missing.Count == 1 ? "it" : "them")} for this listing to appear in the exported index.",
            });
        });

        // Editing a listing. The id is absent on purpose: it is the folder name the game loads
        // the mod under, so it cannot change without breaking every install of it.
        api.MapPatch("/mods/{id}", async (
            string id, EditModBody body, HttpContext http, ModRepository mods,
            TagVocabulary vocabulary, GameBounds bounds, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.EditModListing)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(body.BannerUrl) && !IsUsableBannerUrl(body.BannerUrl))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["bannerUrl"] = ["A banner must be an https:// link to an image, under 2048 characters."],
                });
            }

            // Only vetted when the caller actually sent tags. Otherwise editing a description on
            // a listing that predates the vocabulary - or one ingested from elsewhere - would fail
            // on tags the author never touched.
            var tags = mod.Tags;

            if (body.Tags is not null)
            {
                var (accepted, refused) = await vocabulary.VetAsync(body.Tags, ct);
                if (refused.Count > 0) return UnknownTags(refused);

                tags = accepted;
            }

            // Absent keeps, empty string clears. Without the second case an author who wrote the
            // wrong bound could correct it but never withdraw it.
            GameBound? gameMin = mod.GameMinDisplay is null
                ? null
                : new GameBound(mod.GameMinDisplay, mod.GameMinRevision);

            GameBound? gameMax = mod.GameMaxDisplay is null
                ? null
                : new GameBound(mod.GameMaxDisplay, mod.GameMaxRevision);

            if (body.GameMin is not null)
            {
                var (ok, resolved, error) = await bounds.ResolveAsync(body.GameMin, isUpperBound: false, ct);
                if (!ok) return BadBound("gameMin", error!);

                gameMin = resolved;
            }

            if (body.GameMax is not null)
            {
                var (ok, resolved, error) = await bounds.ResolveAsync(body.GameMax, isUpperBound: true, ct);
                if (!ok) return BadBound("gameMax", error!);

                gameMax = resolved;
            }

            if (Inverted(gameMin, gameMax)) return BadBound("gameMax", InvertedMessage);

            // Absent fields keep their current value, so a caller sending only one field does not
            // silently blank the rest.
            await mods.UpdateAsync(mod with
            {
                Name = body.Name ?? mod.Name,
                Abstract = body.Abstract ?? mod.Abstract,
                Description = body.Description ?? mod.Description,
                License = body.License ?? mod.License,
                Tags = tags,
                Links = body.Links is null
                    ? mod.Links
                    : System.Text.Json.JsonSerializer.Serialize(body.Links),
                BannerUrl = string.IsNullOrWhiteSpace(body.BannerUrl) ? null : body.BannerUrl.Trim(),
                IconUrl = string.IsNullOrWhiteSpace(body.IconUrl) ? null : body.IconUrl.Trim(),

                GameMinDisplay = gameMin?.Display,
                GameMinRevision = gameMin?.Revision,
                GameMaxDisplay = gameMax?.Display,
                GameMaxRevision = gameMax?.Revision,

                // Nullable on the body so "not sent" and "sent as false" stay different. A plain
                // bool would default to false, and every edit that never mentioned this field
                // would quietly un-hide the author.
                HideAuthor = body.HideAuthor ?? mod.HideAuthor,
            }, ct);

            return Results.NoContent();
        });

        // Publish and unlist are two endpoints rather than a state field on the patch above.
        // "Make this visible to everyone" is a decision, not a property edit, and it carries a
        // different permission.
        api.MapPost("/mods/{id}/publish", (string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
            SetVisibilityAsync(id, "listed", http, mods, ct));

        api.MapPost("/mods/{id}/unlist", (string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
            SetVisibilityAsync(id, "unlisted", http, mods, ct));

        // Delete, but only while nothing points at the listing.
        //
        // The site tells people ids stay resolvable so their modlists do not break, and that has
        // to hold even when an author changes their mind. Once a listing has a release, or
        // anything pins or depends on it, the answer is unlisting instead. A listing created by
        // mistake has none of that, and removing it costs nobody anything.
        api.MapDelete("/mods/{id}", async (
            string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.DeleteMod)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var references = await mods.ReferencesAsync(mod.Id, ct);
            if (!references.IsUnused)
            {
                return Results.Conflict(new
                {
                    error = "mod_in_use",
                    detail = references.Explain(),
                });
            }

            await mods.DeleteAsync(mod.Id, ct);
            return Results.NoContent();
        });

        // Claiming a repository. This does not connect it: it issues a challenge, and the link
        // stays unproven until the claimant publishes that challenge in the repository.
        //
        // The unique constraint alone was making this first-come, which stops two listings
        // fighting over one repository but does nothing about the first claim being a stranger's -
        // and since the repository link is the ownership proof for a listing, that was the whole
        // trust model resting on nobody having tried (§5.2).
        // Who works on this listing. Visible to anyone who can manage it, so the manage screen can
        // show the list without a second permission concept.
        api.MapGet("/mods/{id}/maintainers", async (
            string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.ManageMaintainers)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var people = await mods.MaintainersAsync(mod.Id, ct);

            return Results.Ok(new
            {
                spec_version = 1,
                maintainers = people.Select(p => new
                {
                    handle = p.Handle,
                    display_name = p.DisplayName,
                    avatar_url = p.AvatarUrl,
                    role = p.Role,
                    added_at = p.AddedAt,
                }),
            });
        });

        api.MapPost("/mods/{id}/maintainers", async (
            string id, MaintainerBody body, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.ManageMaintainers)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var account = await mods.FindAccountAsync(body.Handle.Trim(), ct);
            if (account is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["handle"] = [$"Nobody here goes by '{body.Handle.Trim()}'. They need an account before you can add them."],
                });
            }

            // A suspended account cannot act, so adding one would look like it worked and do
            // nothing. Say so instead.
            if (account.SuspendedAt is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["handle"] = ["That account is suspended and can't be added."],
                });
            }

            return await mods.AddMaintainerAsync(mod.Id, account.Id, principal.AccountId, ct)
                ? Results.NoContent()
                : Results.Conflict(new { error = "already_a_maintainer", detail = $"{account.Handle} already works on this listing." });
        });

        api.MapDelete("/mods/{id}/maintainers/{handle}", async (
            string id, string handle, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.ManageMaintainers)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var account = await mods.FindAccountAsync(handle, ct);
            if (account is null) return Results.NotFound();

            // The repository refuses to remove an owner, so a false here means either "not on this
            // listing" or "is the owner". Both answer the same way: transfer first.
            return await mods.RemoveMaintainerAsync(mod.Id, account.Id, ct)
                ? Results.NoContent()
                : Results.Conflict(new
                {
                    error = "cannot_remove",
                    detail = "The owner can't be removed. Transfer the listing to somebody else first.",
                });
        });

        // Changing the author.
        //
        // Owners hand their own listings over; moderators and admins can do it for them, which is
        // how an abandoned listing gets a maintainer who can actually act on it. The old owner
        // stays on as a maintainer rather than being removed.
        api.MapPost("/mods/{id}/owner", async (
            string id, MaintainerBody body, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.TransferModOwnership)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var account = await mods.FindAccountAsync(body.Handle.Trim(), ct);
            if (account is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["handle"] = [$"Nobody here goes by '{body.Handle.Trim()}'."],
                });
            }

            if (account.SuspendedAt is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["handle"] = ["That account is suspended, so it can't take ownership."],
                });
            }

            await mods.TransferOwnershipAsync(mod.Id, account.Id, principal.AccountId, ct);
            return Results.NoContent();
        });

        api.MapPost("/mods/{id}/repo-link", async (
            string id, ConnectRepoBody body, HttpContext http, ModRepository mods,
            Database database, IForgeFactory forges, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();

            // Only the owner. A maintainer who could re-point the repository could quietly take
            // over the listing.
            if (!Permissions.Allows(principal, Capability.ConnectRepository)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            if (!ForgeAllowlist.Allows(body.Provider))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["provider"] = [$"'{body.Provider}' is not a forge this site can read. Supported: "
                                    + string.Join(", ", ForgeAllowlist.ApiHosts.Keys) + "."],
                });
            }

            // Accepts the address bar as readily as owner/repository, and everything downstream
            // sees the normalised form, so nothing else has to know a URL was ever involved.
            if (!RepoName.TryNormalise(body.RepoFullName, out var repoFullName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["repoFullName"] = ["That doesn't look like a repository. Paste its address, or give it as owner/repository."],
                });
            }

            // Ask the forge before writing anything: a typo caught here is a sentence, and caught
            // later is an import job that dies five times and lands in the dead queue.
            ForgeRepository repository;

            try
            {
                repository = await forges.For(body.Provider).GetRepositoryAsync(repoFullName, ct);
            }
            catch (ForgeException e)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["repoFullName"] = [e.Message],
                });
            }

            if (repository.Private)
            {
                // The download link on a listing has to work for everybody, and a private
                // repository's release assets do not.
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["repoFullName"] = ["That repository is private, so nobody could download the releases."],
                });
            }

            using var connection = await database.OpenAsync(ct);

            var existing = await connection.QuerySingleOrDefaultAsync<ExistingLink>("""
                select mod_id as ModId, verified_at as VerifiedAt
                from repo_link where provider = @provider and repo_id = @repoId
                """,
                new { provider = body.Provider, repoId = repository.Id });

            if (existing is not null && !string.Equals(existing.ModId, mod.Id, StringComparison.OrdinalIgnoreCase))
            {
                // A repository already linked elsewhere is a dispute, not an error to work around -
                // but only once that claim is proven. An unproven claim has established nothing,
                // so it does not get to hold the repository hostage.
                if (existing.VerifiedAt is not null)
                {
                    return Results.Conflict(new
                    {
                        error = "repository_already_linked",
                        detail = $"That repository is connected to '{existing.ModId}', and that claim is verified. "
                               + "If you believe this is wrong, report it.",
                    });
                }

                await connection.ExecuteAsync(
                    "delete from repo_link where provider = @provider and repo_id = @repoId",
                    new { provider = body.Provider, repoId = repository.Id });
            }

            var challenge = RepositoryProof.NewChallenge();

            // Is this repository simply on the account they signed in with?
            //
            // If it is, there is nothing to prove and asking them to prove it is busywork: the
            // repository sits in that account's namespace, so the account owns it. Checked here
            // rather than only at the verify step so the usual case never shows a proof screen at
            // all. Anything that does not match falls through to the challenge below exactly as
            // before, which is every organisation repository and every repository belonging to
            // somebody else.
            var connectedSubject = await connection.ExecuteScalarAsync<string?>("""
                select subject from oauth_identity
                where account_id = @accountId and provider = @provider
                """,
                new { accountId = principal.AccountId, provider = body.Provider });

            var ownsIt = RepositoryProof.SatisfiedByOwner(repository, connectedSubject);

            await connection.ExecuteAsync("""
                insert into repo_link (mod_id, provider, installation_id, repo_id, repo_full_name,
                                       linked_by, asset_glob, challenge, verified_at, verified_by)
                values (@modId, @provider, @installationId, @repoId, @repoFullName,
                        @linkedBy, @assetGlob,
                        case when @ownsIt then null else @challenge end,
                        case when @ownsIt then now() end,
                        case when @ownsIt then 'owner' end)
                on conflict (mod_id) do update set
                    provider = excluded.provider,
                    installation_id = excluded.installation_id,
                    repo_id = excluded.repo_id,
                    repo_full_name = excluded.repo_full_name,
                    linked_by = excluded.linked_by,
                    asset_glob = excluded.asset_glob,
                    challenge = excluded.challenge,
                    -- Re-pointing at a different repository drops the proof with it. Carrying it
                    -- over would let a verified link be redirected to somebody else's code.
                    --
                    -- Ownership is the exception, and not a loophole: excluded.verified_by is only
                    -- 'owner' when this very request established that the caller owns the account
                    -- the new repository sits under, so it is a fresh proof about the new target
                    -- rather than a stale one carried across.
                    verified_at = case when excluded.verified_by = 'owner' then excluded.verified_at
                                       when repo_link.repo_id = excluded.repo_id
                                       then repo_link.verified_at end,
                    verified_by = case when excluded.verified_by = 'owner' then excluded.verified_by
                                       when repo_link.repo_id = excluded.repo_id
                                       then repo_link.verified_by end,
                    linked_at = now()
                """,
                new
                {
                    modId = mod.Id,
                    provider = body.Provider,
                    installationId = body.InstallationId,
                    repoId = repository.Id,
                    repoFullName = repository.FullName,
                    linkedBy = principal.AccountId,
                    assetGlob = body.AssetGlob,
                    challenge,
                    ownsIt,
                });

            var verified = await connection.ExecuteScalarAsync<DateTime?>(
                "select verified_at from repo_link where mod_id = @modId", new { modId = mod.Id });

            return Results.Ok(new
            {
                repo_full_name = repository.FullName,
                default_branch = repository.DefaultBranch,
                verified = verified is not null,

                // Told apart from the other routes so the frontend can say why nothing was asked
                // of them, rather than silently showing a verified link and leaving somebody to
                // wonder what they missed.
                verified_by = ownsIt ? "owner" : null,
                owner_login = ownsIt ? repository.OwnerLogin : null,

                // Null once ownership settled it: there is no proof outstanding, and printing a
                // challenge next to a verified link is an instruction to do nothing.
                challenge = ownsIt ? null : challenge,
                file_path = RepositoryProof.FilePath,

                // Two routes to the same proof. The topic is listed first because it asks less of
                // the person: nothing enters git history, and there is no commit to make on a
                // branch that may be protected or reviewed.
                instructions =
                    $"Add '{challenge}' as a topic on {repository.FullName}, or commit a file called "
                  + $"{RepositoryProof.FilePath} to its {repository.DefaultBranch} branch containing that same line. "
                  + "Either one proves you control the repository, which is the thing being checked, and either "
                  + "can be removed once you are verified.",
            });
        });

        // Reads back a link that has been connected but not yet proven.
        //
        // Without this the challenge existed only in the response to the POST that created it, so
        // connecting a repository and then reloading the page lost the instructions: still
        // unverified, nothing on screen saying what to do, and the only route back was pressing
        // Connect again on a repository that was already connected.
        //
        // Owner-only, because the challenge is a secret. Anybody who could read it could publish
        // it in a repository they happen to control and claim somebody else's listing.
        api.MapGet("/mods/{id}/repo-link", async (
            string id, HttpContext http, ModRepository mods, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();

            if (!Permissions.Allows(principal, Capability.ConnectRepository)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            using var connection = await database.OpenAsync(ct);

            var link = await connection.QuerySingleOrDefaultAsync<PendingLink>("""
                select provider, repo_full_name as RepoFullName, challenge,
                       verified_at as VerifiedAt, verified_by as VerifiedBy,
                       asset_glob as AssetGlob
                from repo_link where mod_id = @modId
                """,
                new { modId = mod.Id });

            if (link is null) return Results.NotFound();

            return Results.Ok(new
            {
                repo_full_name = link.RepoFullName,

                // The other two fields the connect form is made of. Without them the form has
                // nothing to reload from, so a saved link came back to an empty Repository box
                // that read as "nothing is connected" - and the obvious response to that is to
                // retype it, which re-issues a challenge and drops a proof that was already good.
                provider = link.Provider,
                asset_glob = link.AssetGlob,
                verified = link.VerifiedAt is not null,

                // Carried so a reload says the same thing the connect response said. Without it a
                // link verified by ownership looks, on the next page load, exactly like one whose
                // proof step went missing.
                verified_by = link.VerifiedBy,
                challenge = link.challenge,
                file_path = RepositoryProof.FilePath,

                // Named on every load, not only after a failed verify, because it is the proof
                // worth setting first: it names the account rather than the listing, so it
                // verifies every repository they connect, here and on the community index both.
                index_topic = await connection.ExecuteScalarAsync<string?>(
                    "select github_login from account where id = @accountId",
                    new { accountId = principal.AccountId }) is { Length: > 0 } githubLogin
                    ? RepositoryProof.IndexTopic(githubLogin)
                    : null,
            });
        });

        api.MapPost("/mods/{id}/repo-link/verify", async (
            string id, HttpContext http, ModRepository mods,
            Database database, IForgeFactory forges, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            // Either the owner proving it, or staff vouching for it. Two different permissions,
            // because they are two different acts: vouching cannot re-point a listing at another
            // repository, which is the takeover risk ConnectRepository exists to prevent.
            if (!Permissions.Allows(principal, Capability.ConnectRepository)
                && !Permissions.Allows(principal, Capability.VouchRepository))
            {
                return ApiResults.Forbidden();
            }

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            using var connection = await database.OpenAsync(ct);

            var link = await connection.QuerySingleOrDefaultAsync<PendingLink>("""
                select provider, repo_full_name as RepoFullName, challenge, verified_at as VerifiedAt
                from repo_link where mod_id = @modId
                """,
                new { modId = mod.Id });

            if (link is null) return Results.NotFound();
            if (link.VerifiedAt is not null) return Results.Ok(new { verified = true });

            // Staff can vouch for a link without the proof.
            //
            // The proof answers "can this person write to that repository", and a moderator can
            // establish that by other means: a forum thread, a conversation, a repository whose
            // owner has plainly abandoned it. Refusing them the ability to act on what they know
            // does not make the site safer, it just makes an unreachable author permanent.
            //
            // Recorded as its own method rather than dressed up as a challenge, so the link says
            // truthfully how it came to be trusted.
            if (principal.IsModerator)
            {
                await connection.ExecuteAsync("""
                    update repo_link
                    set verified_at = now(), verified_by = 'staff', challenge = null
                    where mod_id = @modId
                    """,
                    new { modId = mod.Id });

                return Results.Ok(new
                {
                    verified = true,
                    verified_by = "staff",
                    note = "Verified by staff without the usual proof. This is recorded on the link.",
                });
            }

            // Three ways to prove the same claim, and the caller does not have to say which they
            // used. Ownership is checked first because it needs nothing of them at all, then the
            // topic, which comes back on a request the site makes anyway - so somebody who took
            // either route is verified without the file being fetched.
            //
            // Ownership is here as well as at connect time because links made before this existed
            // are sitting unverified with a challenge nobody has done. Pressing Verify now clears
            // those without anyone having to touch their repository.
            string? method = null;
            string? published = null;

            var connectedSubject = await connection.ExecuteScalarAsync<string?>("""
                select subject from oauth_identity
                where account_id = @accountId and provider = @provider
                """,
                new { accountId = principal.AccountId, provider = link.Provider });

            // Kept in step at every sign-in, so it is the login GitHub knows them by today rather
            // than the one they had when they registered. A rename therefore fails closed - the
            // topic stops matching until they set the new one - which is the safe direction.
            var login = await connection.ExecuteScalarAsync<string?>(
                "select github_login from account where id = @accountId",
                new { accountId = principal.AccountId });

            try
            {
                var forge = forges.For(link.Provider);

                var repository = await forge.GetRepositoryAsync(link.RepoFullName, ct);

                if (RepositoryProof.SatisfiedByOwner(repository, connectedSubject))
                {
                    method = "owner";
                }
                else if (RepositoryProof.SatisfiedByIndexTopic(repository.Topics, login))
                {
                    // The community index's own topic (RFC 0038). Checked before ours because it
                    // is the one an author is likeliest to have already set: it names them rather
                    // than a listing, so one topic covers every repository they will ever claim,
                    // here and on the index both.
                    method = "index-topic";
                }
                else if (RepositoryProof.SatisfiedByTopic(repository.Topics, link.challenge))
                {
                    method = "topic";
                }
                else
                {
                    published = await forge.ReadVerificationFileAsync(
                        link.RepoFullName, RepositoryProof.FilePath, ct);

                    if (RepositoryProof.Satisfies(published, link.challenge))
                    {
                        method = "challenge";
                    }
                    else if (RepositoryProof.SatisfiedByIndexMarker(
                                 await forge.ReadVerificationFileAsync(
                                     link.RepoFullName, RepositoryProof.IndexMarkerPath, ct),
                                 login))
                    {
                        // Last, because it is the only proof that costs a second request, and by
                        // RFC 0033's own reckoning the one an author is least likely to have:
                        // placing it means a commit, which under branch protection is a pull
                        // request in front of a pull request.
                        method = "index-marker";
                    }
                }
            }
            catch (ForgeException e)
            {
                return Results.Json(new { error = "forge_unreachable", detail = e.Message },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            if (method is null)
            {
                return Results.Conflict(new
                {
                    error = "not_verified",
                    detail = published is null
                        ? "No matching topic, and no "
                          + $"{RepositoryProof.FilePath} on the default branch. Any of the proofs works, and a change "
                          + "can take a moment to show up in the forge's API."
                        : $"Neither the topics nor {RepositoryProof.FilePath} carry the challenge for this listing.",
                    challenge = link.challenge,
                    file_path = RepositoryProof.FilePath,

                    // Offered as an alternative to the challenge, not a replacement for it. It is
                    // the community index's proof (RFC 0038), so somebody who sets it here is
                    // setting it once for both, and it stays valid for every future listing they
                    // point at this repository.
                    index_topic = login is null ? null : RepositoryProof.IndexTopic(login),
                });
            }

            await connection.ExecuteAsync("""
                update repo_link
                set verified_at = now(), verified_by = @method, challenge = null
                where mod_id = @modId
                """,
                new { modId = mod.Id, method });

            return Results.Ok(new
            {
                verified = true,
                verified_by = method,

                // Names what they actually did. Telling somebody who added a topic that they can
                // delete a file sends them looking for one that was never there.
                note = method == "topic"
                    ? "Connected. You can remove the topic now, the proof is recorded."
                    : "Connected. You can delete the verification file now, the proof is recorded.",
            });
        });

        api.MapPost("/mods/{id}/releases/import", async (
            string id, HttpContext http, ModRepository mods, Database database,
            JobQueue jobs, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.ImportRelease)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            using var connection = await database.OpenAsync(ct);

            var link = await connection.QuerySingleOrDefaultAsync<PendingLink>("""
                select provider, repo_full_name as RepoFullName, challenge, verified_at as VerifiedAt
                from repo_link where mod_id = @modId
                """,
                new { modId = mod.Id });

            if (link is null)
            {
                return Results.Conflict(new
                {
                    error = "no_repository",
                    detail = "Connect a repository first. Releases are imported from it; the site never holds the file.",
                });
            }

            if (link.VerifiedAt is null)
            {
                // Importing from an unproven link would publish somebody else's release under this
                // listing, which is the exact outcome the proof exists to prevent.
                return Results.Conflict(new
                {
                    error = "repository_not_verified",
                    detail = "That repository connection has not been verified yet.",
                });
            }

            var jobId = await jobs.EnqueueAsync("import_release", new { modId = mod.Id }, ct);

            // Importing fetches, hashes and runs a container per release, so it takes seconds to
            // minutes. The UI polls the job rather than holding a request open.
            return Results.Accepted($"/api/v1/jobs/{jobId}", new { job_id = jobId, state = "queued" });
        });

        // What the Accepted above points at. Scoped to the caller's own mods: job payloads name
        // listings, and the queue is otherwise a list of who is publishing what and when.
        api.MapGet("/jobs/{jobId:long}", async (
            long jobId, HttpContext http, ModRepository mods, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            using var connection = await database.OpenAsync(ct);

            var job = await connection.QuerySingleOrDefaultAsync<JobStatusRow>("""
                select id, kind, state, attempts, last_error as LastError,
                       payload ->> 'modId' as ModId, created_at as CreatedAt
                from job where id = @jobId
                """,
                new { jobId });

            if (job is null) return Results.NotFound();

            var principal = job.ModId is null
                ? null
                : await http.PrincipalForModAsync(mods, job.ModId, ct);

            if (principal is null
                || !(Permissions.Allows(principal, Capability.ImportRelease) || principal.IsModerator))
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                id = job.Id,
                kind = job.Kind,
                state = job.State,
                attempts = job.Attempts,
                // Surfaced to the author on purpose: "the asset was not a zip" is something only
                // they can fix, and hiding it behind a support request helps nobody.
                last_error = job.LastError,
                created_at = job.CreatedAt,
                done = job.State is "done" or "dead",
            });
        });

        api.MapPost("/mods/{id}/releases/{version}/yank", async (
            string id, string version, YankBody body, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.YankRelease)) return ApiResults.Forbidden();

            if (string.IsNullOrWhiteSpace(body.Reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A yank must say why: it is shown to anyone who has this version installed."],
                });
            }

            using var connection = await database.OpenAsync(ct);

            var affected = await connection.ExecuteAsync("""
                update mod_release
                set yanked_at = now(), yanked_reason = @reason
                where mod_id = (select id from mod where id_lower = @modId)
                  and version = @version
                  and yanked_at is null
                """,
                new { modId = id.ToLowerInvariant(), version, reason = body.Reason });

            // A yank is the author's statement about one build - distinct from `deprecated`,
            // which covers the whole listing, and from a moderator delisting.
            return affected == 0 ? Results.NotFound() : Results.NoContent();
        });

        api.MapPatch("/mods/{id}/releases/{version}", async (
            string id, string version, ReleaseFacts proposed, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.AmendRelease)) return ApiResults.Forbidden();

            var releases = await mods.ReleasesAsync(id, ct);
            var release = releases.FirstOrDefault(r =>
                string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));

            if (release is null) return Results.NotFound();

            using var connection = await database.OpenAsync(ct);

            var dependencies = await connection.QueryAsync<DependencyFact>("""
                select dep_id as DepId, kind as Kind, min_version as Min, max_version as Max
                from release_dependency where release_id = @id and dep_id is not null
                """,
                new { id = release.Id });

            var current = new ReleaseFacts
            {
                GameMinRevision = release.GameMinRevision,
                GameMaxRevision = release.GameMaxRevision,
                LoaderMin = release.LoaderMin,
                LoaderMax = release.LoaderMax,
                Dependencies = [.. dependencies],
                Yanked = release.YankedAt is not null,
            };

            // The invariant that makes "immutable" mean something: a release can never become
            // more permissive after publication.
            var rejections = ReleaseAmendment.Check(current, proposed);
            if (rejections.Count > 0)
            {
                return Results.BadRequest(new
                {
                    error = "amendment_widens",
                    detail = "A published release may only ever be narrowed. Publish a new version instead.",
                    rejections = rejections.Select(r => new { field = r.Field, reason = r.Reason }),
                });
            }

            await connection.ExecuteAsync("""
                update mod_release
                set game_min_revision = @GameMinRevision,
                    game_max_revision = @GameMaxRevision,
                    loader_min = @LoaderMin,
                    loader_max = @LoaderMax
                where id = @id
                """,
                new
                {
                    id = release.Id,
                    proposed.GameMinRevision,
                    proposed.GameMaxRevision,
                    proposed.LoaderMin,
                    proposed.LoaderMax,
                });

            return Results.NoContent();
        });
    }

    /// <summary>
    /// Shared by publish and unlist. Only ever moves between 'listed' and 'unlisted', so a
    /// moderator's delisting cannot be cleared by the author it was applied to.
    /// </summary>
    private static async Task<IResult> SetVisibilityAsync(
        string id, string state, HttpContext http, ModRepository mods, CancellationToken ct)
    {
        var principal = await http.PrincipalForModAsync(mods, id, ct);
        if (principal is null) return Results.Unauthorized();
        if (!Permissions.Allows(principal, Capability.SetModVisibility)) return ApiResults.Forbidden();

        var mod = await mods.FindAsync(id, ct);
        if (mod is null) return Results.NotFound();

        if (!await mods.SetListingStateAsync(mod.Id, state, ct))
        {
            return Results.Conflict(new
            {
                error = "listing_locked",
                detail = "This listing has been withdrawn by moderators, so its visibility is not yours to change.",
            });
        }

        return Results.NoContent();
    }

    /// <summary>
    /// A banner is a link to an image the author already hosts, so the only things we can check
    /// are the ones that decide whether a browser will render it at all: https, because the page
    /// is https and mixed content is blocked silently, and a length the column will accept.
    /// </summary>
    /// <summary>
    /// One rejection carrying every tag that could not be used and why.
    ///
    /// <para>All of them at once, rather than the first: an author fixing tags one round-trip at a
    /// time gives up before the vocabulary has taught them anything.</para>
    /// </summary>
    private static IResult UnknownTags(Dictionary<string, string> refused) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["tags"] = [.. refused.Select(r => $"{r.Key}: {r.Value}")],
            },
            detail: "Tags come from the site's list. You can suggest a new one, and an admin decides.");

    private const string InvertedMessage =
        "The newest tested build cannot be older than the oldest one that works.";

    private static IResult BadBound(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>
    /// Only compares where both ends resolved. A month still in progress leaves the upper bound
    /// open, and an open bound has no revision to be inverted against.
    /// </summary>
    private static bool Inverted(GameBound? min, GameBound? max) =>
        min?.Revision is { } lower && max?.Revision is { } upper && upper < lower;

    private static bool IsUsableBannerUrl(string candidate) =>
        candidate.Trim() is { Length: > 0 and <= 2048 } url
        && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps;

    private static string Explain(IdRejection reason) => reason switch
    {
        IdRejection.Empty => "An id is required.",
        IdRejection.Length => "An id must be 1 to 64 characters.",
        IdRejection.Boundary => "An id must start and end with a letter or digit.",
        IdRejection.Charset => "An id may contain only ASCII letters, digits, '.', '-' and '_'.",
        IdRejection.Reserved => "That name is reserved by the game or by Windows.",
        _ => "That id is not valid.",
    };
}
