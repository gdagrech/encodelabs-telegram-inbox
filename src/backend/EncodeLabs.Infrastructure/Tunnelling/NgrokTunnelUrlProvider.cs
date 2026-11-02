using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EncodeLabs.Application.Abstractions.Tunnelling;
using Microsoft.Extensions.Logging;

namespace EncodeLabs.Infrastructure.Tunnelling;

/// <summary>
/// Reads the public HTTPS address from the ngrok agent's local inspection API.
/// </summary>
public sealed class NgrokTunnelUrlProvider(
    HttpClient httpClient,
    ILogger<NgrokTunnelUrlProvider> logger) : IPublicTunnelUrlProvider
{
    private const string TunnelsResourcePath = "api/tunnels";

    public async Task<Uri?> TryGetPublicHttpsBaseAddressAsync(CancellationToken cancellationToken)
    {
        try
        {
            NgrokTunnelListResponse? tunnelList = await httpClient.GetFromJsonAsync<NgrokTunnelListResponse>(
                TunnelsResourcePath,
                cancellationToken);

            if (tunnelList?.Tunnels is null || tunnelList.Tunnels.Count == 0)
            {
                logger.LogInformation("The ngrok agent is reachable but has not opened a tunnel yet.");
                return null;
            }

            string? publicHttpsUrl = tunnelList.Tunnels
                .Select(tunnel => tunnel.PublicUrl)
                .FirstOrDefault(candidateUrl =>
                    candidateUrl is not null
                    && candidateUrl.StartsWith(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

            if (publicHttpsUrl is null)
            {
                logger.LogWarning(
                    "The ngrok agent exposes {TunnelCount} tunnel(s) but none of them uses HTTPS, which Telegram requires.",
                    tunnelList.Tunnels.Count);

                return null;
            }

            return new Uri(publicHttpsUrl, UriKind.Absolute);
        }
        catch (Exception tunnelLookupException)
            when (tunnelLookupException is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            logger.LogInformation(
                "The ngrok agent API at {AgentApiBaseAddress} is not answering yet ({FailureReason}).",
                httpClient.BaseAddress,
                tunnelLookupException.Message);

            return null;
        }
    }

    private sealed record NgrokTunnelListResponse(
        [property: JsonPropertyName("tunnels")] IReadOnlyList<NgrokTunnel>? Tunnels);

    private sealed record NgrokTunnel(
        [property: JsonPropertyName("public_url")] string? PublicUrl,
        [property: JsonPropertyName("proto")] string? Protocol);
}
