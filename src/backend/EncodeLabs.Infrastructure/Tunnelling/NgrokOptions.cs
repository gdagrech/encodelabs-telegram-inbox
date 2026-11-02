using System.ComponentModel.DataAnnotations;

namespace EncodeLabs.Infrastructure.Tunnelling;

public sealed class NgrokOptions
{
    public const string ConfigurationSectionName = "Ngrok";

    /// <summary>
    /// Address of the ngrok agent's local inspection API inside the Compose network. The agent
    /// publishes the tunnel's public address there, which is the only way to learn the URL that
    /// ngrok assigned on this start-up.
    /// </summary>
    [Required]
    public Uri AgentApiBaseAddress { get; init; } = new("http://ngrok:4040");
}
