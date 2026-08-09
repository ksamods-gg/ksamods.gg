using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// A fact that skips itself when no test database is configured, so the suite still runs offline.
///
/// <para>Hand-rolled rather than pulling in Xunit.SkippableFact: xunit v2 evaluates
/// <see cref="FactAttribute.Skip"/> at discovery, which is all this needs.</para>
/// </summary>
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public const string Variable = "KSAMODS_TEST_POSTGRES";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(Variable);

    public static bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    public RequiresPostgresFactAttribute()
    {
        if (!Available)
        {
            Skip = $"Set {Variable} to a Postgres connection string to run this.";
        }
    }
}
