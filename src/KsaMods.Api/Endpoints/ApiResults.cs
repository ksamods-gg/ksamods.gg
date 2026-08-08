namespace KsaMods.Api.Endpoints;

/// <summary>
/// Shared results for the outcomes the permission layer produces.
/// </summary>
public static class ApiResults
{
    /// <summary>
    /// A 403, as problem details.
    ///
    /// <para><c>Results.Forbid()</c> cannot be used anywhere in this API. It delegates to
    /// <c>IAuthenticationService</c>, and this app has no authentication handlers registered: the
    /// session cookie is resolved by <c>CurrentUserMiddleware</c>, not by ASP.NET Core's
    /// authentication stack. Calling it throws <c>InvalidOperationException</c>, which the
    /// exception handler turns into a 500.</para>
    ///
    /// <para>The failure mode was quiet in the worst way. Every permission check in the API
    /// returned <c>Results.Forbid()</c>, so the one path that only runs when a caller is refused
    /// was also the one path that crashed. Anybody hitting a permission boundary got "an error
    /// occurred" instead of being told they were not allowed, and the logs recorded it as a
    /// server fault rather than a denied request.</para>
    /// </summary>
    public static IResult Forbidden(string? detail = null) =>
        Results.Problem(
            title: "Forbidden",
            detail: detail ?? "You don't have permission to do that.",
            statusCode: StatusCodes.Status403Forbidden);
}
