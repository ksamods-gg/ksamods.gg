using Dapper;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>The slug rules, which are pure and hold whatever the database says.</summary>
public class TagShapeTests
{
    [Theory]
    [InlineData("Parts", "parts")]
    [InlineData("  UI  ", "ui")]
    [InlineData("Life-Support", "life-support")]
    public void A_tag_is_lowercased_before_anything_else_looks_at_it(string raw, string expected)
    {
        // RFC 0031 says lowercase, so "Parts" and "parts" are one tag. Normalising first is what
        // stops a capitalised tag being reported as unknown.
        Assert.Equal(expected, TagVocabulary.Normalise(raw));
    }

    [Theory]
    [InlineData("parts")]
    [InlineData("life-support")]
    [InlineData("kf2")]
    public void A_well_formed_slug_is_accepted(string slug) =>
        Assert.Null(TagVocabulary.Malformed(slug));

    [Theory]
    [InlineData("a", "length")]                 // too short to mean anything
    [InlineData("life support", "shape")]       // a space makes two words one tag
    [InlineData("life--support", "shape")]      // doubled separator, invisible in a URL
    [InlineData("-parts", "shape")]
    [InlineData("parts-", "shape")]
    [InlineData("Parts", "shape")]              // caller must normalise first
    [InlineData("парты", "shape")]              // a tag is typed at other people
    public void A_malformed_slug_is_refused_with_a_reason(string slug, string _) =>
        Assert.NotNull(TagVocabulary.Malformed(slug));

    [Fact]
    public void Only_an_admin_curates_the_vocabulary()
    {
        // Deciding what the site's categories are is not the same job as acting on things that are
        // wrong, and a tag added on a whim outlives whoever added it.
        Assert.True(Permissions.Allows(
            new Principal { AccountId = 1, SiteRole = SiteRole.Admin }, Capability.ManageTags));

        Assert.False(Permissions.Allows(
            new Principal { AccountId = 2, SiteRole = SiteRole.Moderator }, Capability.ManageTags));

        Assert.False(Permissions.Allows(
            new Principal { AccountId = 3, ModRole = ModRole.Owner }, Capability.ManageTags));
    }
}

/// <summary>Vetting and proposing, against the real table.</summary>
public sealed class TagVocabularyDatabaseTests : IAsyncLifetime
{
    private NpgsqlDataSource? _source;
    private Database? _database;
    private readonly List<string> _slugs = [];
    private readonly List<long> _accounts = [];

    private Database Db => _database ?? throw new InvalidOperationException("No test database.");
    private TagVocabulary Vocabulary => new(Db);

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
        if (_database is not null)
        {
            using var connection = await _database.OpenAsync(CancellationToken.None);

            await connection.ExecuteAsync("delete from tag where slug = any(@slugs)", new { slugs = _slugs.ToArray() });
            await connection.ExecuteAsync("delete from account where id = any(@ids)", new { ids = _accounts.ToArray() });
        }

        if (_source is not null) await _source.DisposeAsync();
    }

    [RequiresPostgresFact]
    public async Task An_approved_tag_may_be_used()
    {
        var slug = await ApprovedTagAsync();

        var (accepted, refused) = await Vocabulary.VetAsync([slug], default);

        Assert.Equal([slug], accepted);
        Assert.Empty(refused);
    }

    [RequiresPostgresFact]
    public async Task Capitalisation_does_not_make_a_tag_unknown()
    {
        var slug = await ApprovedTagAsync();

        var (accepted, refused) = await Vocabulary.VetAsync([slug.ToUpperInvariant()], default);

        Assert.Equal([slug], accepted);
        Assert.Empty(refused);
    }

    [RequiresPostgresFact]
    public async Task A_tag_nobody_approved_is_refused_rather_than_dropped()
    {
        // Silently filtering it would leave the author believing their listing is filed under a
        // word that is not on it.
        var (accepted, refused) = await Vocabulary.VetAsync([$"unknown{Guid.NewGuid():N}"[..20]], default);

        Assert.Empty(accepted);
        Assert.Single(refused);
    }

    [RequiresPostgresFact]
    public async Task A_tag_still_waiting_on_review_cannot_be_used_yet()
    {
        var account = await NewAccountAsync();
        var slug = NewSlug();

        Assert.Equal(TagProposal.Proposed,
            (await Vocabulary.ProposeAsync(slug, "for testing", account, default)).Outcome);

        var (accepted, refused) = await Vocabulary.VetAsync([slug], default);

        Assert.Empty(accepted);
        Assert.Contains("Waiting", refused[slug], StringComparison.Ordinal);
    }

    [RequiresPostgresFact]
    public async Task Duplicates_collapse_and_the_limit_is_enforced()
    {
        var slug = await ApprovedTagAsync();

        var (accepted, _) = await Vocabulary.VetAsync([slug, slug, slug.ToUpperInvariant()], default);
        Assert.Equal([slug], accepted);

        // A listing tagged with everything has said nothing, and every extra tag dilutes the
        // filter for everybody else.
        var many = Enumerable.Range(0, TagVocabulary.MaxPerListing + 1).Select(i => $"tag-{i}").ToArray();
        var (none, refused) = await Vocabulary.VetAsync(many, default);

        Assert.Empty(none);
        Assert.NotEmpty(refused);
    }

    [RequiresPostgresFact]
    public async Task Proposing_a_tag_that_already_exists_says_so_rather_than_failing()
    {
        var account = await NewAccountAsync();
        var slug = await ApprovedTagAsync();

        var (outcome, _) = await Vocabulary.ProposeAsync(slug, null, account, default);

        // Somebody asking for a tag that exists has been served - the thing they wanted is there.
        Assert.Equal(TagProposal.AlreadyApproved, outcome);
    }

    [RequiresPostgresFact]
    public async Task Proposing_twice_does_not_queue_it_twice()
    {
        var first = await NewAccountAsync();
        var second = await NewAccountAsync();
        var slug = NewSlug();

        Assert.Equal(TagProposal.Proposed, (await Vocabulary.ProposeAsync(slug, null, first, default)).Outcome);
        Assert.Equal(TagProposal.AlreadyPending, (await Vocabulary.ProposeAsync(slug, null, second, default)).Outcome);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "select count(*) from tag where slug = @slug", new { slug }));
    }

    [RequiresPostgresFact]
    public async Task A_rejected_tag_comes_back_with_the_reason_it_was_rejected()
    {
        // Otherwise the same tag gets suggested every month by people who cannot know it was
        // already considered.
        var account = await NewAccountAsync();
        var slug = NewSlug();

        await Vocabulary.ProposeAsync(slug, null, account, default);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            update tag set state = 'rejected', reviewed_at = now(),
                           review_note = 'Too vague to filter by.'
            where slug = @slug
            """,
            new { slug });

        var (outcome, note) = await Vocabulary.ProposeAsync(slug, null, account, default);

        Assert.Equal(TagProposal.PreviouslyRejected, outcome);
        Assert.Equal("Too vague to filter by.", note);
    }

    [RequiresPostgresFact]
    public async Task The_database_refuses_a_decision_with_no_decider()
    {
        // The backstop for the review endpoints: a tag cannot sit approved with no record of who
        // said so.
        var slug = NewSlug();
        _slugs.Add(slug);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var violation = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync(
                "insert into tag (slug, label, state) values (@slug, @slug, 'approved')",
                new { slug }));

        Assert.Equal("23514", violation.SqlState);
    }

    [RequiresPostgresFact]
    public async Task The_column_still_accepts_a_tag_the_vocabulary_does_not_know()
    {
        // Deliberate. RFC 0031 leaves the format free-form, and §12 ingest will carry listings
        // whose tags are valid per the format and absent from our list. Refusing them at the
        // storage layer would assert this site's policy as though it were the format's.
        var owner = await NewAccountAsync();
        var id = $"test.tag.{Guid.NewGuid():N}"[..22];

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            insert into mod (id, id_lower, name, abstract, license, tags, created_by)
            values (@id, lower(@id), 'Test', 'For tests.', 'MIT', array['not-on-the-list'], @owner)
            """,
            new { id, owner });

        var stored = await connection.ExecuteScalarAsync<string[]?>(
            "select tags from mod where id = @id", new { id });

        Assert.NotNull(stored);
        Assert.Equal(["not-on-the-list"], stored);

        await connection.ExecuteAsync("delete from mod where id = @id", new { id });
    }

    private string NewSlug()
    {
        var slug = $"t{Guid.NewGuid():N}"[..12];
        _slugs.Add(slug);
        return slug;
    }

    private async Task<string> ApprovedTagAsync()
    {
        var slug = NewSlug();

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            insert into tag (slug, label, state, reviewed_at) values (@slug, @slug, 'approved', now())
            """,
            new { slug });

        return slug;
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
}
