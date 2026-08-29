using System.Diagnostics;
using System.Text.Json;
using Dapper;
using KsaMods.Api.Data;
using KsaMods.Metadata;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KsaMods.Worker;

public sealed record IndexSyncPolicy
{
    public string AuthoredRepo { get; init; } = "https://github.com/KSAModding/content-index.git";
    public string GeneratedRepo { get; init; } = "https://github.com/KSAModding/content-index-releases.git";

    /// <summary>
    /// Where the two clones live. Must survive a restart, or every start pays a full clone.
    /// </summary>
    public string WorkingDirectory { get; init; } = "/var/lib/ksamods/index";

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Off by default, so nothing starts pulling a third-party catalogue unasked.</summary>
    public bool Enabled { get; init; }
}

/// <summary>
/// Keeps the catalogue in step with the community index.
///
/// <para>The index is the arbiter of the id namespace (RFC 0033), so this site reads it rather than
/// competing with it. Listings and releases arrive from two git repositories and land in the tables
/// the site already reads, so search, the mod page, collisions, resolve and the API keep working
/// against exactly what they worked against before.</para>
///
/// <para><b>git rather than the snapshot.</b> The spec's client contract is one document at a fixed
/// address with a strong ETag, which is the right thing to consume - and it is a 404 today, because
/// the builder that produces it has not merged. Cloning is the path the spec calls secondary, and
/// it is what exists. When the snapshot ships, this reader is what gets replaced, and the mapping
/// in <see cref="IndexDocuments"/> is not.</para>
///
/// <para><b>Read-only, in both directions.</b> Nothing here writes to those repositories, and
/// nothing here overwrites a listing that was authored on this site: an id held locally is reported
/// as a collision and left alone. Upstream arbitrates the namespace, but it does not get to delete
/// somebody's work here without a person deciding that.</para>
/// </summary>
public sealed class IndexSync(
    Database database,
    IndexSyncPolicy policy,
    ILogger<IndexSync> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!policy.Enabled)
        {
            log.LogInformation("Index sync is off. Set IndexSync__Enabled=true to mirror the community index.");
            return;
        }

        log.LogInformation(
            "Mirroring {Authored} and {Generated} into {Dir} every {Interval}.",
            policy.AuthoredRepo, policy.GeneratedRepo, policy.WorkingDirectory, policy.Interval);

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogError(e, "Index sync failed. The catalogue keeps whatever it last had.");
                await RecordFailureAsync(e.Message, stopping);
            }

            try
            {
                await Task.Delay(policy.Interval, stopping);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(policy.WorkingDirectory);

        var authored = Path.Combine(policy.WorkingDirectory, "content-index");
        var generated = Path.Combine(policy.WorkingDirectory, "content-index-releases");

        var authoredCommit = await PullAsync(policy.AuthoredRepo, authored, ct);
        var generatedCommit = await PullAsync(policy.GeneratedRepo, generated, ct);

        if (authoredCommit is null || generatedCommit is null)
        {
            throw new InvalidOperationException("Could not update one of the index repositories.");
        }

        using var connection = await database.OpenAsync(ct);

        var current = await connection.QuerySingleOrDefaultAsync<(string? Authored, string? Generated)>(
            "select authored_commit as Authored, generated_commit as Generated from index_sync where id");

        if (current.Authored == authoredCommit && current.Generated == generatedCommit)
        {
            log.LogDebug("Index unchanged at {Authored}/{Generated}.", Short(authoredCommit), Short(generatedCommit));
            return;
        }

        var listings = ReadListings(authored);
        var statuses = ReadStatuses(authored);
        var releases = ReadReleases(generated);
        var versions = ReadGameVersions(generated);

        if (listings.Count == 0)
        {
            // An empty listings directory is far more likely to be a broken clone than a community
            // that deleted every mod, and acting on it would empty this site's catalogue.
            throw new InvalidOperationException(
                "The authored index produced no listings. Refusing to apply that to the catalogue.");
        }

        var (written, collisions) = await ApplyAsync(
            connection, listings, statuses, releases, versions, authoredCommit, generatedCommit, ct);

        foreach (var id in collisions)
        {
            log.LogWarning(
                "'{Id}' is listed upstream and also authored here. Left alone; upstream's copy is not shown.", id);
        }

        log.LogInformation(
            "Index synced: {Listings} listing(s), {Releases} release(s), {Builds} game build(s) at {Commit}.",
            written.Listings, written.Releases, versions.Count, Short(authoredCommit));
    }

    // ── reading the working copies ──────────────────────────────────────────────────────────────

    private List<AuthoredDocument> ReadListings(string repo)
    {
        var directory = Path.Combine(repo, "listings");
        var documents = new List<AuthoredDocument>();

        if (!Directory.Exists(directory)) return documents;

        foreach (var file in Directory.EnumerateFiles(directory, "*.toml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var document = IndexDocuments.ParseListing(File.ReadAllText(file), out var error);

            if (document is null)
            {
                log.LogWarning("Skipping {File}: {Error}", Path.GetFileName(file), error);
                continue;
            }

            documents.Add(document);
        }

        return documents;
    }

    private IReadOnlyList<IndexStatusEntry> ReadStatuses(string repo)
    {
        var file = Path.Combine(repo, "index-status.toml");
        return File.Exists(file) ? IndexDocuments.ParseStatus(File.ReadAllText(file)) : [];
    }

    private List<ReleaseDocument> ReadReleases(string repo)
    {
        var directory = Path.Combine(repo, "releases");
        var documents = new List<ReleaseDocument>();

        if (!Directory.Exists(directory)) return documents;

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var document = IndexDocuments.ParseRelease(File.ReadAllText(file), out var error);

            if (document is null)
            {
                log.LogWarning("Skipping {File}: {Error}", Path.GetFileName(file), error);
                continue;
            }

            documents.Add(document);
        }

        return documents;
    }

    private static IReadOnlyList<(int Revision, string Version)> ReadGameVersions(string repo)
    {
        var file = Path.Combine(repo, "game-versions.json");
        return File.Exists(file) ? IndexDocuments.ParseGameVersions(File.ReadAllText(file)) : [];
    }

    // ── writing ─────────────────────────────────────────────────────────────────────────────────

    private async Task<((int Listings, int Releases) Written, List<string> Collisions)> ApplyAsync(
        Npgsql.NpgsqlConnection connection,
        List<AuthoredDocument> listings,
        IReadOnlyList<IndexStatusEntry> statuses,
        List<ReleaseDocument> releases,
        IReadOnlyList<(int Revision, string Version)> versions,
        string authoredCommit,
        string generatedCommit,
        CancellationToken ct)
    {
        var byId = statuses.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var collisions = new List<string>();
        var writtenListings = 0;
        var writtenReleases = 0;

        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Ids authored on this site. Upstream arbitrates the namespace, but taking somebody's
        // listing out from under them is a decision for a person, not for a scheduled job.
        var local = (await connection.QueryAsync<string>(
                "select id from mod where source = 'local'", transaction: transaction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var listing in listings)
        {
            if (local.Contains(listing.Id))
            {
                collisions.Add(listing.Id);
                continue;
            }

            var status = byId.TryGetValue(listing.Id, out var entry) ? entry : null;

            // 'retracted' scopes to a pack version and says nothing about the listing itself.
            var state = status?.State switch
            {
                "delisted" => "delisted",
                "disputed" => "disputed",
                _ => "listed",
            };

            await connection.ExecuteAsync("""
                insert into mod (id, id_lower, type, name, abstract, description, license, tags,
                                 links, status, superseded_by, listing_state, authors, source,
                                 game_min_display, game_min_revision,
                                 game_max_display, game_max_revision,
                                 install, provides, index_status_reason, index_status_since)
                values (@Id, lower(@Id), @Type, @Name, @Abstract, @Description, @License, @Tags,
                        @Links::jsonb, @Status, null, @State, @Authors, 'index',
                        @GameMin, @GameMinRevision, @GameMax, @GameMaxRevision,
                        @Install::jsonb, @Provides::jsonb, @Reason, @Since)
                on conflict (id) do update set
                    type = excluded.type, name = excluded.name, abstract = excluded.abstract,
                    description = excluded.description, license = excluded.license,
                    tags = excluded.tags, links = excluded.links, status = excluded.status,
                    listing_state = excluded.listing_state, authors = excluded.authors,
                    game_min_display = excluded.game_min_display,
                    game_min_revision = excluded.game_min_revision,
                    game_max_display = excluded.game_max_display,
                    game_max_revision = excluded.game_max_revision,
                    install = excluded.install, provides = excluded.provides,
                    index_status_reason = excluded.index_status_reason,
                    index_status_since = excluded.index_status_since,
                    updated_at = now()
                where mod.source = 'index'
                """,
                new
                {
                    listing.Id,
                    listing.Type,
                    listing.Name,
                    listing.Abstract,
                    listing.Description,
                    listing.License,
                    Tags = listing.Tags.ToArray(),
                    Links = JsonSerializer.Serialize(listing.Links),
                    listing.Status,
                    State = state,
                    Authors = listing.Authors.ToArray(),
                    GameMin = listing.Compatibility?.GameMin,
                    GameMinRevision = Revision(listing.Compatibility?.GameMin, versions, upper: false),
                    GameMax = listing.Compatibility?.GameMax,
                    GameMaxRevision = Revision(listing.Compatibility?.GameMax, versions, upper: true),
                    Install = listing.Install is null ? null : JsonSerializer.Serialize(listing.Install),
                    Provides = listing.Provides is null ? null : JsonSerializer.Serialize(listing.Provides),
                    Reason = status?.Reason,
                    Since = status?.Since,
                },
                transaction);

            writtenListings++;
        }

        var known = listings.Select(l => l.Id).Except(collisions, StringComparer.OrdinalIgnoreCase).ToArray();

        // A listing that left the index leaves the catalogue. Deleted rather than tombstoned here,
        // because the index's own tombstone is index-status.toml and a row that is in neither is
        // one the index no longer arbitrates at all.
        await connection.ExecuteAsync(
            "delete from mod where source = 'index' and id <> all(@known)",
            new { known }, transaction);

        foreach (var release in releases.Where(r => known.Contains(r.Id, StringComparer.OrdinalIgnoreCase)))
        {
            if (!SemVer.TryParse(release.Version, out var version)) continue;

            var releaseId = await connection.ExecuteScalarAsync<long>("""
                insert into mod_release (mod_id, version, version_sort, release_status, released_at,
                                         provider, changelog_url, listing_snapshot, source,
                                         game_min_display, game_min_revision,
                                         game_max_display, game_max_revision,
                                         install_root, install_derived, install_size,
                                         validation_state, availability)
                select m.id, @Version, @Sort, @Status, @ReleasedAt,
                       'index', @Changelog, @Snapshot::jsonb, 'index',
                       @GameMin, @GameMinRevision, @GameMax, @GameMaxRevision,
                       @InstallRoot, @InstallDerived, @InstallSize,
                       'passed', 'unverified'
                from mod m where m.id_lower = lower(@ModId) and m.source = 'index'
                on conflict (mod_id, version) do update set
                    release_status = excluded.release_status,
                    game_min_display = excluded.game_min_display,
                    game_min_revision = excluded.game_min_revision,
                    game_max_display = excluded.game_max_display,
                    game_max_revision = excluded.game_max_revision,
                    listing_snapshot = excluded.listing_snapshot,
                    yanked_at = case when @Yanked then coalesce(mod_release.yanked_at, now()) else null end,
                    yanked_reason = @YankedReason
                where mod_release.source = 'index'
                returning id
                """,
                new
                {
                    ModId = release.Id,
                    Version = version.Normalised,
                    Sort = version.ToSortKey(),
                    Status = release.Status,
                    ReleasedAt = release.ReleaseDate,
                    Changelog = release.Changelog,
                    Snapshot = JsonSerializer.Serialize(release.Listing ?? new ListingSnapshot
                    {
                        Name = release.Id,
                        Authors = [],
                        Abstract = "",
                        License = "",
                    }),
                    GameMin = release.GameMin,
                    GameMinRevision = release.GameMinRevision,
                    GameMax = release.GameMax,
                    GameMaxRevision = release.GameMaxRevision,
                    InstallRoot = release.Install?.Root,
                    InstallDerived = release.Install?.Derived,
                    InstallSize = release.InstallSize,
                    Yanked = release.Yanked == true,
                    YankedReason = release.YankedReason,
                },
                transaction);

            if (releaseId == 0) continue;

            // The download is the point of the record. Rewritten rather than inserted-if-absent so
            // an upstream amendment to a mirror or a content type actually lands.
            await connection.ExecuteAsync(
                "delete from release_artifact where release_id = @releaseId and not is_mirror",
                new { releaseId }, transaction);

            await connection.ExecuteAsync("""
                insert into release_artifact (release_id, url, sha256, size, content_type)
                values (@releaseId, @Url, decode(@Sha256, 'hex'), @Size, @ContentType)
                on conflict (release_id, url) do nothing
                """,
                new
                {
                    releaseId,
                    release.Download.Url,
                    Sha256 = release.Download.Sha256.ToLowerInvariant(),
                    release.Download.Size,
                    release.Download.ContentType,
                },
                transaction);

            writtenReleases++;
        }

        if (versions.Count > 0)
        {
            await connection.ExecuteAsync("""
                insert into build (revision, version_string)
                values (@Revision, @Version)
                on conflict (revision) do nothing
                """,
                versions.Select(v => new { v.Revision, v.Version }), transaction);
        }

        await connection.ExecuteAsync("""
            update index_sync
            set authored_commit = @authoredCommit, generated_commit = @generatedCommit,
                synced_at = now(), listings = @listings, releases = @releases, last_error = null
            where id
            """,
            new { authoredCommit, generatedCommit, listings = writtenListings, releases = writtenReleases },
            transaction);

        await transaction.CommitAsync(ct);

        return ((writtenListings, writtenReleases), collisions);
    }

    /// <summary>
    /// Resolves an authored bound to a revision against the index's own build list.
    ///
    /// <para>A full version carries its revision in its fourth component. A month names a range,
    /// and the list has no dates - but it does not need any, because every entry states its own
    /// year and month. A lower bound takes the month's first revision and an upper bound its last,
    /// which is the difference between "from July" and "through July".</para>
    /// </summary>
    private static int? Revision(
        string? bound, IReadOnlyList<(int Revision, string Version)> versions, bool upper)
    {
        if (string.IsNullOrWhiteSpace(bound)) return null;
        if (KsaVersion.TryParse(bound, out var exact)) return exact.Revision;
        if (!KsaVersion.TryParseMonth(bound, out var month)) return null;

        var inMonth = versions
            .Where(v => KsaVersion.TryParse(v.Version, out var parsed)
                     && parsed.Year == month.Year && parsed.Month == month.Month)
            .Select(v => v.Revision)
            .ToList();

        if (inMonth.Count == 0) return null;

        return upper ? inMonth.Max() : inMonth.Min();
    }

    private async Task RecordFailureAsync(string message, CancellationToken ct)
    {
        try
        {
            using var connection = await database.OpenAsync(ct);
            await connection.ExecuteAsync(
                "update index_sync set last_error = @message where id", new { message });
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Could not record the sync failure.");
        }
    }

    // ── git ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clones or updates one repository and returns its HEAD commit, or null if it could not.
    ///
    /// <para>A hard reset rather than a merge: this is a mirror, nothing here ever commits, and a
    /// working copy that somehow diverged should be discarded rather than reconciled.</para>
    /// </summary>
    private async Task<string?> PullAsync(string url, string path, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(path, ".git")))
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);

            if (await GitAsync(ct, null, "clone", "--depth", "1", url, path) is not 0)
            {
                log.LogError("Could not clone {Url}.", url);
                return null;
            }
        }
        else if (await GitAsync(ct, path, "fetch", "--depth", "1", "origin") is not 0
              || await GitAsync(ct, path, "reset", "--hard", "FETCH_HEAD") is not 0)
        {
            log.LogError("Could not update {Path}.", path);
            return null;
        }

        var (code, head) = await GitOutputAsync(ct, path, "rev-parse", "HEAD");
        return code == 0 && head.Length >= 7 ? head.Trim() : null;
    }

    private static async Task<int> GitAsync(CancellationToken ct, string? workingDirectory, params string[] args) =>
        (await GitOutputAsync(ct, workingDirectory, args)).ExitCode;

    private static async Task<(int ExitCode, string Output)> GitOutputAsync(
        CancellationToken ct, string? workingDirectory, params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        try
        {
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return (process.ExitCode, output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return (-1, "");
        }
    }

    private static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;
}
