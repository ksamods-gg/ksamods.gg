namespace KsaMods.Api.Domain;

public static class SiteRole
{
    public const string User = "user";
    public const string Moderator = "moderator";
    public const string Admin = "admin";
}

public static class ModRole
{
    public const string Owner = "owner";
    public const string Maintainer = "maintainer";
}

public static class ModlistRole
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Editor = "editor";
}

/// <summary>Who is asking, and what they hold on the subject in question.</summary>
public sealed record Principal
{
    public required long AccountId { get; init; }
    public string SiteRole { get; init; } = Domain.SiteRole.User;

    /// <summary>The caller's role on the mod in question, if any.</summary>
    public string? ModRole { get; init; }

    /// <summary>The caller's role on the modlist in question, if any.</summary>
    public string? ModlistRole { get; init; }

    public bool IsModerator => SiteRole is Domain.SiteRole.Moderator or Domain.SiteRole.Admin;
    public bool IsAdmin => SiteRole == Domain.SiteRole.Admin;
    public bool IsModOwner => ModRole == Domain.ModRole.Owner;
    public bool IsModMaintainer => ModRole is Domain.ModRole.Owner or Domain.ModRole.Maintainer;
    public bool IsListOwner => ModlistRole == Domain.ModlistRole.Owner;
    public bool IsListAdmin => ModlistRole is Domain.ModlistRole.Owner or Domain.ModlistRole.Admin;
    public bool IsListEditor => ModlistRole is
        Domain.ModlistRole.Owner or Domain.ModlistRole.Admin or Domain.ModlistRole.Editor;
}

public enum Capability
{
    EditModListing,
    ConnectRepository,
    ImportRelease,
    YankRelease,
    AmendRelease,
    ManageMaintainers,
    TransferModOwnership,
    SetModVisibility,
    DeleteMod,
    VouchRepository,

    EditModlistDraft,
    PublishModlistVersion,
    ManageCollaborators,
    DeleteModlist,
    TransferModlistOwnership,

    Moderate,
    SuspendAccount,
    ManageSiteRoles,
    ManageTags,
}

/// <summary>
/// The permission matrix from backend.md §4.2, as a pure function.
///
/// <para>Kept out of the endpoint layer deliberately: authorisation that is only expressed as
/// scattered <c>if</c> statements is authorisation nobody can audit, and this is the one part of
/// the API where a quiet mistake hands someone else's mod to a stranger.</para>
/// </summary>
public static class Permissions
{
    public static bool Allows(Principal principal, Capability capability) => capability switch
    {
        // Moderators can edit listing metadata - that is how a takedown notice gets acted on
        // without waiting for an absent author.
        Capability.EditModListing => principal.IsModMaintainer || principal.IsModerator,

        // Connecting a repository is the ownership proof, so only the owner may change it.
        // A maintainer who could re-point it could quietly take over the listing.
        Capability.ConnectRepository => principal.IsModOwner,

        // Vouching is not connecting. Staff cannot point a listing at a different repository,
        // which is the takeover risk the rule above exists for; they can only attest that the
        // repository already named is the right one. That is a judgement they can reach by means
        // the automated proofs cannot see, and an unreachable author should not make a listing
        // permanently unmanageable.
        Capability.VouchRepository => principal.IsModerator,

        Capability.ImportRelease => principal.IsModMaintainer || principal.IsModerator,
        Capability.YankRelease => principal.IsModMaintainer || principal.IsModerator,
        Capability.AmendRelease => principal.IsModMaintainer || principal.IsModerator,

        Capability.ManageMaintainers => principal.IsModOwner || principal.IsModerator,
        Capability.TransferModOwnership => principal.IsModOwner || principal.IsModerator,

        // Publishing puts a listing in front of everyone, and unlisting takes it back out. Both
        // are the owner's call rather than a maintainer's: a maintainer helps run a listing,
        // they do not decide whether it exists in public.
        Capability.SetModVisibility => principal.IsModOwner || principal.IsModerator,

        // Owner only, and never a moderator. A moderator removing content has delisting, which
        // is reversible and leaves the id resolvable; handing them a destructive delete as well
        // would make the reversible tool the harder one to reach for.
        Capability.DeleteMod => principal.IsModOwner,

        Capability.EditModlistDraft => principal.IsListEditor,

        // Publishing mints an immutable version that other people will install. It is a
        // meaningfully different act from adding a mod to a draft, and splitting it is the main
        // reason `admin` exists as a distinct role from `editor`.
        Capability.PublishModlistVersion => principal.IsListAdmin,

        Capability.ManageCollaborators => principal.IsListAdmin || principal.IsModerator,
        Capability.DeleteModlist => principal.IsListOwner,
        Capability.TransferModlistOwnership => principal.IsListOwner || principal.IsModerator,

        Capability.Moderate => principal.IsModerator,

        // Suspending someone stops them signing in. A moderator needs it to stop abuse in
        // progress, and it is reversible, so it does not need to wait for an admin.
        Capability.SuspendAccount => principal.IsModerator,

        // Granting moderator is the one action that hands out the power to do everything else on
        // this list, so it sits a level above them all. A moderator who could promote could grant
        // themselves an admin accomplice; an admin doing it is at least a deliberate choice by
        // someone who already had the keys.
        Capability.ManageSiteRoles => principal.IsAdmin,

        // The tag vocabulary is a curation decision rather than an abuse one: it shapes how the
        // whole catalogue is filed, and a tag added on a whim outlives whoever added it. Moderators
        // act on things that are wrong; this is about deciding what the site's categories are, and
        // it sits with the people who own that.
        Capability.ManageTags => principal.IsAdmin,

        _ => false,
    };

    /// <summary>
    /// Whether a caller may see a modlist at all. Private lists are collaborators-only - but
    /// still subject to moderation on report, and the interface should say so rather than
    /// implying privacy from staff.
    /// </summary>
    public static bool CanViewModlist(Principal? principal, string visibility, string listingState)
    {
        if (principal?.IsModerator == true) return true;
        if (listingState is "delisted" or "taken_down") return principal?.IsListEditor == true;

        return visibility switch
        {
            "public" or "unlisted" => true,
            "private" => principal?.IsListEditor == true,
            _ => false,
        };
    }

    /// <summary>
    /// Whether a caller may see a mod release that has not passed validation. A failed import
    /// stays visible to its maintainers with the full report, so an author fixing a packaging
    /// mistake never has to delete and recreate anything.
    /// </summary>
    public static bool CanViewRelease(Principal? principal, string validationState, string listingState)
    {
        if (principal?.IsModerator == true) return true;
        if (principal?.IsModMaintainer == true) return true;

        if (validationState is "pending" or "running" or "failed") return false;
        return listingState is "listed" or "unlisted";
    }
}
