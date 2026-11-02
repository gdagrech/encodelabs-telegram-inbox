namespace EncodeLabs.Application.Abstractions.Tunnelling;

public interface IPublicTunnelUrlProvider
{
    /// <summary>
    /// Discovers the public HTTPS base address currently exposed by the tunnel in front of this API.
    /// The address changes on every tunnel restart, so it is read at start-up rather than configured.
    /// </summary>
    /// <returns>The public base address, or <see langword="null"/> when no HTTPS tunnel is available yet.</returns>
    Task<Uri?> TryGetPublicHttpsBaseAddressAsync(CancellationToken cancellationToken);
}
