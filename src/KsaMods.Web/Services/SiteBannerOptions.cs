using System.Security.Cryptography;
using System.Text;

namespace KsaMods.Web.Services;

/// <summary>
/// The notice strip across the top of every page: outages, incidents, anything the site itself
/// needs to say. Configured under a "SiteBanner" section, so turning one on is a config change
/// and a restart rather than a deploy.
///
/// <para>Distinct from a mod's banner image, which belongs to an author and lives on the mod.</para>
/// </summary>
public sealed class SiteBannerOptions
{
    public string? Message { get; set; }

    /// <summary>info, warning or error. Anything else is treated as info.</summary>
    public string Variant { get; set; } = "info";

    public string? LinkText { get; set; }
    public string? LinkHref { get; set; }
    public bool Dismissible { get; set; } = true;

    public bool IsActive => !string.IsNullOrWhiteSpace(Message);

    /// <summary>
    /// Identifies this banner to the browser's dismissal memory.
    ///
    /// <para>It is derived from the text rather than hand-set, so editing the message publishes a
    /// new banner that everyone sees again. A hand-written id is the kind of thing you forget to
    /// bump, and the failure mode is an incident notice nobody reads.</para>
    /// </summary>
    public string DismissKey
    {
        get
        {
            var material = $"{Message}{LinkHref}";
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant();
        }
    }
}
