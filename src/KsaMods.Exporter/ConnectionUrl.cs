using Npgsql;

namespace KsaMods.Exporter;

/// <summary>
/// Accepts either form of connection string and returns the one Npgsql understands.
///
/// <para>Every managed database hands you a URL - <c>postgres://user:pass@host:5432/db</c> - and
/// Npgsql does not parse them: it wants libpq's key/value form, and given a URL it reports a
/// missing host, which is a confusing thing to read while looking at a string that plainly
/// contains one.</para>
///
/// <para>It lives here because the API, the worker and the exporter all take the same variable and
/// must agree about it, and this is the lowest assembly all three already share that has any
/// business knowing about Npgsql. A better home would be a data project none of them has yet;
/// two implementations of this would be worse than an odd address for one.</para>
/// </summary>
public static class ConnectionUrl
{
    /// <summary>Npgsql's default is 100 per data source, which is most of a stock Postgres.</summary>
    private const int DefaultPoolSize = 20;

    /// <summary>
    /// libpq keywords Npgsql spells differently.
    ///
    /// <para>A URL is a libpq artifact - it came from psql's documentation, and whoever pasted it
    /// wrote <c>application_name</c> because that is what every other tool takes. Npgsql accepts
    /// most of these already; these are the ones it does not, and rejecting them would mean the
    /// same URL working in psql and failing here.</para>
    /// </summary>
    private static readonly Dictionary<string, string> LibpqAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application_name"] = "Application Name",
        ["connect_timeout"] = "Timeout",
        ["sslcert"] = "SSL Certificate",
        ["sslkey"] = "SSL Key",
        ["sslrootcert"] = "Root Certificate",
        ["target_session_attrs"] = "Target Session Attributes",
    };

    public static string Normalise(string connectionString)
    {
        var trimmed = connectionString.Trim();

        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            // .NET has no default port for this scheme, so an absent one comes back as -1.
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        if (uri.UserInfo is { Length: > 0 } userInfo)
        {
            // A password may contain anything, so it arrives percent-encoded and the split has to
            // stop at the first colon.
            var separator = userInfo.IndexOf(':');

            builder.Username = Uri.UnescapeDataString(
                separator < 0 ? userInfo : userInfo[..separator]);

            if (separator >= 0)
            {
                builder.Password = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
            }
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0) continue;

            var key = Uri.UnescapeDataString(pair[..separator]);
            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);

            try
            {
                builder[LibpqAliases.GetValueOrDefault(key, key)] = value;
            }
            catch (ArgumentException e)
            {
                // Worth naming: Npgsql is not libpq and does not implement all of it, and the raw
                // exception says only that a key was not found.
                throw new ArgumentException(
                    $"'{key}' in the database URL is not something Npgsql understands. "
                    + "Its keywords are mostly libpq's, but not all of them.", e);
            }
        }

        // Only when the URL is silent. A shared database is easy to exhaust, and dropping to
        // Npgsql's default on the way to a URL would be an invisible change of behaviour.
        if (builder.MaxPoolSize == new NpgsqlConnectionStringBuilder().MaxPoolSize)
        {
            builder.MaxPoolSize = DefaultPoolSize;
        }

        return builder.ConnectionString;
    }
}
