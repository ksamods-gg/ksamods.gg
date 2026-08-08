using KsaMods.Api.Data;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Turning a provider's database URL into what Npgsql wants.
///
/// <para>Worth testing because there is no build-time signal here at all: the string arrives from
/// the environment, and a parse that drops the port or mangles a password fails at deploy with a
/// connection error that says nothing about which part went wrong.</para>
/// </summary>
public class ConnectionStringTests
{
    private static NpgsqlConnectionStringBuilder Parse(string value) => new(Database.Normalise(value));

    [Fact]
    public void A_libpq_connection_string_is_left_alone()
    {
        // Both forms have to keep working: the URL is what deployments paste in, and key/value is
        // what a developer types locally.
        const string original = "Host=localhost;Database=ksamods;Username=ksamods;Password=ksamods";

        Assert.Equal(original, Database.Normalise(original));
    }

    [Fact]
    public void A_url_becomes_the_same_connection_by_another_name()
    {
        var parsed = Parse("postgres://someone:secret@db.example.com:6543/ksamods");

        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal(6543, parsed.Port);
        Assert.Equal("ksamods", parsed.Database);
        Assert.Equal("someone", parsed.Username);
        Assert.Equal("secret", parsed.Password);
    }

    [Fact]
    public void Both_url_schemes_are_accepted()
    {
        // postgres:// and postgresql:// are both in the wild, and which one a provider hands you
        // is not a decision anybody makes deliberately.
        Assert.Equal("db.example.com", Parse("postgresql://u:p@db.example.com/ksamods").Host);
        Assert.Equal("db.example.com", Parse("postgres://u:p@db.example.com/ksamods").Host);
    }

    [Fact]
    public void A_url_without_a_port_gets_the_postgres_default()
    {
        // .NET has no default port for this scheme, so an absent one arrives as -1. Passing that
        // through would produce a connection attempt to port -1.
        Assert.Equal(5432, Parse("postgres://u:p@db.example.com/ksamods").Port);
    }

    [Fact]
    public void A_percent_encoded_password_is_decoded()
    {
        // Generated passwords contain @ : / and # often enough that this is the normal case, not
        // an edge one - and each of those would otherwise be read as part of the URL's structure.
        var parsed = Parse("postgres://someone:p%40ss%3Aword%2F1@db.example.com/ksamods");

        Assert.Equal("p@ss:word/1", parsed.Password);
        Assert.Equal("db.example.com", parsed.Host);
    }

    [Fact]
    public void A_password_containing_a_colon_keeps_all_of_it()
    {
        // The split has to stop at the first colon. Splitting on every one would silently truncate.
        Assert.Equal("a:b:c", Parse("postgres://someone:a%3Ab%3Ac@db.example.com/ksamods").Password);
    }

    [Fact]
    public void Query_parameters_are_carried_across()
    {
        // sslmode is the one that matters: a managed database usually requires it, and dropping it
        // turns a working URL into a refused connection.
        var parsed = Parse("postgres://u:p@db.example.com/ksamods?sslmode=require");

        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void The_pool_size_is_capped_unless_the_url_says_otherwise()
    {
        // Npgsql defaults to 100 per data source, which is most of a stock Postgres and all of a
        // small managed one. The compose file used to set this explicitly; moving to a URL must
        // not quietly drop it.
        Assert.Equal(20, Parse("postgres://u:p@db.example.com/ksamods").MaxPoolSize);

        Assert.Equal(50,
            Parse("postgres://u:p@db.example.com/ksamods?Maximum Pool Size=50").MaxPoolSize);
    }

    [Fact]
    public void Libpq_spellings_are_translated_rather_than_refused()
    {
        // The URL came from libpq's world - whoever pasted it wrote the names psql documents. The
        // same string working in psql and failing here would be a maddening thing to debug.
        var parsed = Parse(
            "postgres://u:p@db.example.com/ksamods?application_name=ksamods-api&connect_timeout=10");

        Assert.Equal("ksamods-api", parsed.ApplicationName);
        Assert.Equal(10, parsed.Timeout);
    }

    [Fact]
    public void An_unknown_query_parameter_says_which_one_it_was()
    {
        // Npgsql is not libpq and does not implement all of it. The raw exception says only that a
        // key was missing, which is not enough to act on while reading a deploy log.
        var error = Assert.Throws<ArgumentException>(() =>
            Database.Normalise("postgres://u:p@db.example.com/ksamods?gssencmode=disable"));

        Assert.Contains("gssencmode", error.Message, StringComparison.Ordinal);
    }
}
