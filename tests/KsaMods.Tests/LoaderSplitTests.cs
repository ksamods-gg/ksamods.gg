using System.Net;
using System.Text.Json;
using Dapper;
using KsaMods.Api.Data;
using KsaMods.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Keeping loaders out of the mod catalogue.
///
/// <para>A loader is not a mod anybody browses for - it is the thing a mod tells you it needs -
/// and mixed into the same grid it competes with the content the page exists to show. The API
/// has always been able to tell them apart; the risk this guards is the filter quietly going
/// missing again, which fails silently by showing one extra card nobody questions.</para>
/// </summary>
public class LoaderTypeTests
{
    [Fact]
    public void A_listing_knows_whether_it_is_a_loader()
    {
        Assert.True(new ModDetail { Id = "StarMap", Type = "mod-loader" }.IsLoader);
        Assert.False(new ModDetail { Id = "OuterPlanets", Type = "mod" }.IsLoader);

        // The default matters: a listing whose type did not come back should read as a mod rather
        // than announcing itself as infrastructure on a page that is not.
        Assert.False(new ModDetail { Id = "Unknown" }.IsLoader);
    }
}

/// <summary>The same split, over HTTP against a real database.</summary>
public sealed class LoaderSplitTests : IAsyncLifetime
{
    private ApiFactory? _factory;
    private NpgsqlDataSource? _source;
    private Database? _database;

    private readonly List<long> _accounts = [];
    private readonly List<string> _mods = [];

    private Database Db => _database ?? throw new InvalidOperationException("No test database.");

    public Task InitializeAsync()
    {
        if (RequiresPostgresFactAttribute.Available)
        {
            _source = Database.CreateDataSource(RequiresPostgresFactAttribute.ConnectionString!);
            _database = new Database(_source);
            _factory = new ApiFactory(RequiresPostgresFactAttribute.ConnectionString!);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            using var connection = await _database.OpenAsync(CancellationToken.None);

            await connection.ExecuteAsync(
                "delete from mod where id = any(@mods)", new { mods = _mods.ToArray() });
            await connection.ExecuteAsync(
                "delete from account where id = any(@accounts)", new { accounts = _accounts.ToArray() });
        }

        _factory?.Dispose();
        if (_source is not null) await _source.DisposeAsync();
    }

    [RequiresPostgresFact]
    public async Task The_mod_list_leaves_loaders_out()
    {
        var owner = await NewAccountAsync();
        var mod = await NewListingAsync(owner, "mod");
        var loader = await NewListingAsync(owner, "mod-loader");

        using var client = _factory!.CreateClient();

        var mods = await IdsAsync(client, "/api/v1/mods?type=mod&limit=200");

        Assert.Contains(mod, mods);
        Assert.DoesNotContain(loader, mods);
    }

    [RequiresPostgresFact]
    public async Task The_loader_list_leaves_mods_out()
    {
        var owner = await NewAccountAsync();
        var mod = await NewListingAsync(owner, "mod");
        var loader = await NewListingAsync(owner, "mod-loader");

        using var client = _factory!.CreateClient();

        var loaders = await IdsAsync(client, "/api/v1/mods?type=mod-loader&limit=200");

        Assert.Contains(loader, loaders);
        Assert.DoesNotContain(mod, loaders);
    }

    [RequiresPostgresFact]
    public async Task Asking_for_neither_still_returns_both()
    {
        // The filter is the site's choice about its own pages, not a change to what the public API
        // answers. A client reading the catalogue must still see everything in it.
        var owner = await NewAccountAsync();
        var mod = await NewListingAsync(owner, "mod");
        var loader = await NewListingAsync(owner, "mod-loader");

        using var client = _factory!.CreateClient();

        var everything = await IdsAsync(client, "/api/v1/mods?limit=200");

        Assert.Contains(mod, everything);
        Assert.Contains(loader, everything);
    }

    [RequiresPostgresFact]
    public async Task A_loader_is_still_findable_by_name()
    {
        // Splitting the lists must not make a loader unreachable: it is what the "1 loader matches
        // it under Loaders" hint on an empty mod search depends on.
        var owner = await NewAccountAsync();
        var loader = await NewListingAsync(owner, "mod-loader");

        using var client = _factory!.CreateClient();

        var found = await IdsAsync(client, $"/api/v1/mods?type=mod-loader&q={loader}&limit=200");

        Assert.Contains(loader, found);
    }

    private static async Task<List<string>> IdsAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()!)
            .ToList();
    }

    private async Task<long> NewAccountAsync()
    {
        using var connection = await Db.OpenAsync(CancellationToken.None);

        var id = await connection.ExecuteScalarAsync<long>(
            "insert into account (handle, display_name) values (@handle, 'Test') returning id",
            new { handle = $"t{Guid.NewGuid():N}"[..16] });

        _accounts.Add(id);
        return id;
    }

    private async Task<string> NewListingAsync(long owner, string type)
    {
        var id = $"test{type.Replace("-", "")}{Guid.NewGuid():N}"[..24];

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            insert into mod (id, id_lower, type, name, abstract, license, created_by)
            values (@id, lower(@id), @type, @id, 'For tests.', 'MIT', @owner)
            """,
            new { id, type, owner });

        _mods.Add(id);
        return id;
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Database>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
    }
}
