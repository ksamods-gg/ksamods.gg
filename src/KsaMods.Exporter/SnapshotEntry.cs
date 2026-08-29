using System.Text.Json.Serialization;
using KsaMods.Metadata;

namespace KsaMods.Exporter;

/// <summary>
/// One entry in the snapshot's <c>listings</c> array (spec/snapshot.md).
///
/// <para>Every field but the id is nullable so a tombstone and a live listing are the same shape
/// with different halves present. The serialiser drops nulls, which is what makes a tombstone come
/// out as just its id and its state - the spec's requirement that it "carries nothing else".</para>
///
/// <para><see cref="Releases"/> is deliberately an empty list rather than null on a live listing
/// whose host has no release yet: absent means tombstone, empty means nothing to install.</para>
/// </summary>
public sealed record SnapshotEntry
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("authored")] public AuthoredDocument? Authored { get; init; }
    [JsonPropertyName("releases")] public IReadOnlyList<ReleaseDocument>? Releases { get; init; }
    [JsonPropertyName("index_status")] public SnapshotStatus? IndexStatus { get; init; }
}

/// <summary>The index's own voice about a listing, which the author cannot write.</summary>
public sealed record SnapshotStatus
{
    /// <summary>delisted, disputed, or retracted.</summary>
    [JsonPropertyName("state")] public required string State { get; init; }

    [JsonPropertyName("since")] public DateTimeOffset? Since { get; init; }

    /// <summary>One sentence, written for the user the client shows it to.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}
