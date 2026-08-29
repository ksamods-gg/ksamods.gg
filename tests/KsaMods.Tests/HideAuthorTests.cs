using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Hiding the author, which is a privacy promise and therefore worth pinning in tests.
///
/// <para>The failure that matters is not the listing forgetting to hide a name. It is the second
/// place the name appears: the author's own public profile lists what they own, so a listing that
/// hides the byline while still appearing there has hidden nothing, and the profile is the
/// shorter path between the two. That is what most of this file is about.</para>
///
/// <para>The other half is that hiding is a display rule and not an ownership change. Losing the
/// maintainer row would take the listing away from its author and leave moderators with nobody to
/// send a takedown to, which is a worse outcome than the one being avoided.</para>
/// </summary>
public sealed class HideAuthorTests : IAsyncLifetime
{
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
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_database is not null && (_accounts.Count > 0 || _mods.Count > 0))
        {
            using var connection = await _database.OpenAsync(CancellationToken.None);

            await connection.ExecuteAsync(
                "delete from mod where id = any(@ids)", new { ids = _mods.ToArray() });
            await connection.ExecuteAsync(
                "delete from account where id = any(@ids)", new { ids = _accounts.ToArray() });
        }

        if (_source is not null) await _source.DisposeAsync();
    }

    private async Task<long> NewAccountAsync()
    {
        var accounts = new AccountStore(Db);
        var id = await accounts.UpsertAsync(
            "github", $"acct-{Guid.NewGuid():N}", "Test Person",
            $"person{Guid.NewGuid():N}"[..12], null, default);

        _accounts.Add(id);
        return id;
    }

    private async Task<string> NewModAsync(long ownerId, bool hidden)
    {
        var id = $"Test{Guid.NewGuid():N}"[..20];
        var mods = new ModRepository(Db);

        await mods.CreateAsync(new ModRow
        {
            Id = id,
            Type = "mod",
            Name = "A test listing",
            Abstract = "For the hide-author tests.",
            License = "MIT",
            Status = "active",
            ListingState = "listed",
            HideAuthor = hidden,
            CreatedBy = ownerId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }, ownerId, default);

        _mods.Add(id);
        return id;
    }

    [RequiresPostgresFact]
    public async Task Hiding_the_author_does_not_give_up_the_owner()
    {
        // The whole design rests on this. If hiding dropped the maintainer row, a takedown would
        // have nobody to land on and the author would lose their own management screen.
        var owner = await NewAccountAsync();
        var modId = await NewModAsync(owner, hidden: true);

        var mods = new ModRepository(Db);
        var mod = await mods.FindAsync(modId, default);
        var recorded = await mods.OwnerAsync(modId, default);

        Assert.True(mod!.HideAuthor);
        Assert.NotNull(recorded);
    }

    [RequiresPostgresFact]
    public async Task A_hidden_listing_is_left_off_the_public_profile()
    {
        // The leak this feature would otherwise have. Both listings belong to the same person, and
        // only the one that asked to be hidden is missing from what strangers see.
        var owner = await NewAccountAsync();
        var open = await NewModAsync(owner, hidden: false);
        var hidden = await NewModAsync(owner, hidden: true);

        var visible = await PublicProfileModIdsAsync(owner);

        Assert.Contains(open, visible);
        Assert.DoesNotContain(hidden, visible);
    }

    [RequiresPostgresFact]
    public async Task Unhiding_puts_the_listing_back_on_the_profile()
    {
        // The setting has to be reversible in both directions. A privacy control that only turns
        // on is a trap, not a control.
        var owner = await NewAccountAsync();
        var modId = await NewModAsync(owner, hidden: true);

        var mods = new ModRepository(Db);
        var mod = await mods.FindAsync(modId, default);
        await mods.UpdateAsync(mod! with { HideAuthor = false }, default);

        Assert.Contains(modId, await PublicProfileModIdsAsync(owner));
    }

    [RequiresPostgresFact]
    public async Task An_edit_that_says_nothing_about_hiding_leaves_it_alone()
    {
        // The bug a plain `bool` on the request body would have caused: every unrelated edit
        // arrives with HideAuthor defaulted to false and quietly publishes the author's name.
        // Reproduced at the layer the endpoint uses, which is where the value is resolved.
        var owner = await NewAccountAsync();
        var modId = await NewModAsync(owner, hidden: true);

        var mods = new ModRepository(Db);
        var mod = await mods.FindAsync(modId, default);

        bool? notSent = null;
        await mods.UpdateAsync(mod! with
        {
            Name = "Renamed, nothing to do with the author",
            HideAuthor = notSent ?? mod.HideAuthor,
        }, default);

        var after = await mods.FindAsync(modId, default);

        Assert.True(after!.HideAuthor);
        Assert.Equal("Renamed, nothing to do with the author", after.Name);
    }

    [RequiresPostgresFact]
    public async Task The_static_export_carries_a_placeholder_instead_of_the_name()
    {
        // The quietest leak of the three, and the worst: the export and its git mirror are public
        // and permanent, so a name suppressed on the site and published here is suppressed
        // nowhere. Runs the exporter's own reader rather than a copy of its query.
        var owner = await NewAccountAsync();
        var open = await NewModAsync(owner, hidden: false);
        var hidden = await NewModAsync(owner, hidden: true);

        using var connection = await Db.OpenAsync(default);
        var input = await KsaMods.Exporter.ExportReader.ReadAsync(connection, default);

        var exportedOpen = input.Listings.Single(l => l.Id == open);
        var exportedHidden = input.Listings.Single(l => l.Id == hidden);

        Assert.Contains("Test Person", exportedOpen.Authors);

        // Named as anonymous rather than left empty: RFC 0031 requires an author, and an empty
        // list would drop the listing out of the export instead of anonymising it.
        Assert.DoesNotContain("Test Person", exportedHidden.Authors);
        Assert.Equal(["Anonymous"], exportedHidden.Authors);
    }

    /// <summary>
    /// The profile query from AccountEndpoints, kept in step with it deliberately: the point of
    /// these tests is the exclusion, and asserting it against a different query would pass while
    /// the real one leaked.
    /// </summary>
    private async Task<IReadOnlyList<string>> PublicProfileModIdsAsync(long accountId)
    {
        using var connection = await Db.OpenAsync(default);

        var rows = await connection.QueryAsync<string>("""
            select m.id
            from mod m
            join mod_maintainer mm on mm.mod_id = m.id and mm.role = 'owner'
            where mm.account_id = @id and m.listing_state = 'listed' and not m.hide_author
            """,
            new { id = accountId });

        return rows.ToList();
    }
}
