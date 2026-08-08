namespace KsaMods.Web.Components.Ui;

/// <summary>
/// Class-list helper, the Blazor equivalent of shadcn's <c>cn()</c>.
///
/// <para>Joins non-empty fragments and drops nulls, so a component can build its class list from
/// conditional expressions without producing double spaces or a stray "null" in the DOM. It does
/// not do Tailwind conflict resolution (there is no tailwind-merge here) — components put the
/// caller's <c>class</c> last so a caller's utility wins by source order.</para>
/// </summary>
public static class Cx
{
    public static string Of(params string?[] fragments) =>
        string.Join(' ', fragments.Where(f => !string.IsNullOrWhiteSpace(f)));
}
