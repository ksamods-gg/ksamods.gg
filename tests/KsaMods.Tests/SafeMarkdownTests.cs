using KsaMods.Web.Services;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// A mod description is CommonMark written by a stranger and shown to everyone who opens the
/// listing, so this is the most likely XSS vector on the site. These tests are the reason it is
/// rendered at all.
/// </summary>
public class SafeMarkdownTests
{
    private static string Html(string markdown) => SafeMarkdown.ToHtml(markdown).Value ?? "";

    [Fact]
    public void Renders_the_markdown_a_description_actually_uses()
    {
        var html = Html("""
            Adds quick-tools to the Transfer Planner.

            ### Maneuver Quick-Tools

            New plan types: **Set Periapsis** and *Set Apoapsis*.

            - one
            - two

            `code` and [a link](https://example.com/x).
            """);

        Assert.Contains("<h3", html, StringComparison.Ordinal);
        Assert.Contains("Maneuver Quick-Tools", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Set Periapsis</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>Set Apoapsis</em>", html, StringComparison.Ordinal);
        Assert.Contains("<li>one</li>", html, StringComparison.Ordinal);
        Assert.Contains("<code>code</code>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/x\"", html, StringComparison.Ordinal);

        // The literal markers must be gone; leaving them is the bug this fixes.
        Assert.DoesNotContain("### Maneuver", html, StringComparison.Ordinal);
        Assert.DoesNotContain("**Set Periapsis**", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=\"https://evil.test\"></iframe>")]
    [InlineData("<a href=\"https://evil.test\" onclick=\"alert(1)\">x</a>")]
    [InlineData("<svg/onload=alert(1)>")]
    [InlineData("<style>body{display:none}</style>")]
    public void Raw_html_never_survives_the_parse(string markdown)
    {
        var html = Html(markdown);

        // The property that matters is escaping, not absence. DisableHtml turns the input into
        // text, so "&lt;img src=x onerror=alert(1)&gt;" is a correct and completely inert result -
        // and it does contain the substring "onerror=". Asserting the substring is missing would
        // be asserting the wrong thing, and would pass for a renderer that silently dropped the
        // content instead of showing it.
        //
        // So: the author's angle bracket must arrive escaped, and no tag may open.
        Assert.Contains("&lt;", html, StringComparison.Ordinal);

        foreach (var tag in (string[])["<script", "<img", "<iframe", "<svg", "<style", "<a "])
        {
            Assert.DoesNotContain(tag, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    public void A_link_carrying_a_dangerous_scheme_loses_its_url(string url)
    {
        // Markdig parses this into an ordinary link and renders a working one; nothing in the
        // library stops it. The whitespace and control-character cases matter because a browser
        // ignores those and executes anyway, so the string checked has to be the one it acts on.
        var html = Html($"[click me]({url})");

        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:text/html", html, StringComparison.OrdinalIgnoreCase);

        // The text survives; only the destination is dropped. A reader still sees what was written.
        Assert.Contains("click me", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_source_is_checked_the_same_way_as_a_link()
    {
        Assert.DoesNotContain("javascript", Html("![x](javascript:alert(1))"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.com/a.png", Html("![x](https://example.com/a.png)"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com/a")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("/mods/StarMap")]
    [InlineData("../releases/1.0.0")]
    [InlineData("docs/guide.md")]
    [InlineData("path/to:file")]
    public void Ordinary_urls_still_work(string url)
    {
        // The last one is the case a naive scheme check gets wrong: the colon is inside the path,
        // so there is no scheme and it is an ordinary relative link.
        Assert.True(SafeMarkdown.IsSafeUrl(url), url);
    }

    [Fact]
    public void Empty_input_renders_nothing_rather_than_throwing()
    {
        Assert.Equal("", SafeMarkdown.ToHtml(null).Value);
        Assert.Equal("", SafeMarkdown.ToHtml("   ").Value);
    }
}
