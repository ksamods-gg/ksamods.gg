namespace KsaMods.Api;

/// <summary>
/// Self-probe for the container healthcheck.
///
/// <para>The runtime image is chiselled, so there is no shell and no HTTP client to invoke from a
/// HEALTHCHECK line. Shipping curl into the image to solve that would add a binary - and an attack
/// surface - for the sake of one request the app can make itself.</para>
/// </summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync(string ports)
    {
        // ASPNETCORE_HTTP_PORTS may list several; the first is the one to probe.
        var port = ports.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "8080";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/health");

            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
