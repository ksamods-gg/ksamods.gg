using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace KsaMods.Web.Services;

/// <summary>
/// Renders a mod description, which is CommonMark written by a stranger and shown to everybody.
///
/// <para>The description was rendered as plain text until now, because the schema always called it
/// CommonMark and nothing here could render it safely. That is the whole difficulty: this is the
/// most likely XSS vector on the site, and a renderer that is merely correct is not enough.</para>
///
/// <para><b>Two defences, because one is not sufficient.</b></para>
///
/// <para><c>DisableHtml</c> stops raw HTML surviving the parse, so nothing an author writes as
/// markup reaches the page as markup. That alone leaves the hole people actually exploit: Markdig
/// does not sanitise URLs, so <c>[click me](javascript:…)</c> parses into an ordinary link and
/// renders into a working one. Every link and image URL is therefore checked against a scheme
/// allowlist, and anything else is dropped.</para>
///
/// <para>An allowlist rather than a blocklist. <c>javascript:</c> is the obvious one, and the list
/// of the others - <c>data:</c>, <c>vbscript:</c>, and whatever a browser adds next - is not
/// knowable in advance. Naming what is permitted fails closed.</para>
/// </summary>
public static class SafeMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        // The single most important line here. Without it an author writes <script> and it runs.
        .DisableHtml()

        // Bare URLs become links. Worth it because people paste them, and the scheme check below
        // covers autolinks exactly as it covers written ones.
        .UseAutoLinks()
        .Build();

    /// <summary>
    /// Schemes a link may use. Relative URLs carry no scheme and are allowed by falling through.
    /// </summary>
    private static readonly string[] Allowed = ["http", "https", "mailto"];

    public static MarkupString ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return new MarkupString(string.Empty);

        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        // Walked after parsing rather than filtered from the output, because at this point a URL is
        // a value in a known place. Trying to find one again in rendered HTML means parsing HTML
        // with string matching, which is how sanitisers get bypassed.
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url)) link.Url = null;
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);

        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return new MarkupString(writer.ToString());
    }

    /// <summary>
    /// Whether a URL may be rendered.
    ///
    /// <para>The parsing is deliberately paranoid about what counts as a scheme. Browsers ignore
    /// leading whitespace and embedded control characters, so <c>"java\tscript:alert(1)"</c> and
    /// <c>" javascript:alert(1)"</c> both execute while neither starts with a scheme any naive
    /// check would recognise. Those characters are stripped before the scheme is read, so the
    /// string being checked is the one the browser will act on.</para>
    /// </summary>
    public static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var cleaned = new string([.. url.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))]);

        if (cleaned.Length == 0) return false;

        var colon = cleaned.IndexOf(':');

        // No colon at all, or one that arrives after the path has started, means there is no
        // scheme and the URL is relative. "foo/bar:baz" is a path, not a "foo/bar" scheme.
        if (colon < 0) return true;

        var separator = cleaned.AsSpan(0, colon).IndexOfAny('/', '?', '#');
        if (separator >= 0) return true;

        return Allowed.Contains(cleaned[..colon], StringComparer.OrdinalIgnoreCase);
    }
}
