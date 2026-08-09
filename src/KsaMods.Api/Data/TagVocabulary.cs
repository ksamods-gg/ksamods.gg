using System.Text.RegularExpressions;
using Dapper;

namespace KsaMods.Api.Data;

public sealed record TagRow
{
    public string Slug { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Description { get; init; }
    public string State { get; init; } = "proposed";
    public string? Reason { get; init; }
    public string? ProposerHandle { get; init; }
    public string? ReviewNote { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }

    /// <summary>How many listings currently carry it. What makes a retire decision informed.</summary>
    public int Uses { get; init; }
}

public enum TagProposal
{
    Proposed,

    /// <summary>Already in the vocabulary. Proposing it again is not an error, just a no-op.</summary>
    AlreadyApproved,

    /// <summary>Somebody else already asked and it is waiting on an admin.</summary>
    AlreadyPending,

    /// <summary>Asked before and turned down. The note says why.</summary>
    PreviouslyRejected,
}

/// <summary>
/// The curated tag vocabulary (RFC 0031, backend.md §3).
///
/// <para>RFC 0031 defines <c>tags</c> as free-form lowercase strings and notes that "a curated
/// vocabulary can come later without a format change". This is that vocabulary, and the wording
/// decides where it binds: on <b>what an author may pick on this site</b>, not on what the column
/// or the exported document may contain. The format stays a list of lowercase strings, and a
/// listing ingested from another index keeps whatever tags it arrived with.</para>
///
/// <para>So the check lives here, in front of the write endpoints, rather than in a database
/// constraint. The cost is that unknown tags can exist in the table; the admin panel lists them,
/// which turns that cost into a curation queue instead of a leak.</para>
/// </summary>
public sealed class TagVocabulary(Database database)
{
    /// <summary>
    /// Lowercase ASCII words joined by single hyphens. Matches the database constraint, and is
    /// checked here as well so a bad proposal comes back as a sentence rather than a 23514.
    /// </summary>
    private static readonly Regex Shape = new(
        "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const int MinLength = 2;
    public const int MaxLength = 32;

    /// <summary>
    /// How many tags one listing may carry. Not a storage limit - a listing tagged with twenty
    /// things has said nothing, and every one of them dilutes the filter for everybody else.
    /// </summary>
    public const int MaxPerListing = 8;

    /// <summary>
    /// The RFC says lowercase, so "Parts" and "parts" are the same tag and this is where they
    /// become so. Done before the vocabulary check, or a capitalised tag would look unknown.
    /// </summary>
    public static string Normalise(string tag) => tag.Trim().ToLowerInvariant();

    public static string? Malformed(string slug) =>
        slug.Length < MinLength || slug.Length > MaxLength
            ? $"Tags are {MinLength} to {MaxLength} characters."
            : !Shape.IsMatch(slug)
                ? "Lowercase letters, digits and single hyphens only."
                : null;

    public async Task<IReadOnlyList<TagRow>> ApprovedAsync(CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return (await connection.QueryAsync<TagRow>("""
            select slug, label, description, state
            from tag where state = 'approved'
            order by slug
            """)).ToList();
    }

    /// <summary>
    /// Splits a submitted tag list into what may be stored and what may not.
    ///
    /// <para>Returns the normalised, deduplicated, approved tags, plus every rejected value with a
    /// reason. Partial acceptance is deliberately not offered: silently dropping a tag an author
    /// typed would leave them believing a listing is filed somewhere it is not.</para>
    /// </summary>
    public async Task<(string[] Accepted, Dictionary<string, string> Rejected)> VetAsync(
        IEnumerable<string> tags, CancellationToken ct)
    {
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidates = new List<string>();

        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var slug = Normalise(raw);

            if (Malformed(slug) is { } problem)
            {
                rejected[raw.Trim()] = problem;
                continue;
            }

            if (!candidates.Contains(slug, StringComparer.Ordinal)) candidates.Add(slug);
        }

        if (candidates.Count > MaxPerListing)
        {
            rejected["*"] = $"At most {MaxPerListing} tags. Pick the ones somebody would filter by.";
            return ([], rejected);
        }

        if (candidates.Count == 0) return ([], rejected);

        using var connection = await database.OpenAsync(ct);

        var known = (await connection.QueryAsync<(string Slug, string State)>(
            "select slug, state from tag where slug = any(@candidates)", new { candidates }))
            .ToDictionary(t => t.Slug, t => t.State, StringComparer.Ordinal);

        var accepted = new List<string>();

        foreach (var slug in candidates)
        {
            if (known.TryGetValue(slug, out var state) && state == "approved")
            {
                accepted.Add(slug);
                continue;
            }

            rejected[slug] = state switch
            {
                "proposed" => "Waiting on an admin to approve it.",
                "rejected" => "This tag was reviewed and turned down.",
                _ => "Not a tag on this site yet. You can suggest it.",
            };
        }

        return ([.. accepted], rejected);
    }

    /// <summary>
    /// Records somebody's suggestion. Anyone signed in may ask; only an admin may agree.
    ///
    /// <para>Every outcome other than <see cref="TagProposal.Proposed"/> is a way of saying "this
    /// has been asked already", and each is answered differently on screen - a rejection needs to
    /// carry the reason, or the same tag gets proposed every week.</para>
    /// </summary>
    public async Task<(TagProposal Outcome, string? Note)> ProposeAsync(
        string slug, string? reason, long accountId, CancellationToken ct)
    {
        var (connection, transaction) = await database.BeginTransactionAsync(ct);

        await using (connection)
        await using (transaction)
        {
            var existing = await connection.QuerySingleOrDefaultAsync<(string State, string? Note)?>(
                "select state, review_note from tag where slug = @slug", new { slug }, transaction);

            if (existing is { } found)
            {
                return (found.State switch
                {
                    "approved" => TagProposal.AlreadyApproved,
                    "rejected" => TagProposal.PreviouslyRejected,
                    _ => TagProposal.AlreadyPending,
                }, found.Note);
            }

            await connection.ExecuteAsync("""
                insert into tag (slug, label, reason, proposed_by)
                values (@slug, @slug, @reason, @accountId)
                on conflict (slug) do nothing
                """,
                new { slug, reason, accountId }, transaction);

            await transaction.CommitAsync(ct);
            return (TagProposal.Proposed, null);
        }
    }
}
