using KsaMods.Forge;
using KsaMods.Worker;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// The parts of talking to a forge that are this project's decisions rather than GitHub's.
/// </summary>
public class ForgeTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("v0.1.0+build.7", "0.1.0+build.7")]
    public void A_version_tag_becomes_a_version(string tag, string expected) =>
        Assert.Equal(expected, ReleaseTag.ToVersion(tag));

    [Theory]
    [InlineData("release-3")]
    [InlineData("2024-06-01")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("")]
    public void A_tag_that_is_not_a_version_is_refused_rather_than_guessed(string tag)
    {
        // The site orders releases by version and modlists pin exact ones, so a guess here is
        // wrong in a way nobody downstream can see. Skipping with a reason is the honest answer.
        Assert.Null(ReleaseTag.ToVersion(tag));
    }

    [Theory]
    [InlineData("owner/repo")]
    [InlineData("Owner/Repo.With-Dots_1")]
    public void A_repository_name_of_the_right_shape_is_accepted(string name) =>
        Assert.True(RepoName.IsValid(name));

    [Theory]
    [InlineData("owner")]                    // no repository
    [InlineData("owner/repo/extra")]         // a path, not a name
    [InlineData("../../etc/passwd")]         // traversal out of the API path
    [InlineData("owner/..")]
    [InlineData("owner/repo?x=1")]           // a query string smuggled into the path
    [InlineData("owner /repo")]
    [InlineData(null)]
    public void Anything_that_could_escape_the_api_path_is_refused(string? name)
    {
        // This value arrives from a form and is about to be interpolated into a URL. Being
        // stricter than GitHub only rejects things it would have accepted anyway.
        Assert.False(RepoName.IsValid(name));
    }

    [Fact]
    public void Only_forges_with_an_adapter_are_offered()
    {
        // The database constraint permits gitlab and codeberg because the schema anticipates them.
        // Accepting one here would let somebody connect a repository the importer cannot read - a
        // listing that looks connected and imports nothing.
        Assert.True(ForgeAllowlist.Allows("github"));
        Assert.False(ForgeAllowlist.Allows("gitlab"));
        Assert.False(ForgeAllowlist.Allows("codeberg"));
        Assert.False(ForgeAllowlist.Allows("evil.example.com"));
    }

    [Fact]
    public void A_challenge_is_unguessable_and_new_each_time()
    {
        var first = RepositoryProof.NewChallenge();
        var second = RepositoryProof.NewChallenge();

        Assert.NotEqual(first, second);
        Assert.StartsWith("ksamods-verify-", first, StringComparison.Ordinal);

        // 128 bits of it. A challenge somebody can guess is a repository somebody can take.
        Assert.Equal("ksamods-verify-".Length + 32, first.Length);
    }

    [Theory]
    // The forms people actually paste, all naming one repository.
    [InlineData("Maximilian-Nesslauer/KSA-DeltaVMap")]
    [InlineData("https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap")]
    [InlineData("https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap/")]
    [InlineData("https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap.git")]
    [InlineData("http://github.com/Maximilian-Nesslauer/KSA-DeltaVMap")]
    [InlineData("github.com/Maximilian-Nesslauer/KSA-DeltaVMap")]
    [InlineData("www.github.com/Maximilian-Nesslauer/KSA-DeltaVMap")]
    [InlineData("git@github.com:Maximilian-Nesslauer/KSA-DeltaVMap.git")]
    // Deep links: somebody copies the address while looking at a branch or a file.
    [InlineData("https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap/tree/main")]
    [InlineData("https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap/blob/main/README.md")]
    [InlineData("https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap?tab=readme-ov-file")]
    [InlineData("  https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap  ")]
    public void Anything_that_names_a_repository_normalises_to_owner_and_name(string input)
    {
        Assert.True(RepoName.TryNormalise(input, out var fullName), $"'{input}' was refused.");
        Assert.Equal("Maximilian-Nesslauer/KSA-DeltaVMap", fullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("KSA-DeltaVMap")]                       // No owner.
    [InlineData("https://github.com/Maximilian")]       // A person, not a repository.
    // The reason normalising has to end at IsValid rather than replace it: a traversal or a
    // separator smuggled through a URL would otherwise escape the API path it gets interpolated into.
    [InlineData("https://github.com/../../admin/secrets")]
    [InlineData("owner/../etc")]
    [InlineData("owner/repo name")]
    [InlineData("owner/repo?x=1")]
    public void Anything_that_does_not_is_refused(string? input)
    {
        Assert.False(RepoName.TryNormalise(input, out var fullName));
        Assert.Equal("", fullName);
    }

    [Fact]
    public void A_challenge_is_usable_as_a_repository_topic_without_reshaping()
    {
        // The topic proof only works because the challenge already fits what a forge accepts as a
        // topic: 50 characters at most, lowercase alphanumerics and hyphens, starting with one of
        // them. If NewChallenge ever grows or gains a capital, that proof silently stops being
        // offerable, and the failure would show up as a user unable to paste it rather than here.
        var challenge = RepositoryProof.NewChallenge();

        Assert.True(challenge.Length <= 50, $"'{challenge}' is {challenge.Length} characters, too long for a topic.");
        Assert.Matches("^[a-z0-9][a-z0-9-]*$", challenge);
    }

    [Fact]
    public void A_topic_matching_the_challenge_proves_the_repository()
    {
        var challenge = RepositoryProof.NewChallenge();

        Assert.True(RepositoryProof.SatisfiedByTopic([challenge], challenge));

        // Among the topics the repository already had, which is the normal case.
        Assert.True(RepositoryProof.SatisfiedByTopic(["ksp", "modding", challenge], challenge));

        // Forges lowercase topics on the way in, so a proof that failed on capitalisation the
        // user never typed would be a puzzle rather than a check.
        Assert.True(RepositoryProof.SatisfiedByTopic([challenge.ToUpperInvariant()], challenge));
    }

    [Fact]
    public void Topics_without_the_challenge_prove_nothing()
    {
        var challenge = RepositoryProof.NewChallenge();

        Assert.False(RepositoryProof.SatisfiedByTopic(null, challenge));
        Assert.False(RepositoryProof.SatisfiedByTopic([], challenge));
        Assert.False(RepositoryProof.SatisfiedByTopic(["ksp", "modding"], challenge));
        Assert.False(RepositoryProof.SatisfiedByTopic([RepositoryProof.NewChallenge()], challenge));

        // A listing with no challenge outstanding is not proven by anything at all, which is what
        // stops an empty stored challenge matching an empty topic.
        Assert.False(RepositoryProof.SatisfiedByTopic([challenge], null));
        Assert.False(RepositoryProof.SatisfiedByTopic([""], ""));
    }

    [Fact]
    public void A_published_challenge_satisfies_the_proof_despite_a_trailing_newline()
    {
        // Every editor adds one. Refusing over it would be a puzzle rather than a safeguard.
        var challenge = RepositoryProof.NewChallenge();

        Assert.True(RepositoryProof.Satisfies(challenge, challenge));
        Assert.True(RepositoryProof.Satisfies($"{challenge}\n", challenge));
        Assert.True(RepositoryProof.Satisfies($"  {challenge}  \r\n", challenge));
    }

    [Fact]
    public void A_different_or_missing_challenge_does_not()
    {
        var challenge = RepositoryProof.NewChallenge();

        Assert.False(RepositoryProof.Satisfies(null, challenge));
        Assert.False(RepositoryProof.Satisfies("", challenge));
        Assert.False(RepositoryProof.Satisfies(RepositoryProof.NewChallenge(), challenge));

        // Case matters: this is a secret being compared, not a piece of text.
        Assert.False(RepositoryProof.Satisfies(challenge.ToUpperInvariant(), challenge));

        // And a link with no challenge can never be satisfied, whatever the file says.
        Assert.False(RepositoryProof.Satisfies("anything", null));
        Assert.False(RepositoryProof.Satisfies("anything", "  "));
    }

    // ── choosing which file to import ──

    private static ForgeRelease Release(params string[] assetNames) => new()
    {
        Id = "1",
        Tag = "v1.0.0",
        PublishedAt = DateTimeOffset.UnixEpoch,
        Assets = assetNames.Select(n => new ForgeAsset
        {
            Id = n,
            Name = n,
            DownloadUrl = $"https://github.com/o/r/releases/download/v1.0.0/{n}",
        }).ToList(),
    };

    [Fact]
    public void One_archive_is_the_one_to_import()
    {
        var chosen = ImportHandler.SelectAsset(Release("MyMod.zip", "README.md", "checksums.txt"), null);

        Assert.NotNull(chosen);
        Assert.Equal("MyMod.zip", chosen.Name);
    }

    [Fact]
    public void Several_archives_are_refused_rather_than_guessed_between()
    {
        // Importing the wrong one produces a listing that installs the wrong thing, and the author
        // cannot tell from the outside why. The asset pattern is how they say which.
        Assert.Null(ImportHandler.SelectAsset(Release("MyMod-win.zip", "MyMod-linux.zip"), null));

        var chosen = ImportHandler.SelectAsset(Release("MyMod-win.zip", "MyMod-linux.zip"), "*-linux.zip");

        Assert.NotNull(chosen);
        Assert.Equal("MyMod-linux.zip", chosen.Name);
    }

    [Fact]
    public void A_release_with_no_archive_selects_nothing()
    {
        Assert.Null(ImportHandler.SelectAsset(Release("notes.txt"), null));
        Assert.Null(ImportHandler.SelectAsset(Release(), null));
    }

    [Fact]
    public void A_pattern_that_matches_nothing_selects_nothing()
    {
        // Rather than falling back to "the only zip". A pattern is an instruction, and quietly
        // ignoring it would import a file the author specifically did not ask for.
        Assert.Null(ImportHandler.SelectAsset(Release("MyMod.zip"), "*.tar.gz"));
    }

    [Fact]
    public void Source_archives_are_not_mistaken_for_a_release()
    {
        // GitHub attaches no source archives to the assets array - they are separate URLs - so an
        // author who tags without attaching anything gets a clear skip rather than an import of
        // their repository tree, which is not an installable mod.
        Assert.Null(ImportHandler.SelectAsset(Release(), null));
    }
}
