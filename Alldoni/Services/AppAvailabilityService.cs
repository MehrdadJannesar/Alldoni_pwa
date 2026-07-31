using Alldoni.Models;

namespace Alldoni.Services;

public sealed class AppAvailabilityService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public async Task<IReadOnlyDictionary<string, bool>> CheckAsync(
        IEnumerable<AppEntry> applications,
        CancellationToken cancellationToken)
    {
        var checks = applications.Select(application => CheckAsync(application, cancellationToken));
        return (await Task.WhenAll(checks))
            .ToDictionary(result => result.Key, result => result.Available, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<(string Key, bool Available)> CheckAsync(
        AppEntry application,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(application.Url, UriKind.Absolute, out var uri))
        {
            return (application.Key, false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return (application.Key, (int)response.StatusCode < 500);
        }
        catch (HttpRequestException)
        {
            return (application.Key, false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (application.Key, false);
        }
    }
}
