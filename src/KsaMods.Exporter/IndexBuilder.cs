using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KsaMods.Metadata;

namespace KsaMods.Exporter;

/// <summary>
/// Builds the exported index (backend.md §12).
///
/// <para>Pure: takes records, returns file contents. No filesystem, no git, no clock — which is
/// what makes the determinism requirement testable rather than aspirational.</para>
///
/// <para><b>Determinism is a hard requirement, not a nicety.</b> A run that changes nothing must
/// produce byte-identical output, or the mirror's history becomes noise and diffing two days
/// apart tells you nothing. Every collection here is sorted and every timestamp comes from the
/// input rather than the clock.</para>
/// </summary>
public static class IndexBuilder
{
    public const int SpecVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Stable escaping so the same string always produces the same bytes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ExportResult Build(ExportInput input)
    {
        var files = new List<ExportFile>();
        var skipped = new List<(string, string)>();

        var exportable = new List<ExportListing>();

        foreach (var listing in input.Listings.OrderBy(l => l.Id, StringComparer.OrdinalIgnoreCase))
        {
            // Only listed content is exported. An unlisted or delisted listing is deliberately
            // absent rather than exported with a flag: the index is what other clients install
            // from, and offering something the site has withdrawn would defeat the withdrawal.
            if (listing.ListingState != "listed")
            {
                skipped.Add((listing.Id, $"listing state is '{listing.ListingState}'"));
                continue;
            }

            if (Conformance.Check(listing) is { } reason)
            {
                // Exporting a document that does not satisfy the format it claims to implement
                // would make the export worse than useless (§12.2).
                skipped.Add((listing.Id, reason));
                continue;
            }

            exportable.Add(listing);
            files.Add(new ExportFile(
                $"listings/{listing.Id.ToLowerInvariant()}.json",
                Serialise(ToAuthored(listing))));
        }

        var exportableIds = exportable
            .Select(l => l.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var release in input.Releases
                     .Where(r => exportableIds.Contains(r.ModId))
                     .OrderBy(r => r.ModId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => SemVer.Parse(r.Version)))
        {
            files.Add(new ExportFile(
                $"releases/{release.ModId.ToLowerInvariant()}/{release.Version}.json",
                Serialise(ToRelease(release))));
        }

        foreach (var modlist in input.Modlists
                     .Where(m => m.Visibility == "public")
                     .OrderBy(m => m.ModlistId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(m => SemVer.Parse(m.Version)))
        {
            files.Add(new ExportFile(
                $"modlists/{modlist.ModlistId.ToLowerInvariant()}/{modlist.Version}.json",
                Serialise(ToModlist(modlist))));
        }

        if (input.ModlistAliases.Count > 0)
        {
            var aliases = input.ModlistAliases
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            files.Add(new ExportFile("modlist-aliases.json", Serialise(aliases)));
        }

        files.Add(new ExportFile("builds.json", Serialise(
            input.Builds
                .OrderBy(b => b.Revision)
                .Select(b => new { revision = b.Revision, build = b.VersionString, date = b.Released })
                .ToList())));

        // The audit trail has to exist somewhere the service cannot rewrite (§13.4).
        if (input.Moderation.Count > 0)
        {
            files.Add(new ExportFile("moderation-log.json", Serialise(
                input.Moderation
                    .OrderBy(m => m.Id)
                    .Select(m => new
                    {
                        id = m.Id,
                        action = m.Action,
                        subject_kind = m.SubjectKind,
                        subject_id = m.SubjectId,
                        rationale = m.Rationale,
                        supersedes = m.Supersedes,
                        created_at = m.CreatedAt,
                    })
                    .ToList())));
        }

        files.Add(new ExportFile("index.json", Serialise(new
        {
            spec_version = SpecVersion,
            generated_at = input.GeneratedAt,
            listings = exportable.Count,
            releases = files.Count(f => f.Path.StartsWith("releases/", StringComparison.Ordinal)),
            modlists = files.Count(f => f.Path.StartsWith("modlists/", StringComparison.Ordinal)),
        })));

        return new ExportResult
        {
            Files = [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)],
            Skipped = skipped,
        };
    }

    private static string Serialise<T>(T value) =>
        JsonSerializer.Serialize(value, Json).ReplaceLineEndings("\n") + "\n";

    private static AuthoredDocument ToAuthored(ExportListing listing) => new()
    {
        SpecVersion = SpecVersion,
        Id = listing.Id,
        Type = listing.Type,
        Name = listing.Name,
        Authors = [.. listing.Authors],
        Abstract = listing.Abstract,
        Description = listing.Description,
        License = listing.License,
        Tags = [.. listing.Tags.OrderBy(t => t, StringComparer.Ordinal)],
        Status = listing.Status,
        SupersededBy = listing.SupersededBy,
        Links = listing.Links
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        Compatibility = listing.GameMin is null && listing.GameMax is null && listing.Os is null
            ? null
            : new CompatibilityBlock { GameMin = listing.GameMin, GameMax = listing.GameMax, Os = listing.Os },
        Loader = listing.Loader,
        Dependencies = [.. listing.Dependencies.OrderBy(d => d.Id ?? "", StringComparer.OrdinalIgnoreCase)],
    };

    private static ReleaseDocument ToRelease(ExportRelease release) => new()
    {
        SpecVersion = SpecVersion,
        Id = release.ModId,
        Type = ContentType.Mod,
        Version = release.Version,
        Status = release.Status,
        ReleaseDate = release.ReleasedAt,
        GameMin = release.GameMin,
        GameMinRevision = release.GameMinRevision,
        GameMax = release.GameMax,
        GameMaxRevision = release.GameMaxRevision,
        Os = release.Os,
        Download = new DownloadBlock
        {
            Url = release.DownloadUrl,
            Sha256 = release.Sha256,
            Size = release.Size,
            ContentType = release.ContentType,
        },
        InstallSize = release.InstallSize,
        Install = release.InstallRoot is null
            ? null
            : new InstallBlock { Root = release.InstallRoot, Derived = release.InstallDerived },
        Loader = release.Loader,
        Dependencies = [.. release.Dependencies.OrderBy(d => d.Id ?? "", StringComparer.OrdinalIgnoreCase)],
        Changelog = release.ChangelogUrl,
        Listing = release.Listing,
        Yanked = release.Yanked ? true : null,
        YankedReason = release.YankedReason,
    };

    private static ModlistDocument ToModlist(ExportModlistVersion modlist) => new()
    {
        SpecVersion = SpecVersion,
        Id = modlist.ModlistId,
        Type = ContentType.ModPack,
        Name = modlist.Name,
        Authors = [.. modlist.Authors],
        Abstract = modlist.Abstract,
        License = modlist.License,
        Tags = [.. modlist.Tags.OrderBy(t => t, StringComparer.Ordinal)],
        Version = modlist.Version,
        ReleasedAt = modlist.PublishedAt,
        Changelog = modlist.Changelog,
        Compatibility = modlist.GameMin is null && modlist.GameMax is null
            ? null
            : new CompatibilityBlock { GameMin = modlist.GameMin, GameMax = modlist.GameMax },
        Mods = [.. modlist.Mods.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)],
        Vehicles = [.. modlist.Vehicles.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)],
        Saves = [.. modlist.Saves.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)],
    };
}

/// <summary>
/// RFC 0031 conformance, checked before a document leaves the site.
///
/// <para>The site does not gate publishing on the forums link because app installation is a
/// stronger ownership proof (§5.2), but the RFC requires it — so it is required <i>here</i>, and
/// a listing without one is skipped with the reason shown to its maintainers.</para>
/// </summary>
public static class Conformance
{
    public static string? Check(ExportListing listing)
    {
        if (!ContentId.TryParse(listing.Id, out _, out var reason))
        {
            return $"id is not valid under RFC 0031 ({reason})";
        }

        if (!ContentType.IsKnown(listing.Type)) return $"unknown content type '{listing.Type}'";
        if (string.IsNullOrWhiteSpace(listing.Name)) return "name is required";
        if (listing.Authors.Count == 0) return "at least one author is required";
        if (string.IsNullOrWhiteSpace(listing.Abstract)) return "abstract is required";
        if (string.IsNullOrWhiteSpace(listing.License)) return "an SPDX license expression is required";

        if (!listing.Links.TryGetValue("forums", out var forums) || string.IsNullOrWhiteSpace(forums))
        {
            return "links.forums is required by RFC 0031 and is missing";
        }

        if (listing.SupersededBy is not null && listing.Status != "deprecated")
        {
            return "superseded_by is only meaningful with status = deprecated";
        }

        foreach (var dependency in listing.Dependencies)
        {
            if (!DependencyKind.IsKnown(dependency.Kind)) return $"unknown dependency kind '{dependency.Kind}'";

            if (dependency.Id is null && dependency.AnyOf is null or { Count: 0 })
            {
                return "a dependency entry needs either id or any_of";
            }

            // any_of claims a choice, and a choice only exists where the loader will actually
            // start without that specific dependency.
            if (dependency.AnyOf is { Count: > 0 } &&
                dependency.Kind is not (DependencyKind.Required or DependencyKind.Recommends))
            {
                return $"any_of is not valid with kind '{dependency.Kind}'";
            }
        }

        return null;
    }
}

internal static class ExportEncoding
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
}
