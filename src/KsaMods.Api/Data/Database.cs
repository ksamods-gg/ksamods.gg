using System.Data;
using KsaMods.Exporter;
using Npgsql;

namespace KsaMods.Api.Data;

/// <summary>
/// Connection factory. Everything below it uses Dapper with hand-written SQL: the schema is small,
/// the queries are shaped by the read model rather than by objects, and the indexes in
/// db/migrations exist to serve specific statements that are easier to keep honest when visible.
/// </summary>
public sealed class Database(NpgsqlDataSource source)
{
    /// <summary>
    /// Returns a connection that is <b>already open</b>. Callers must not call <c>Open()</c> on it.
    ///
    /// <para>Npgsql throws "Connection already open" rather than ignoring a redundant open, and
    /// because only transactional writes bothered to open explicitly, that mistake sat undetected
    /// through every read path until the first person tried to sign in. The return type is
    /// <see cref="NpgsqlConnection"/> rather than <see cref="IDbConnection"/> partly so that
    /// <see cref="BeginTransactionAsync"/> below is reachable without a cast.</para>
    /// </summary>
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct) =>
        await source.OpenConnectionAsync(ct);

    /// <summary>
    /// Opens a connection and starts a transaction on it, so no call site has to remember which
    /// of the two steps the factory already did.
    /// </summary>
    public async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> BeginTransactionAsync(
        CancellationToken ct)
    {
        var connection = await source.OpenConnectionAsync(ct);

        try
        {
            return (connection, await connection.BeginTransactionAsync(ct));
        }
        catch
        {
            // Otherwise a failure between open and begin leaks the connection back to nobody.
            await connection.DisposeAsync();
            throw;
        }
    }

    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(Normalise(connectionString));
        builder.EnableDynamicJson();
        return builder.Build();
    }

    /// <summary>
    /// Accepts either form of connection string and returns the one Npgsql understands.
    ///
    /// <para>Forwards to <see cref="ConnectionUrl"/>, which the exporter and the worker call too.
    /// All three processes take the same variable and have to agree about it, so there is one
    /// implementation and it lives in the lowest assembly they share.</para>
    /// </summary>
    public static string Normalise(string connectionString) =>
        ConnectionUrl.Normalise(connectionString);

}

/// <summary>
/// Postgres-backed job queue (backend.md §11).
///
/// <para><c>FOR UPDATE SKIP LOCKED</c> rather than a broker: at this scale a dedicated queue is a
/// component to operate for no benefit, and the job table gets transactional consistency with
/// the rows the job is about for free.</para>
/// </summary>
public sealed class JobQueue(Database database)
{
    public async Task<long> EnqueueAsync(string kind, object payload, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await Dapper.SqlMapper.ExecuteScalarAsync<long>(connection, """
            insert into job (kind, payload)
            values (@kind, @payload::jsonb)
            returning id
            """,
            new { kind, payload = System.Text.Json.JsonSerializer.Serialize(payload) });
    }

    /// <summary>
    /// Claims one job. Every handler must be idempotent: at-least-once is the only delivery
    /// guarantee worth building on, and a worker can die between doing the work and marking it.
    /// </summary>
    public async Task<ClaimedJob?> ClaimAsync(string workerId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await Dapper.SqlMapper.QuerySingleOrDefaultAsync<ClaimedJob>(connection, """
            update job
            set state = 'running', locked_by = @workerId, locked_at = now(), attempts = attempts + 1
            where id = (
                select id from job
                where state = 'queued' and run_after <= now()
                order by run_after, id
                for update skip locked
                limit 1
            )
            returning id as Id, kind as Kind, payload::text as Payload, attempts as Attempts
            """,
            new { workerId });
    }

    public async Task CompleteAsync(long id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        await Dapper.SqlMapper.ExecuteAsync(connection,
            "update job set state = 'done', locked_by = null where id = @id", new { id });
    }

    /// <summary>
    /// Puts a job back untouched: no attempt spent, no error recorded, available immediately.
    ///
    /// <para>For the case where the job never got a fair run - our own infrastructure failed, not
    /// the work. Spending an attempt there means five infrastructure faults kill a job that was
    /// always fine, and writing <c>last_error</c> shows an author our plumbing on their listing.</para>
    /// </summary>
    public async Task ReleaseAsync(long id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        await Dapper.SqlMapper.ExecuteAsync(connection,
            "update job set state = 'queued', locked_by = null, locked_at = null where id = @id",
            new { id });
    }

    /// <summary>
    /// Exponential backoff, then <c>dead</c> with an alert. A dead job is an operational signal,
    /// not a silent drop - §16.3 alerts on it.
    /// </summary>
    public async Task FailAsync(long id, int attempts, string error, CancellationToken ct)
    {
        const int MaxAttempts = 5;

        using var connection = await database.OpenAsync(ct);

        if (attempts >= MaxAttempts)
        {
            await Dapper.SqlMapper.ExecuteAsync(connection,
                "update job set state = 'dead', last_error = @error, locked_by = null where id = @id",
                new { id, error });
            return;
        }

        var delaySeconds = (int)Math.Pow(4, attempts);

        await Dapper.SqlMapper.ExecuteAsync(connection, """
            update job
            set state = 'queued',
                last_error = @error,
                locked_by = null,
                run_after = now() + make_interval(secs => @delaySeconds)
            where id = @id
            """,
            new { id, error, delaySeconds });
    }
}

public sealed record ClaimedJob
{
    public required long Id { get; init; }
    public required string Kind { get; init; }
    public required string Payload { get; init; }
    public required int Attempts { get; init; }
}
