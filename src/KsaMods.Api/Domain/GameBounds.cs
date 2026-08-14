using Dapper;
using KsaMods.Api.Data;
using KsaMods.Metadata;

namespace KsaMods.Api.Domain;

/// <summary>
/// A compatibility bound as it is stored: what the author wrote, plus the revision it resolves to.
/// </summary>
/// <param name="Display">Exactly what the author typed, kept because it is what a human reads.</param>
/// <param name="Revision">
/// Null when the bound is a month that has not finished, which is a legitimate open state and not
/// a failure. RFC 0017 reads a null lower bound as Unknown, so it is only ever written when it is
/// genuinely known.
/// </param>
public readonly record struct GameBound(string Display, int? Revision);

/// <summary>
/// Resolves what an author writes in <c>game_min</c> and <c>game_max</c> to a revision.
///
/// <para>Two granularities exist, per RFC 0031: a full version such as <c>2026.8.3.5117</c>, which
/// carries its own revision and needs nothing looked up, and a month such as <c>2026.7</c>, which
/// names a range and needs the build list to resolve.</para>
///
/// <para>The two ends of a month resolve differently, and getting this backwards silently changes
/// what a bound means: <c>game_min = "2026.7"</c> means "from the start of July", so it takes the
/// month's <i>first</i> revision, while <c>game_max = "2026.7"</c> means "through the end of July"
/// and takes its <i>last</i>. A month still in progress has no last revision yet - RFC 0033 says
/// so explicitly - so an upper bound naming the current month stays open until the month closes
/// and something re-resolves it. Guessing the newest build we happen to know about would silently
/// declare every later build untested.</para>
/// </summary>
public sealed class GameBounds(Database database)
{
    /// <summary>
    /// Parses and resolves a bound. Returns false with a reason a person can act on, so the
    /// caller can put it beside the field rather than logging it.
    /// </summary>
    public async Task<(bool Ok, GameBound? Bound, string? Error)> ResolveAsync(
        string? value, bool isUpperBound, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return (true, null, null);

        var display = value.Trim();

        if (KsaVersion.TryParse(display, out var version))
        {
            // The revision is the fourth component. No lookup: a build we have never heard of is
            // still a build whose ordering we can read straight off the version the user pasted,
            // which matters because our build list trails the game.
            return (true, new GameBound(version.ToDisplayString(), version.Revision), null);
        }

        if (!KsaVersion.TryParseMonth(display, out var month))
        {
            return (false, null,
                "Write a game version the way the game displays it, such as 2026.8.3.5117, or a whole month, such as 2026.8.");
        }

        var revision = await ResolveMonthAsync(month.Year, month.Month, isUpperBound, ct);

        if (revision is null && !isUpperBound)
        {
            // A lower bound naming a month with no builds is a typo far more often than it is a
            // prediction, and accepting it stores a bound that evaluates as Unknown forever.
            return (false, null, $"No game build has been released in {display} yet.");
        }

        return (true, new GameBound($"{month.Year}.{month.Month}", revision), null);
    }

    private async Task<int?> ResolveMonthAsync(int year, int month, bool isUpperBound, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);

        var start = new DateOnly(year, month, 1);
        var next = start.AddMonths(1);

        if (isUpperBound)
        {
            // Deliberately null while the month is still running: a month is only closed once a
            // build exists after it, and until then its last revision is not knowable.
            var closed = await connection.ExecuteScalarAsync<bool>(
                "select exists (select 1 from build where released_on >= @next)",
                new { next });

            if (!closed) return null;
        }

        var sql = isUpperBound
            ? "select max(revision) from build where released_on >= @start and released_on < @next"
            : "select min(revision) from build where released_on >= @start and released_on < @next";

        return await connection.ExecuteScalarAsync<int?>(sql, new { start, next });
    }
}
