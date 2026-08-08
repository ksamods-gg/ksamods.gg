using System.Data;
using Npgsql;

namespace KsaMods.Api.Data;

/// <summary>
/// Connection factory. Everything below it uses Dapper with hand-written SQL: the schema is small,
/// the queries are shaped by the read model rather than by objects, and the indexes in
/// db/migrations exist to serve specific statements that are easier to keep honest when visible.
/// </summary>
public sealed class Database(NpgsqlDataSource source)
{
    public async Task<IDbConnection> OpenAsync(CancellationToken ct) =>
        await source.OpenConnectionAsync(ct);

    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.EnableDynamicJson();
        return builder.Build();
    }
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
    /// Exponential backoff, then <c>dead</c> with an alert. A dead job is an operational signal,
    /// not a silent drop — §16.3 alerts on it.
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
