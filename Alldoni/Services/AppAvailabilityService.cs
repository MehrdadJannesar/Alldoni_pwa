using System.Net.Sockets;
using Alldoni.Models;

namespace Alldoni.Services;

public sealed class AppAvailabilityService
{
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

        var port = uri.IsDefaultPort
            ? uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(uri.Host, port, timeout.Token);
            return (application.Key, client.Connected);
        }
        catch (Exception exception) when (exception is SocketException
            or TimeoutException
            or OperationCanceledException)
        {
            return (application.Key, false);
        }
    }
}
