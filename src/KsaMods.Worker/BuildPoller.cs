using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using KsaMods.Api.Data;
using KsaMods.Metadata;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KsaMods.Worker;

public sealed record BuildPolicy
{
    /// <summary>
    /// The master server broadcast, the same endpoint the game asks in
    /// <c>VersionInfo.GetServerVersionAsync</c> (RFC 0017). Plain http and a non-standard port
    /// because that is what it serves on.
    /// </summary>
    public string MasterUrl { get; init; } = "http://ksa-master1.rocketwerkz.com:8082/version";

    /// <summary>
    /// The accumulated history, read once at startup. The master server reports exactly one
    /// version - the current production build - and says nothing about anything older, so no
    /// amount of polling reconstructs the past. This file has been accumulating it hourly since
    /// 2026-07-02 (RFC 0017, RFC 0033).
    /// </summary>
    public string BackfillUrl { get; init; } =
        "https://raw.githubusercontent.com/KSAModding/KSA-CKAN-meta/main/builds.json";

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Set false where reaching a third-party repository at startup is not wanted.</summary>
    public bool Backfill { get; init; } = true;
}

/// <summary>
/// Keeps the game build list current.
///
/// <para>The list is not decoration. RFC 0031 lets an author write a compatibility bound as a
/// month, and a month only becomes a revision - the thing that actually orders (RFC 0017) - by
/// looking it up here. With an empty build table every month bound stays unresolved, every release
/// carrying one evaluates as Unknown, and the compatibility work does nothing. That is the state
/// the site shipped in: nothing wrote this table.</para>
///
/// <para>Two sources, because neither is sufficient alone. The master server is the authority but
/// has no memory: it reports the current production build and nothing else, so polling it forward
/// from today never learns about last month. The CKAN-meta file is the memory but is somebody
/// else's file, so it is read once to backfill rather than depended on continuously.</para>
///
/// <para>Append-only in effect: a revision already recorded is never rewritten. The revision is
/// the primary key and it is the game's own commit count, so a second sighting of one is the same
/// build seen twice, not a correction.</para>
/// </summary>
public sealed class BuildPoller(
    Database database,
    IHttpClientFactory clients,
    BuildPolicy policy,
    ILogger<BuildPoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (policy.Backfill)
        {
            try
            {
                await BackfillAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Backfill is a nice-to-have on every run after the first. Failing it must not
                // stop the poll below, which is the part that keeps the list from going stale.
                log.LogWarning(e, "Build backfill failed. Continuing with the master server poll.");
            }
        }

        log.LogInformation("Polling {Url} every {Interval}.", policy.MasterUrl, policy.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Build poll failed. Retrying next round.");
            }

            try
            {
                await Task.Delay(policy.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var client = clients.CreateClient(ForgeFactory.ClientName);
        using var response = await client.GetAsync(policy.MasterUrl, ct);

        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadAsStringAsync(ct)).Trim();

        // The broadcast is a bare version string, but it has been served as a JSON object before,
        // and a poller that breaks on a shape change stops silently: it logs a parse warning
        // forever while the list quietly ages.
        var reported = ExtractVersion(body);

        if (reported is null || !KsaVersion.TryParse(reported, out var version))
        {
            log.LogWarning("Master server answered something that is not a version: {Body}.", Clip(body));
            return;
        }

        // Dated today rather than left null. The broadcast carries no date, and "the day we first
        // saw it announced" is within a poll interval of the truth, which is what a month bound
        // needs. Only on first sight: a build already recorded keeps whatever date it arrived with,
        // since the backfill's date is better than ours.
        var added = await RecordAsync(
            [(version.Revision, version.ToDisplayString(), DateOnly.FromDateTime(DateTime.UtcNow))], ct);

        if (added > 0)
        {
            log.LogInformation("New game build {Version} (revision {Revision}).",
                version.ToDisplayString(), version.Revision);
        }
    }

    private async Task BackfillAsync(CancellationToken ct)
    {
        var client = clients.CreateClient(ForgeFactory.ClientName);
        var body = await client.GetStringAsync(policy.BackfillUrl, ct);

        using var document = JsonDocument.Parse(body);

        var found = new Dictionary<int, (string Version, DateOnly? Date)>();
        Harvest(document.RootElement, found, depth: 0);

        if (found.Count == 0)
        {
            log.LogWarning("Backfill found no game versions in {Url}.", policy.BackfillUrl);
            return;
        }

        var added = await RecordAsync(
            [.. found.Select(e => (e.Key, e.Value.Version, e.Value.Date))], ct);

        log.LogInformation("Backfilled {Added} game builds of {Total} read.", added, found.Count);
    }

    /// <summary>
    /// Pulls every game version out of a JSON document, whatever shape it is in.
    ///
    /// <para>Deliberately structural rather than typed. This is somebody else's file, maintained
    /// for a different tool, and CKAN's own build maps have been an array of objects, an object
    /// keyed by version, and a wrapper around either. A deserialiser bound to one of those turns
    /// the day the shape changes into a silent failure - the list simply stops growing - whereas
    /// "anything that parses as a KSA version is a build" survives all three.</para>
    ///
    /// <para>The revision is taken from the version string rather than any neighbouring field,
    /// because the string is the thing a human can check against the game.</para>
    /// </summary>
    private static void Harvest(
        JsonElement element, Dictionary<int, (string, DateOnly?)> found, int depth)
    {
        // A cheap guard against a pathological document; no real build list nests this far.
        if (depth > 6) return;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                Take(element.GetString(), null);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Harvest(item, found, depth + 1);
                break;

            case JsonValueKind.Object:
                // An object may be one build - a version beside its date - or a map of many. Try
                // it as one first, so the date does not get separated from the version it belongs
                // to, and recurse only if that finds nothing.
                var version = First(element, "version", "build", "name");
                var date = First(element, "date", "released", "released_on", "releaseDate");

                if (version is not null && Take(version, ParseDate(date))) return;

                foreach (var property in element.EnumerateObject())
                {
                    // A map keyed by version: the key carries the build, the value its date.
                    if (KsaVersion.TryParse(property.Name, out _))
                    {
                        Take(property.Name,
                            ParseDate(property.Value.ValueKind == JsonValueKind.String
                                ? property.Value.GetString()
                                : null));
                    }

                    Harvest(property.Value, found, depth + 1);
                }

                break;
        }

        bool Take(string? text, DateOnly? on)
        {
            if (text is null || !KsaVersion.TryParse(text, out var parsed)) return false;

            // First sighting wins, so a later entry with no date cannot blank one that had it.
            if (!found.ContainsKey(parsed.Revision))
            {
                found[parsed.Revision] = (parsed.ToDisplayString(), on);
            }

            return true;
        }
    }

    private static string? First(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Writes builds, ignoring ones already known. Returns how many were new.
    /// </summary>
    private async Task<int> RecordAsync(
        IReadOnlyList<(int Revision, string VersionString, DateOnly? Released)> builds, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.ExecuteAsync("""
            insert into build (revision, version_string, released_on)
            values (@Revision, @VersionString, @Released)
            on conflict (revision) do nothing
            """,
            builds.Select(b => new { b.Revision, b.VersionString, b.Released }));
    }

    private static string? ExtractVersion(string body)
    {
        if (body.Length == 0) return null;
        if (body[0] is not ('{' or '[')) return body;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in (string[])["version", "Version", "build", "Build"])
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var date)
            ? date
            : DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var stamp)
                ? DateOnly.FromDateTime(stamp.UtcDateTime)
                : null;

    private static string Clip(string text) =>
        text.Length <= 120 ? text : text[..120] + "…";
}
