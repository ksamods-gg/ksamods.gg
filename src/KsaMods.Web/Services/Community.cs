namespace KsaMods.Web.Services;

/// <summary>
/// Where the KSA modding community actually lives, which is not here.
///
/// <para>One place for the address so the footer and the home page cannot drift apart. An invite
/// that has been rotated in one of two places is worse than one that is wrong in both, because
/// nobody goes looking for the second copy.</para>
///
/// <para>Deliberately holds no member count. A number like that is right on the day it is typed
/// and quietly wrong from then on, and it is not the sort of thing anybody remembers to come
/// back and edit.</para>
/// </summary>
public static class Community
{
    /// <summary>The invite. Not RocketWerkz's server, which is why nothing here says "official"
    /// and every place that shows this link says whose it is.</summary>
    public const string Discord = "https://discord.gg/nt4fK4QuTz";

    /// <summary>
    /// What the Discord is called. Written once so no page invents its own name for it.
    /// </summary>
    public const string DiscordName = "KSA Modding Society";

    /// <summary>
    /// Where the member counts come from. A base address rather than a full URL because it is
    /// handed to a named <see cref="HttpClient"/>, which is what carries the timeout.
    /// </summary>
    public const string PresenceBaseUrl = "https://versions.kittenspaceagency.wiki";
}
