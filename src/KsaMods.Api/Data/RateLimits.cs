namespace KsaMods.Api.Data;

/// <summary>
/// The request budgets, held somewhere they can be changed.
///
/// <para>These used to be literals in Program.cs, which meant the one thing you reach for when
/// the site is being hammered was the one thing that needed a deploy. They are now read from
/// configuration at startup and can be lowered at runtime by an admin.</para>
///
/// <para><b>Changing a limit changes the partition key.</b> That indirection is the whole trick.
/// A partitioned limiter builds one limiter per key and caches it, so the factory that would read
/// a new limit is never called again for an address already being counted. Reading a mutable
/// field from inside the factory therefore does nothing for exactly the traffic you are trying to
/// slow down: the busy one, whose partition already exists. Measured, not assumed: fifteen
/// requests against a limit of ten all passed.</para>
///
/// <para>So <see cref="Generation"/> is part of the key. Lowering a limit moves everybody onto
/// fresh partitions built with the new budget, and the abandoned ones fall out of the cache when
/// they go idle. The cost is that everybody's current window resets at that moment, which for a
/// rare and deliberate action is a fair price for the change actually taking effect.</para>
///
/// <para>Written through <see cref="Set"/> rather than by assigning properties, so a bad value
/// cannot be installed and then discovered when the limiter throws on the next request.</para>
/// </summary>
public sealed class RateLimits
{
    /// <summary>Anonymous catalogue reads, per address.</summary>
    public int Reads { get; private set; } = 300;

    /// <summary>Writes, per account where there is one and per address where there is not.</summary>
    public int Writes { get; private set; } = 60;

    /// <summary>Forge deliveries, per sender. A burst of releases is a normal morning.</summary>
    public int Webhooks { get; private set; } = 120;

    /// <summary>The window every budget is counted over.</summary>
    public TimeSpan Window { get; private set; } = TimeSpan.FromMinutes(1);

    /// <summary>When it was last changed, so the admin screen can say whether it is the default.</summary>
    public DateTimeOffset? ChangedAt { get; private set; }

    /// <summary>
    /// Bumped on every accepted change, and mixed into the partition key so a change reaches
    /// addresses that are already being counted. See the note above.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>The partition key for a caller, carrying the generation that built it.</summary>
    public string Key(string caller) => $"{Generation}:{caller}";

    /// <summary>
    /// A floor rather than allowing zero. A limit of nothing locks everybody out including the
    /// admin who set it, and the way back is a redeploy.
    /// </summary>
    public const int Minimum = 5;

    public const int Maximum = 100_000;

    public static RateLimits FromConfiguration(IConfiguration configuration)
    {
        var limits = new RateLimits();

        limits.Set(
            configuration.GetValue("RateLimits:Reads", limits.Reads),
            configuration.GetValue("RateLimits:Writes", limits.Writes),
            configuration.GetValue("RateLimits:Webhooks", limits.Webhooks),
            configuration.GetValue("RateLimits:WindowSeconds", (int)limits.Window.TotalSeconds),
            out _);

        limits.ChangedAt = null;
        return limits;
    }

    /// <summary>Applies a change, or explains why it was refused and leaves the current values alone.</summary>
    public bool Set(int reads, int writes, int webhooks, int windowSeconds, out string? error)
    {
        if (Out(reads) || Out(writes) || Out(webhooks))
        {
            error = $"Every limit must be between {Minimum} and {Maximum}.";
            return false;
        }

        if (windowSeconds is < 1 or > 3600)
        {
            error = "The window must be between 1 second and an hour.";
            return false;
        }

        Reads = reads;
        Writes = writes;
        Webhooks = webhooks;
        Window = TimeSpan.FromSeconds(windowSeconds);
        ChangedAt = DateTimeOffset.UtcNow;
        Generation++;

        error = null;
        return true;

        static bool Out(int value) => value is < Minimum or > Maximum;
    }
}
