using System.Security.Cryptography;
using System.Text;
using Dapper;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;

namespace KsaMods.Api.Auth;

public sealed record SiteSessionOptions
{
    public string CookieName { get; init; } = "ksamods_session";

    /// <summary>Sliding, so an active user is not logged out mid-edit.</summary>
    public TimeSpan SlidingExpiry { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Hard ceiling regardless of activity.</summary>
    public TimeSpan AbsoluteExpiry { get; init; } = TimeSpan.FromDays(90);
}

public sealed record AuthenticatedUser
{
    public required long AccountId { get; init; }
    public required string Handle { get; init; }
    public required string SiteRole { get; init; }
    public required Guid SessionId { get; init; }
}

/// <summary>
/// Server-side sessions (backend.md §4.1).
///
/// <para>Opaque ids in a cookie, backed by a table, rather than a self-contained token — because
/// revocation has to actually work. A signed token cannot be withdrawn before it expires, and
/// "log out everywhere" and "this account is suspended" both need to take effect now.</para>
///
/// <para>No passwords are stored anywhere. There is nothing here worth the cost of holding one.</para>
/// </summary>
public sealed class SessionStore(Database database, SiteSessionOptions options)
{
    public async Task<(Guid Id, DateTimeOffset Expires)> IssueAsync(
        long accountId, string? userAgent, string? ip, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        var expires = DateTimeOffset.UtcNow.Add(options.SlidingExpiry);

        using var connection = await database.OpenAsync(ct);

        await connection.ExecuteAsync("""
            insert into session (id, account_id, expires_at, user_agent, ip_hash)
            values (@id, @accountId, @expires, @userAgent, @ipHash)
            """,
            new
            {
                id,
                accountId,
                expires,
                userAgent = Truncate(userAgent, 512),
                // Hashed, never stored raw: it is useful for spotting session theft and not
                // useful enough to justify keeping an address log.
                ipHash = ip is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(ip)),
            });

        return (id, expires);
    }

    public async Task<AuthenticatedUser?> ResolveAsync(Guid sessionId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<(long AccountId, string Handle, string SiteRole, DateTimeOffset IssuedAt)?>("""
            select a.id, a.handle, a.site_role, s.issued_at
            from session s
            join account a on a.id = s.account_id
            where s.id = @sessionId
              and s.revoked_at is null
              and s.expires_at > now()
              and a.suspended_at is null
            """,
            new { sessionId });

        if (row is null) return null;

        // The absolute ceiling is enforced on read rather than only at issue, so extending a
        // sliding session can never carry one past it.
        if (DateTimeOffset.UtcNow - row.Value.IssuedAt > options.AbsoluteExpiry)
        {
            await RevokeAsync(sessionId, ct);
            return null;
        }

        return new AuthenticatedUser
        {
            AccountId = row.Value.AccountId,
            Handle = row.Value.Handle,
            SiteRole = row.Value.SiteRole,
            SessionId = sessionId,
        };
    }

    public async Task TouchAsync(Guid sessionId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync("""
            update session set expires_at = now() + @window
            where id = @sessionId and revoked_at is null
            """,
            new { sessionId, window = options.SlidingExpiry });
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(
            "update session set revoked_at = now() where id = @sessionId and revoked_at is null",
            new { sessionId });
    }

    /// <summary>Called on any privilege change — role grant, ownership transfer, suspension.</summary>
    public async Task RevokeAllAsync(long accountId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(
            "update session set revoked_at = now() where account_id = @accountId and revoked_at is null",
            new { accountId });
    }

    private static string? Truncate(string? value, int length) =>
        value is null ? null : value.Length <= length ? value : value[..length];
}

/// <summary>Links an OAuth identity to an account, creating one on first sign-in.</summary>
public sealed class AccountStore(Database database)
{
    public async Task<long> UpsertAsync(
        string provider, string subject, string displayName, string? login, string? avatarUrl,
        CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        var existing = await connection.ExecuteScalarAsync<long?>(
            "select account_id from oauth_identity where provider = @provider and subject = @subject",
            new { provider, subject }, transaction);

        if (existing is { } accountId)
        {
            if (provider == "github")
            {
                await connection.ExecuteAsync(
                    "update account set github_login = @login where id = @accountId",
                    new { login, accountId }, transaction);
            }

            transaction.Commit();
            return accountId;
        }

        // Handles are user-facing and must be unique; derive a candidate and disambiguate rather
        // than failing sign-in on a collision the user did not cause.
        var handle = await UniqueHandleAsync(connection, transaction, login ?? displayName);

        var created = await connection.ExecuteScalarAsync<long>("""
            insert into account (handle, display_name, github_login, avatar_url)
            values (@handle, @displayName, @githubLogin, @avatarUrl)
            returning id
            """,
            new
            {
                handle,
                displayName,
                githubLogin = provider == "github" ? login : null,
                avatarUrl,
            },
            transaction);

        await connection.ExecuteAsync("""
            insert into oauth_identity (account_id, provider, subject) values (@created, @provider, @subject)
            """,
            new { created, provider, subject }, transaction);

        transaction.Commit();
        return created;
    }

    private static async Task<string> UniqueHandleAsync(
        System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, string seed)
    {
        var candidate = Sanitise(seed);

        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var attempt = suffix == 0 ? candidate : $"{candidate}{suffix}";

            var taken = await connection.ExecuteScalarAsync<bool>(
                "select exists (select 1 from account where handle = @attempt)",
                new { attempt }, transaction);

            if (!taken) return attempt;
        }

        return $"user{Guid.NewGuid():N}"[..16];
    }

    private static string Sanitise(string seed)
    {
        var cleaned = new string([.. seed.Where(char.IsAsciiLetterOrDigit)]);
        if (cleaned.Length == 0) cleaned = "user";
        return cleaned.Length > 24 ? cleaned[..24] : cleaned;
    }
}
