using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dapper;
using KsaMods.Api.Data;
using KsaMods.Forge;
using KsaMods.Metadata;
using KsaMods.Validation;
using Microsoft.Extensions.Logging;

namespace KsaMods.Worker;

/// <summary>
/// Turns a connected repository's releases into listings (backend.md §5.3).
///
/// <para>This is the loop the whole service is built around, and the order of its steps is the
/// security model: fetch on the host where DNS and egress can be controlled, then parse inside a
/// container with no network at all. Doing either in the other place gives up the property that
/// makes it worth having.</para>
///
/// <para>Idempotent by version. A release already in the database is skipped, so a redelivered
/// webhook, a retried job and an author pressing the button twice all converge on the same rows.
/// Nothing here ever rewrites a published version: an imported release is immutable, because
/// modlists pin it and installs depend on the bytes matching the hash.</para>
/// </summary>
public sealed class ImportHandler(
    Database database,
    JobQueue queue,
    IForgeFactory forges,
    SafeFetcher fetcher,
    ContainerRunner containers,
    ILogger<ImportHandler> log) : IJobHandler
{
    public string Kind => "import_release";

    /// <summary>
    /// How many releases one job imports before handing the rest to a fresh job.
    ///
    /// <para>A repository connected for the first time can have fifty releases, each a download
    /// and a container run. One job doing all of them holds its lock for an hour and loses
    /// everything if the worker restarts; stopping and requeueing keeps the unit of work small and
    /// the progress durable.</para>
    /// </summary>
    private const int MaxPerRun = 10;

    public async Task HandleAsync(JsonElement payload, CancellationToken ct)
    {
        var modId = payload.TryGetProperty("modId", out var value) ? value.GetString() : null;

        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new InvalidOperationException("import_release payload has no modId.");
        }

        using var connection = await database.OpenAsync(ct);

        var link = await connection.QuerySingleOrDefaultAsync<LinkRow>("""
            select r.mod_id as ModId, r.provider, r.repo_full_name as RepoFullName,
                   r.asset_glob as AssetGlob, r.verified_at as VerifiedAt
            from repo_link r
            where r.mod_id = @modId
            """,
            new { modId });

        if (link is null) throw new InvalidOperationException($"'{modId}' has no connected repository.");

        if (link.VerifiedAt is null)
        {
            // Importing from an unproven link would publish somebody else's release under this
            // listing, which is the exact thing the proof exists to prevent.
            throw new InvalidOperationException(
                $"The repository connected to '{modId}' has not been verified.");
        }

        var snapshot = await connection.QuerySingleOrDefaultAsync<ListingRow>("""
            select m.id, m.type, m.name, m.abstract, m.license, m.tags, m.links::text as Links,
                   m.status, m.superseded_by as SupersededBy,
                   coalesce(
                     (select string_agg(a.display_name, '|' order by mm.role, a.handle)
                      from mod_maintainer mm join account a on a.id = mm.account_id
                      where mm.mod_id = m.id), '') as Authors
            from mod m where m.id = @modId
            """,
            new { modId });

        if (snapshot is null) throw new InvalidOperationException($"'{modId}' is not a listing.");

        var known = (await connection.QueryAsync<string>(
            "select version from mod_release where mod_id = @modId", new { modId }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var releases = await forges.For(link.Provider).ListReleasesAsync(link.RepoFullName, ct);

        // Oldest first, so a partial run leaves a contiguous history rather than a hole. Somebody
        // reading a listing mid-import should see the early versions, not versions 9 and 12.
        var candidates = releases
            .Where(r => !r.Draft)
            .OrderBy(r => r.PublishedAt)
            .ToList();

        var imported = 0;
        var skipped = new List<string>();
        var remaining = 0;

        foreach (var release in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var version = ReleaseTag.ToVersion(release.Tag);

            if (version is null)
            {
                // Not approximated into something plausible: the site orders by version and
                // modlists pin exact ones, so a guess here is wrong in a way nobody can see.
                skipped.Add($"{release.Tag}: not a SemVer tag");
                continue;
            }

            if (known.Contains(version)) continue;

            if (imported >= MaxPerRun)
            {
                remaining++;
                continue;
            }

            if (SelectAsset(release, link.AssetGlob) is not { } asset)
            {
                skipped.Add($"{release.Tag}: no downloadable archive");
                continue;
            }

            await ImportOneAsync(connection, link, snapshot, release, version, asset, ct);
            imported++;
        }

        if (skipped.Count > 0)
        {
            log.LogInformation("{ModId}: skipped {Count} release(s): {Reasons}",
                modId, skipped.Count, string.Join("; ", skipped.Take(10)));
        }

        await connection.ExecuteAsync(
            "update repo_link set last_seen_at = now() where mod_id = @modId", new { modId });

        if (remaining > 0)
        {
            // Progress is already committed; the next job carries on from where this one stopped.
            log.LogInformation("{ModId}: {Remaining} release(s) left, queueing another pass.", modId, remaining);
            await queue.EnqueueAsync(Kind, new { modId }, ct);
        }

        log.LogInformation("{ModId}: imported {Imported} release(s).", modId, imported);
    }

    private async Task ImportOneAsync(
        Npgsql.NpgsqlConnection connection, LinkRow link, ListingRow snapshot,
        ForgeRelease release, string version, ForgeAsset asset, CancellationToken ct)
    {
        log.LogInformation("{ModId} {Version}: fetching {Url}", link.ModId, version, asset.DownloadUrl);

        // Stage 1 and 2, on the host: SSRF is a network problem and this is where the network can
        // be controlled. Every redirect hop is re-checked against the allowlist inside.
        var fetched = await fetcher.FetchAsync(new Uri(asset.DownloadUrl), ct);

        try
        {
            // Stages 3 to 8, in a container with no network, no capabilities and a read-only root.
            var outcome = await containers.RunAsync(fetched.TempFilePath, link.ModId, ct);

            if (outcome.TimedOut)
            {
                throw new InvalidOperationException(
                    $"Validation of {version} timed out. An archive that takes this long is usually a zip bomb.");
            }

            if (outcome.ReportJson is null)
            {
                throw new InvalidOperationException(
                    $"The validator produced no report for {version} (exit {outcome.ExitCode}). {Trim(outcome.Stderr)}");
            }

            var report = JsonSerializer.Deserialize<ValidatorReport>(outcome.ReportJson, ReportJson.Options)
                ?? throw new InvalidOperationException($"The validator's report for {version} was unreadable.");

            if (report.Outcome == "crashed")
            {
                // A crash is a bug in the validator, not a verdict about the mod. Recording it as
                // "failed" would put a black mark on somebody's release for our defect.
                throw new InvalidOperationException($"The validator crashed on {version}: {report.Error}");
            }

            await WriteAsync(connection, link, snapshot, release, version, asset, fetched, report, ct);

            log.LogInformation("{ModId} {Version}: {Outcome}, {Findings} finding(s).",
                link.ModId, version, report.Outcome, report.Findings.Count);
        }
        finally
        {
            // The site does not keep the bytes (§5.3 step 7). Everything worth knowing about them
            // is in the rows just written, and the hash is what proves the link still serves them.
            TryDelete(fetched.TempFilePath);
        }
    }

    private static async Task WriteAsync(
        Npgsql.NpgsqlConnection connection, LinkRow link, ListingRow snapshot,
        ForgeRelease release, string version, ForgeAsset asset, FetchResult fetched,
        ValidatorReport report, CancellationToken ct)
    {
        var facts = report.Facts ?? new ExtractedFacts();
        var parsed = SemVer.Parse(version);

        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Everything about one release lands together. A release row without its findings would
        // show a listing as validated with nothing behind the claim.
        var releaseId = await connection.ExecuteScalarAsync<long>("""
            insert into mod_release (
                mod_id, version, version_sort, release_status, released_at,
                provider, provider_release_id, provider_tag, provider_commit,
                changelog_url, changelog_body,
                install_root, install_derived, install_size,
                listing_snapshot, validation_state, availability, last_verified_at)
            values (
                @modId, @version, @versionSort, @status, @releasedAt,
                @provider, @providerReleaseId, @tag, @commit,
                @changelogUrl, @changelogBody,
                @installRoot, @installDerived, @installSize,
                @snapshot::jsonb, @validationState, 'verified', now())
            on conflict (mod_id, version) do nothing
            returning id
            """,
            new
            {
                modId = link.ModId,
                version,
                versionSort = parsed.ToSortKey(),
                // The author's own signal. A tag GitHub marks prerelease, or a version with a
                // prerelease part, is not something to put in front of everybody as stable.
                status = release.Prerelease || parsed.IsPreRelease ? "testing" : "stable",
                releasedAt = release.PublishedAt,
                provider = link.Provider,
                providerReleaseId = release.Id,
                tag = release.Tag,
                commit = release.Commit,
                changelogUrl = release.HtmlUrl,
                changelogBody = release.Body,
                installRoot = facts.InstallRoot,
                installDerived = facts.InstallRootDerived,
                installSize = facts.UncompressedSize == 0 ? (long?)null : facts.UncompressedSize,
                snapshot = snapshot.ToJson(),
                validationState = report.Outcome,
            },
            transaction);

        // Another worker won the race for this version. Theirs is as good as ours.
        if (releaseId == 0)
        {
            await transaction.RollbackAsync(ct);
            return;
        }

        await connection.ExecuteAsync("""
            insert into release_artifact (release_id, url, asset_id, sha256, size, content_type,
                                          download_count, counted_at)
            values (@releaseId, @url, @assetId, @sha256, @size, @contentType,
                    @downloadCount, case when @downloadCount is null then null else now() end)
            """,
            new
            {
                releaseId,
                // The address recorded is the one the author published, not the one the redirects
                // ended at: asset URLs are signed and expire, and the durable link is the first.
                url = asset.DownloadUrl,
                assetId = asset.Id,
                sha256 = fetched.Sha256,
                size = fetched.Size,
                contentType = fetched.ContentType,

                // Stamped with when it was read, because it is a figure that keeps moving and a
                // count with no date on it invites being read as current when it is months old.
                downloadCount = asset.DownloadCount,
            },
            transaction);

        if (facts.AssetIds.Count > 0)
        {
            await connection.ExecuteAsync(
                "insert into release_asset_id (release_id, asset_id, xml_path) values (@releaseId, @assetId, @xmlPath)",
                facts.AssetIds.Select(a => new { releaseId, assetId = a.Id, xmlPath = a.XmlPath }),
                transaction);
        }

        if (facts.Assemblies.Count > 0)
        {
            await connection.ExecuteAsync("""
                insert into release_assembly (release_id, file_path, assembly_name, assembly_version, is_entry)
                values (@releaseId, @filePath, @assemblyName, @assemblyVersion, @isEntry)
                on conflict (release_id, file_path) do nothing
                """,
                facts.Assemblies.Select(a => new
                {
                    releaseId,
                    filePath = a.FilePath,
                    assemblyName = a.AssemblyName,
                    assemblyVersion = a.AssemblyVersion,
                    isEntry = a.IsEntry,
                }),
                transaction);
        }

        if (facts.ConsoleCommands.Count > 0)
        {
            await connection.ExecuteAsync("""
                insert into release_console (release_id, hook, ordinal, command)
                values (@releaseId, @hook, @ordinal, @command)
                on conflict (release_id, hook, ordinal) do nothing
                """,
                facts.ConsoleCommands.Select(c => new
                {
                    releaseId,
                    hook = c.Hook,
                    ordinal = c.Ordinal,
                    command = c.Command,
                }),
                transaction);
        }

        if (facts.Dependencies.Count > 0)
        {
            // 'derived' because these came out of the archive rather than from the author filling
            // in a form. The distinction is what lets a maintainer correct one without the next
            // import quietly overwriting the correction.
            await connection.ExecuteAsync("""
                insert into release_dependency (release_id, dep_id, kind, source)
                values (@releaseId, @depId, @kind, 'derived')
                """,
                facts.Dependencies.Select(d => new
                {
                    releaseId,
                    depId = d.ModId,
                    kind = d.Optional ? "optional" : "required",
                }),
                transaction);
        }

        if (report.Findings.Count > 0)
        {
            await connection.ExecuteAsync("""
                insert into release_finding (release_id, stage, severity, code, message, path)
                values (@releaseId, @stage, @severity, @code, @message, @path)
                """,
                report.Findings.Select(f => new
                {
                    releaseId,
                    stage = (short)f.Stage,
                    severity = f.Severity.ToString().ToLowerInvariant(),
                    code = f.Code,
                    message = f.Message,
                    path = f.Path,
                }),
                transaction);
        }

        await connection.ExecuteAsync(
            "update mod set updated_at = now() where id = @modId", new { modId = link.ModId }, transaction);

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Picks the archive to import.
    ///
    /// <para>Refuses rather than guesses when a release has several candidates: importing the
    /// wrong one produces a listing that installs the wrong thing, and the author cannot tell
    /// from the outside why. <c>asset_glob</c> on the repository link is how they say which.</para>
    /// </summary>
    internal static ForgeAsset? SelectAsset(ForgeRelease release, string? glob)
    {
        var candidates = release.Assets
            .Where(a => !string.IsNullOrWhiteSpace(a.DownloadUrl))
            .ToList();

        if (!string.IsNullOrWhiteSpace(glob))
        {
            var pattern = GlobToRegex(glob);
            candidates = candidates.Where(a => pattern.IsMatch(a.Name)).ToList();
        }
        else
        {
            candidates = candidates
                .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static Regex GlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Trim()[..Math.Min(text.Trim().Length, 500)];

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Scratch is a tmpfs that goes with the container. A file left behind is untidy, not
            // a reason to fail an import that otherwise worked.
        }
    }

    private sealed record LinkRow
    {
        public string ModId { get; init; } = "";
        public string Provider { get; init; } = "";
        public string RepoFullName { get; init; } = "";
        public string? AssetGlob { get; init; }
        public DateTime? VerifiedAt { get; init; }
    }

    private sealed record ListingRow
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "mod";
        public string Name { get; init; } = "";
        public string Abstract { get; init; } = "";
        public string License { get; init; } = "";
        public string[] Tags { get; init; } = [];
        public string Links { get; init; } = "{}";
        public string Status { get; init; } = "active";
        public string? SupersededBy { get; init; }
        public string Authors { get; init; } = "";

        /// <summary>
        /// The listing as it read when this version shipped (§3, <c>listing_snapshot</c>). Kept
        /// because a release document has to stay reproducible: renaming a mod today must not
        /// rewrite what last year's release said about itself.
        /// </summary>
        public string ToJson() => JsonSerializer.Serialize(new
        {
            id = Id,
            type = Type,
            name = Name,
            @abstract = Abstract,
            license = License,
            tags = Tags,
            status = Status,
            superseded_by = SupersededBy,
            authors = Authors.Split('|', StringSplitOptions.RemoveEmptyEntries),
            links = JsonSerializer.Deserialize<JsonElement>(
                string.IsNullOrWhiteSpace(Links) ? "{}" : Links),
        });
    }
}

/// <summary>The container's side of the contract (KsaMods.Validator.Host).</summary>
internal sealed record ValidatorReport
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("outcome")] public string Outcome { get; init; } = "failed";
    [JsonPropertyName("error")] public string? Error { get; init; }
    // Fully qualified: the API's catalogue loader has a Finding of its own, and picking the wrong
    // one here would deserialize silently into the wrong shape.
    [JsonPropertyName("findings")]
    public IReadOnlyList<Metadata.Finding> Findings { get; init; } = [];
    [JsonPropertyName("facts")] public ExtractedFacts? Facts { get; init; }
}

internal static class ReportJson
{
    /// <summary>
    /// Must match the container's writer exactly. Two copies of a wire format is a bad idea, and
    /// the alternative - a shared assembly for four lines of options - is worse: the worker would
    /// take a dependency on the validator host it is meant to be isolated from.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}
