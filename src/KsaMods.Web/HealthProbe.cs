namespace KsaMods.Web;

/// <summary>
/// Self-probe for the container healthcheck.
///
/// <para>The runtime image is chiselled — no shell, no curl, no wget — so the only thing that can
/// make an HTTP request inside the container is the app. It probes its own <c>/health</c>, which
/// is deliberately liveness-only: it does not reach the API, so a backend blip cannot cause the
/// orchestrator to restart the frontend and turn one outage into two.</para>
/// </summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync(string ports)
    {
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
