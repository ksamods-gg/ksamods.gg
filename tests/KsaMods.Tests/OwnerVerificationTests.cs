using KsaMods.Forge;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Verifying a repository by the fact that it sits on the connected account.
///
/// <para>This is the one shortcut through repository proof, so the tests that matter are the ones
/// asserting it does <b>not</b> fire. Signing in with a forge proves you control an account and
/// says nothing on its own about a repository: if this rule were loose in any direction it would
/// hand somebody else's listing to whoever asked first.</para>
///
/// <para>Pure, and deliberately so. The rule is three conditions and no I/O, which means it can be
/// pinned exhaustively here rather than inferred from an endpoint test that happens to exercise
/// one path through it.</para>
/// </summary>
public class OwnerVerificationTests
{
    private static ForgeRepository Repo(
        string? ownerId = "4242", bool ownerIsUser = true, string? ownerLogin = "maxi") => new()
        {
            Id = "1",
            FullName = $"{ownerLogin}/KSA-DeltaVMap",
            DefaultBranch = "main",
            OwnerId = ownerId,
            OwnerLogin = ownerLogin,
            OwnerIsUser = ownerIsUser,
        };

    [Fact]
    public void A_repository_on_your_own_account_needs_no_further_proof()
    {
        // The case the whole thing exists for: github.com/maxi/KSA-DeltaVMap, connected by the
        // account GitHub calls 4242, which is the account that owns it.
        Assert.True(RepositoryProof.SatisfiedByOwner(Repo(), "4242"));
    }

    [Fact]
    public void Somebody_elses_repository_is_not_yours()
    {
        // The attack this rule has to survive. Being signed in is not a claim on a repository.
        Assert.False(RepositoryProof.SatisfiedByOwner(Repo(ownerId: "4242"), "9999"));
    }

    [Fact]
    public void An_organisation_repository_is_never_settled_this_way()
    {
        // Belonging to an org is not owning its repositories, and we do not ask for the scope that
        // would tell us about membership anyway. Even with the ids lined up, an org owner has to
        // go the long way round.
        Assert.False(RepositoryProof.SatisfiedByOwner(
            Repo(ownerId: "4242", ownerIsUser: false, ownerLogin: "RocketWerkz"), "4242"));
    }

    [Fact]
    public void A_forge_that_did_not_say_who_owns_it_proves_nothing()
    {
        // Absent is not a match. A forge that stops sending the owner block must fail closed, or
        // the shortcut silently becomes "anybody signed in".
        Assert.False(RepositoryProof.SatisfiedByOwner(Repo(ownerId: null), "4242"));
    }

    [Fact]
    public void Somebody_with_no_connected_identity_proves_nothing()
    {
        // Signing in with Discord and connecting a GitHub repository is the ordinary way to arrive
        // here with nothing to compare against.
        Assert.False(RepositoryProof.SatisfiedByOwner(Repo(), null));
        Assert.False(RepositoryProof.SatisfiedByOwner(Repo(), ""));
        Assert.False(RepositoryProof.SatisfiedByOwner(Repo(), "   "));
    }

    [Theory]
    [InlineData("4242", "42420")]
    [InlineData("4242", " 4242")]
    [InlineData("4242", "4242 ")]
    public void The_ids_have_to_match_exactly(string ownerId, string connected)
    {
        // Compared as opaque strings with no trimming and no prefix matching. These are forge ids,
        // not something a person types, so anything that is not equal is not the same account.
        Assert.False(RepositoryProof.SatisfiedByOwner(Repo(ownerId: ownerId), connected));
    }

    [Fact]
    public void The_login_is_never_what_decides_it()
    {
        // GitHub logins can be changed, and a freed-up one can be claimed by somebody else. If
        // this ever started matching on the name, whoever picked up an abandoned username would
        // inherit a proof over repositories that were never theirs. Same login, different account:
        // still refused.
        var renamed = Repo(ownerId: "9999", ownerLogin: "maxi");

        Assert.False(RepositoryProof.SatisfiedByOwner(renamed, "4242"));
    }
}
