using Dapper;
using KsaMods.Metadata;
using KsaMods.Resolver;

namespace KsaMods.Api.Data;

/// <summary>
/// Loads the whole installable catalogue into memory for the resolver.
///
/// <para>Whole-catalogue rather than lazy per-mod queries, because resolution is iterative
/// (constraints are recollected each pass) and a lazy loader would issue the same queries dozens
/// of times per request. The KSA catalogue is hundreds of mods, so this is small; if it stops
/// being small, cache it against the latest index change rather than making the resolver chatty.</para>
/// </summary>
public static class CatalogueLoader
{
    public static async Task<ICatalogue> LoadAsync(Database database, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var releases = await connection.QueryAsync<ReleaseShape>("""
            select r.id as Id, r.mod_id as ModId, r.version as Version,
                   r.game_min_revision as GameMinRevision, r.game_max_revision as GameMaxRevision,
                   r.loader_id as LoaderId, r.loader_min as LoaderMin, r.loader_max as LoaderMax,
                   (r.yanked_at is not null) as Yanked
            from mod_release r
            join mod m on m.id = r.mod_id
            where m.listing_state = 'listed'
              and r.validation_state in ('passed', 'passed_warnings')
            """);

        var dependencies = await connection.QueryAsync<DependencyShape>("""
            select release_id as ReleaseId, dep_id as DepId, group_id as GroupId,
                   kind as Kind, min_version as MinVersion, max_version as MaxVersion
            from release_dependency
            """);

        var assetIds = await connection.QueryAsync<(long ReleaseId, string AssetId)>(
            "select release_id, asset_id from release_asset_id");

        // Which releases override stock content. Core's own ids are the reference set, and a
        // shared id means the mod must load before Core to win the first-wins race.
        var coreOverrides = await connection.QueryAsync<long>("""
            select distinct a.release_id
            from release_asset_id a
            where exists (
                select 1 from release_asset_id core
                join mod_release cr on cr.id = core.release_id
                where cr.mod_id = 'Core' and core.asset_id = a.asset_id
            )
            """);

        var overrideSet = coreOverrides.ToHashSet();

        var dependenciesByRelease = dependencies
            .GroupBy(d => d.ReleaseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var assetsByRelease = assetIds
            .GroupBy(a => a.ReleaseId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AssetId).ToList());

        var catalogue = new InMemoryCatalogue();

        foreach (var release in releases)
        {
            if (!SemVer.TryParse(release.Version, out var version)) continue;

            catalogue.Add(new CatalogueRelease
            {
                ModId = release.ModId,
                Version = version,
                GameMinRevision = release.GameMinRevision,
                GameMaxRevision = release.GameMaxRevision,
                Yanked = release.Yanked,
                OverridesCore = overrideSet.Contains(release.Id),
                Loader = release.LoaderId is null
                    ? null
                    : new LoaderRequirement(release.LoaderId, Bound(release.LoaderMin, release.LoaderMax)),
                AssetIds = assetsByRelease.GetValueOrDefault(release.Id, []),
                Dependencies = BuildDependencies(dependenciesByRelease.GetValueOrDefault(release.Id, [])),
            });
        }

        return catalogue;
    }

    private static IReadOnlyList<ResolvedDependency> BuildDependencies(List<DependencyShape> rows)
    {
        var result = new List<ResolvedDependency>();

        foreach (var single in rows.Where(r => r.GroupId is null && r.DepId is not null))
        {
            result.Add(new ResolvedDependency
            {
                ModId = single.DepId,
                Kind = single.Kind,
                Bound = Bound(single.MinVersion, single.MaxVersion),
            });
        }

        foreach (var group in rows.Where(r => r.GroupId is not null).GroupBy(r => r.GroupId!.Value))
        {
            var members = group
                .Where(m => m.DepId is not null)
                .Select(m => (m.DepId!, Bound(m.MinVersion, m.MaxVersion)))
                .ToList();

            if (members.Count == 0) continue;

            result.Add(new ResolvedDependency
            {
                Kind = group.First().Kind,
                Alternatives = members,
            });
        }

        return result;
    }

    private static VersionBound Bound(string? min, string? max) => new(
        min is not null && SemVer.TryParse(min, out var parsedMin) ? parsedMin : null,
        max is not null && SemVer.TryParse(max, out var parsedMax) ? parsedMax : null);

    private sealed record ReleaseShape
    {
        public required long Id { get; init; }
        public required string ModId { get; init; }
        public required string Version { get; init; }
        public int? GameMinRevision { get; init; }
        public int? GameMaxRevision { get; init; }
        public string? LoaderId { get; init; }
        public string? LoaderMin { get; init; }
        public string? LoaderMax { get; init; }
        public bool Yanked { get; init; }
    }

    private sealed record DependencyShape
    {
        public required long ReleaseId { get; init; }
        public string? DepId { get; init; }
        public int? GroupId { get; init; }
        public required string Kind { get; init; }
        public string? MinVersion { get; init; }
        public string? MaxVersion { get; init; }
    }
}
