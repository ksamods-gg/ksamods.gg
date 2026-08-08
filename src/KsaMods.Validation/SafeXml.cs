using System.Xml;
using System.Xml.Linq;

namespace KsaMods.Validation;

/// <summary>
/// XML reading hardened per backend.md §14.3.
///
/// <para>Closes XXE, billion-laughs and external entity fetches. The last of those would
/// otherwise be a second SSRF surface reachable from inside a mod archive — the container having
/// no network makes it unexploitable rather than merely mitigated, which is defence in depth
/// working as intended, not a reason to skip this.</para>
/// </summary>
public static class SafeXml
{
    private const int MaxDepth = 64;

    private static XmlReaderSettings CreateSettings() => new()
    {
        // No DTD at all: no external subset to fetch, no internal subset to expand.
        DtdProcessing = DtdProcessing.Prohibit,

        // No resolver: nothing can be pulled in by URI even if a DTD somehow appeared.
        XmlResolver = null,

        MaxCharactersFromEntities = 1024,
        MaxCharactersInDocument = 64L * 1024 * 1024,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
    };

    /// <summary>
    /// Parses a document, returning null and the reason on failure. Never throws for malformed
    /// input: malformed XML is a finding an author needs to see, not an exception to propagate.
    /// </summary>
    public static XDocument? TryLoad(Stream stream, out string? error)
    {
        error = null;
        try
        {
            using var reader = XmlReader.Create(stream, CreateSettings());
            var document = XDocument.Load(reader, LoadOptions.None);

            if (Depth(document.Root) > MaxDepth)
            {
                error = $"Element nesting exceeds the {MaxDepth} level limit.";
                return null;
            }

            return document;
        }
        catch (XmlException ex)
        {
            error = $"Line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}";
            return null;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static int Depth(XElement? element)
    {
        if (element is null) return 0;
        var deepest = 0;
        foreach (var child in element.Elements())
        {
            deepest = Math.Max(deepest, Depth(child));
        }
        return deepest + 1;
    }
}
