using System.Security.Cryptography;
using System.Text;
using Dapper;
using KsaMods.Api.Data;

namespace KsaMods.Api.Auth;

/// <summary>
/// A caller holding a token. Deliberately not an <see cref="AuthenticatedUser"/>: a session and a
/// token are not interchangeable, and giving them one type is how a token ends up satisfying a
/// write check written for a session.
/// </summary>
public sealed record TokenPrincipal
{
    public required long TokenId { get; init; }
    public required long AccountId { get; init; }
    public required string Handle { get; init; }
    public required string SiteRole { get; init; }

    /// <summary>personal or application. An application token claims no user identity.</summary>
    public required string Kind { get; init; }

    public bool IsApplication => Kind == "application";
}

public sealed record IssuedToken(long Id, string Secret, string Prefix);

/// <summary>
/// API tokens (backend.md §4.1).
///
/// <para><b>Tokens read; sessions write.</b> That is the whole authorisation model here, and it is
/// enforced in one place rather than per endpoint - see <c>UseCurrentUser</c>, which never puts a
/// token into the slot the write paths read from. A rule spread across forty handlers is a rule
/// with thirty-nine chances to be forgotten.</para>
///
/// <para>What a token buys is quota and visibility of your own drafts. What it cannot buy is the
/// power to act as you, which means a credential living in a CI file or a mod manager's config is
/// worth far less to whoever steals it than the account it belongs to.</para>
/// </summary>
public sealed class TokenStore(Database database)
{
    /// <summary>
    /// Distinguishable at a glance and greppable in a leaked file. Secret scanners key on fixed
    /// prefixes, and a token somebody can recognise in a paste is a token that gets revoked.
    /// </summary>
    public const string PersonalPrefix = "ksm_pat_";
    public const string ApplicationPrefix = "ksm_app_";

    /// <summary>Kept in clear so the owner can tell two tokens apart. Useless on its own.</summary>
    private const int VisiblePrefixLength = 12;

    /// <summary>256 bits from a CSPRNG. There is no dictionary against this, only the whole space.</summary>
    private const int SecretBytes = 32;

    public async Task<IssuedToken> IssueAsync(
        long accountId, string name, string kind, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        var body = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var secret = (kind == "application" ? ApplicationPrefix : PersonalPrefix) + body;

        using var connection = await database.OpenAsync(ct);

        var id = await connection.ExecuteScalarAsync<long>("""
            insert into api_token (account_id, kind, name, token_hash, prefix, expires_at)
            values (@accountId, @kind, @name, @hash, @prefix, @expiresAt)
            returning id
            """,
            new
            {
                accountId,
                kind,
                name = name.Trim(),
                hash = Hash(secret),
                prefix = secret[..VisiblePrefixLength],
                expiresAt,
            });

        // Returned once and never again: nothing stores the secret, so this is the only moment it
        // exists outside the caller's hands.
        return new IssuedToken(id, secret, secret[..VisiblePrefixLength]);
    }

    /// <summary>
    /// Resolves a presented secret, or null.
    ///
    /// <para>Null covers every failure - unknown, revoked, expired, and an account that has been
    /// suspended or deleted since the token was made. Suspension has to reach tokens or it is not
    /// suspension: an account locked out of the browser could otherwise keep reading its own
    /// drafts through a credential nobody remembered.</para>
    /// </summary>
    public async Task<TokenPrincipal?> ResolveAsync(string secret, CancellationToken ct)
    {
        if (!LooksLikeToken(secret)) return null;

        using var connection = await database.OpenAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<TokenRow>("""
            select t.id as TokenId, t.account_id as AccountId, t.kind,
                   a.handle::text as Handle, a.site_role as SiteRole,
                   t.last_used_at as LastUsedAt
            from api_token t
            join account a on a.id = t.account_id
            where t.token_hash = @hash
              and t.revoked_at is null
              and (t.expires_at is null or t.expires_at > now())
              and a.suspended_at is null
              and a.deleted_at is null
            """,
            new { hash = Hash(secret) });

        if (row is null) return null;

        // At most once a minute. A polling client would otherwise turn every read into a write,
        // which is the read path paying for an audit column.
        if (row.LastUsedAt is null || DateTime.UtcNow - row.LastUsedAt.Value > TimeSpan.FromMinutes(1))
        {
            await connection.ExecuteAsync(
                "update api_token set last_used_at = now() where id = @id", new { id = row.TokenId });
        }

        return new TokenPrincipal
        {
            TokenId = row.TokenId,
            AccountId = row.AccountId,
            Handle = row.Handle,
            SiteRole = row.SiteRole,
            Kind = row.Kind,
        };
    }

    public async Task<IReadOnlyList<TokenSummary>> ListAsync(long accountId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return (await connection.QueryAsync<TokenSummary>("""
            select id, name, kind, prefix, created_at as CreatedAt,
                   expires_at as ExpiresAt, last_used_at as LastUsedAt
            from api_token
            where account_id = @accountId and revoked_at is null
            order by created_at desc
            """,
            new { accountId })).ToList();
    }

    /// <summary>
    /// Revocation is a timestamp, not a delete. The row is what a later "who was reading our
    /// catalogue" question is answered from, and deleting it answers nothing.
    /// </summary>
    public async Task<bool> RevokeAsync(long accountId, long tokenId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var revoked = await connection.ExecuteAsync("""
            update api_token set revoked_at = now()
            where id = @tokenId and account_id = @accountId and revoked_at is null
            """,
            new { tokenId, accountId });

        return revoked > 0;
    }

    public static bool LooksLikeToken(string? value) =>
        value is not null
        && (value.StartsWith(PersonalPrefix, StringComparison.Ordinal)
            || value.StartsWith(ApplicationPrefix, StringComparison.Ordinal));

    /// <summary>
    /// SHA-256 of the whole presented string, prefix included, so two tokens differing only in
    /// kind can never collide.
    /// </summary>
    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>URL-safe and unpadded, so a token survives a query string, a header and a shell.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record TokenRow
    {
        public long TokenId { get; init; }
        public long AccountId { get; init; }
        public string Kind { get; init; } = "personal";
        public string Handle { get; init; } = "";
        public string SiteRole { get; init; } = "user";
        public DateTime? LastUsedAt { get; init; }
    }
}

public sealed record TokenSummary
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "personal";
    public string Prefix { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
}
