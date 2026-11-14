using System.Collections.Immutable;

namespace EMCL.Protocol.Security;

/// <summary>Supplies symmetric keys, system titles and protection policy for one application association.</summary>
public sealed class DlmsSecurityOptions
{
    /// <summary>Gets the selected symmetric suite.</summary>
    public DlmsSecuritySuite Suite
    {
        get; init;
    }

    /// <summary>Gets the local eight-octet system title.</summary>
    public required ImmutableArray<byte> LocalSystemTitle
    {
        get; init;
    }

    /// <summary>Gets the expected peer's eight-octet system title.</summary>
    public required ImmutableArray<byte> PeerSystemTitle
    {
        get; init;
    }

    /// <summary>Gets the 16-octet AES key or 64-octet Kuznyechik encryption and MAC key pair.</summary>
    public required ImmutableArray<byte> GlobalKey
    {
        get; init;
    }

    /// <summary>Gets the 16-octet AES authentication key. Kuznyechik uses the second half of its combined key.</summary>
    public ImmutableArray<byte> AuthenticationKey { get; init; } = [];

    /// <summary>Gets the optional association-specific key in the same format as the global key.</summary>
    public ImmutableArray<byte> DedicatedKey { get; init; } = [];

    /// <summary>Gets the optional key-encryption key in the same format as the global key.</summary>
    public ImmutableArray<byte> MasterKey { get; init; } = [];

    /// <summary>Gets the shared invocation counter store used for protection and key transfer.</summary>
    public required IInvocationCounterStore Counters
    {
        get; init;
    }

    /// <summary>Gets the outgoing protection policy, which defaults to authentication and encryption.</summary>
    public ApduProtectionMode OutgoingMode { get; init; } = ApduProtectionMode.AuthenticationAndEncryption;

    /// <summary>Gets the minimum accepted incoming protection, which defaults to authentication and encryption.</summary>
    public ApduProtectionMode MinimumIncomingMode { get; init; } = ApduProtectionMode.AuthenticationAndEncryption;

    /// <summary>Gets whether to use general ciphering for established traffic with a service-specific tag available.</summary>
    public bool UseGeneralCiphering
    {
        get; init;
    }
}
