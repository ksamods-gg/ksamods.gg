namespace KsaMods.Web.Components.Ui;

/// <summary>
/// Class-list helper, the Blazor equivalent of shadcn's <c>cn()</c>.
///
/// <para>Joins non-empty fragments and drops nulls, so a component can build its class list from
/// conditional expressions without producing double spaces or a stray "null" in the DOM.</para>
///
/// <para><b>It does not do Tailwind conflict resolution</b> (there is no tailwind-merge here).
/// Components put the caller's <c>class</c> last, and that settles nothing on its own: two
/// utilities of equal specificity are resolved by their order in the stylesheet, not by their
/// order in the attribute. Passing <c>hidden</c> to a component whose base list already sets
/// <c>inline-flex</c> may or may not hide it, depending only on how Tailwind sorted that pair.</para>
///
/// <para>So a caller may safely add a property the component does not already set, and may
/// override one with a <i>variant</i> (<c>sm:</c>, <c>hover:</c>), which is emitted later and does
/// win. To override a plain property the component already sets, change the component or wrap it:
/// <c>&lt;span class="hidden sm:contents"&gt;</c> is the usual way to hide one without disturbing
/// the layout around it.</para>
/// </summary>
public static class Cx
{
    public static string Of(params string?[] fragments) =>
        string.Join(' ', fragments.Where(f => !string.IsNullOrWhiteSpace(f)));
}
