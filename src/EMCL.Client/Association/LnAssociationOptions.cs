using System.Collections.Immutable;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Association;

/// <summary>Configures an unciphered, confirmed LN association proposal.</summary>
public sealed record LnAssociationOptions
{
    /// <summary>The default advertised maximum receive PDU length.</summary>
    public const ushort DefaultMaxReceivePduSize = 1024;
    /// <summary>The conformance subset implemented by the built-in client.</summary>
    public const DlmsConformance SupportedConformance = DlmsConformance.Get | DlmsConformance.Set
        | DlmsConformance.Action | DlmsConformance.SelectiveAccess | DlmsConformance.BlockTransferWithGet
        | DlmsConformance.DataNotification | DlmsConformance.PriorityManagement;

    /// <summary>Gets the requested services; the default includes all implemented services except priority management.</summary>
    public DlmsConformance Conformance { get; init; } = SupportedConformance & ~DlmsConformance.PriorityManagement;
    /// <summary>Gets the advertised client receive limit; zero means unlimited by the protocol, while local resource limits still apply.</summary>
    public ushort MaxReceivePduSize { get; init; } = DefaultMaxReceivePduSize;
    /// <summary>Gets the authentication mechanism; no authentication is the default.</summary>
    public ClientAuthentication Authentication
    {
        get; init;
    }
    /// <summary>Gets immutable low-level authentication octets; they are never added to diagnostics.</summary>
    public ImmutableArray<byte> Password { get; init; } = [];

    internal void Validate()
    {
        if ((Conformance & ~SupportedConformance) != DlmsConformance.None)
        {
            throw new NotSupportedException("The client proposal contains unsupported conformance flags.");
        }

        if (MaxReceivePduSize != DlmsNegotiation.UnlimitedPduSize && MaxReceivePduSize < DlmsNegotiation.MinimumPduSize)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReceivePduSize));
        }

        if (!Enum.IsDefined(Authentication))
        {
            throw new NotSupportedException("HLS and protected associations require a separate implementation.");
        }

        if (Authentication == ClientAuthentication.None && !Password.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A password requires low-level authentication.");
        }
    }
}
