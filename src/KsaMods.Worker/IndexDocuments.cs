using System.Text.Json;
using KsaMods.Metadata;
using Tomlyn;
using Tomlyn.Model;

namespace KsaMods.Worker;

/// <summary>One entry of the index's <c>index-status.toml</c>: the index's own voice about a listing.</summary>
public sealed record IndexStatusEntry(string Id, string State, string? Version, DateTimeOffset? Since, string? Reason);

/// <summary>
/// Reads the community index's files (KSAModding/content-index and content-index-releases).
///
/// <para>Pure: text in, records out. No git, no network, no database - which is what makes
/// "does our reader actually understand their files" a test rather than a deployment.</para>
///
/// <para>The mapping is not hand-written. Their TOML keys and our
/// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> names are already the
/// same strings, down to <c>content-dir</c> and <c>game-path</c>, because both sides implement
/// RFC 0031 and RFC 0035. So a listing is parsed to a table, rendered as JSON and read back
/// through the models we already have. A second hand-written mapper would be a hundred lines whose
/// only job is to drift from the first one.</para>
/// </summary>
public static class IndexDocuments
{
    /// <summary>
    /// Parses one <c>listings/&lt;id&gt;.toml</c>.
    ///
    /// <para>Returns null with a reason rather than throwing. One malformed document upstream must
    /// cost that listing and not the whole sync: the alternative is that a typo in somebody else's
    /// pull request empties this site's catalogue.</para>
    /// </summary>
    public static AuthoredDocument? ParseListing(string toml, out string? error)
    {
        error = null;

        if (!Toml.TryToModel<TomlTable>(toml, out var table, out var diagnostics) || table is null)
        {
            error = diagnostics?.FirstOrDefault()?.Message ?? "not valid TOML";
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<AuthoredDocument>(JsonSerializer.Serialize(table));

            if (document is null || string.IsNullOrWhiteSpace(document.Id))
            {
                error = "no id";
                return null;
            }

            return document;
        }
        catch (JsonException e)
        {
            error = e.Message;
            return null;
        }
        catch (NotSupportedException e)
        {
            // A TOML value with no JSON equivalent. Same treatment: skip the listing, name the
            // reason, keep the catalogue.
            error = e.Message;
            return null;
        }
    }

    /// <summary>
    /// Parses one <c>releases/&lt;id&gt;/&lt;version&gt;.json</c>.
    ///
    /// <para>Straight deserialisation: their stamped file and our <see cref="ReleaseDocument"/> are
    /// the same format, which is the point of both implementing RFC 0031.</para>
    /// </summary>
    public static ReleaseDocument? ParseRelease(string json, out string? error)
    {
        error = null;

        try
        {
            var document = JsonSerializer.Deserialize<ReleaseDocument>(json);

            if (document is null || string.IsNullOrWhiteSpace(document.Id) ||
                string.IsNullOrWhiteSpace(document.Version))
            {
                error = "no id or version";
                return null;
            }

            if (document.Download is null || string.IsNullOrWhiteSpace(document.Download.Url))
            {
                error = "no download url";
                return null;
            }

            return document;
        }
        catch (JsonException e)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>
    /// Parses <c>index-status.toml</c>. An absent or empty file is no entries, not an error.
    /// </summary>
    public static IReadOnlyList<IndexStatusEntry> ParseStatus(string toml)
    {
        if (!Toml.TryToModel<TomlTable>(toml, out var table, out _) || table is null) return [];

        if (!table.TryGetValue("entries", out var raw) || raw is not TomlTableArray and not TomlArray)
        {
            return [];
        }

        var entries = new List<IndexStatusEntry>();

        foreach (var item in raw as IEnumerable<object> ?? [])
        {
            if (item is not TomlTable entry) continue;

            var id = Text(entry, "id");
            var state = Text(entry, "state");

            if (id is null || state is null) continue;

            entries.Add(new IndexStatusEntry(
                id, state, Text(entry, "version"), Stamp(entry, "since"), Text(entry, "reason")));
        }

        return entries;
    }

    /// <summary>
    /// Parses <c>game-versions.json</c> into revision-and-display pairs.
    ///
    /// <para>The file carries version strings and no dates, which is enough: the revision is the
    /// fourth component of the string itself, and it is the only component that orders (RFC 0017).
    /// A month bound resolves from the year and month the string already states, so nothing here
    /// needs a release date and nothing has to be invented when there is not one.</para>
    /// </summary>
    public static IReadOnlyList<(int Revision, string Version)> ParseGameVersions(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("versions", out var versions) ||
                versions.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var parsed = new List<(int, string)>();

            foreach (var element in versions.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String) continue;
                if (!KsaVersion.TryParse(element.GetString(), out var version)) continue;

                parsed.Add((version.Revision, version.ToDisplayString()));
            }

            return parsed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Text(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is string s && s.Length > 0 ? s : null;

    private static DateTimeOffset? Stamp(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is not null
        && DateTimeOffset.TryParse(
            value.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var stamp)
            ? stamp
            : null;
}
