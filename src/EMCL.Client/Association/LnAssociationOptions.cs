using System.Collections.Immutable;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Association;

/// <summary>Configures an unciphered, confirmed LN association proposal.</summary>
public sealed record LnAssociationOptions
{
    private const uint MaximumAdvertisedConformance = 0xFFFFFF;

    /// <summary>The default advertised maximum receive PDU length.</summary>
    public const ushort DefaultMaxReceivePduSize = 1024;
    /// <summary>The required secret length in octets for the built-in AES high-level mechanism.</summary>
    public const int HighSecretLength = 16;
    /// <summary>The conformance subset implemented by the built-in client.</summary>
    public const DlmsConformance SupportedConformance = DlmsConformance.Get | DlmsConformance.Set
        | DlmsConformance.Action | DlmsConformance.SelectiveAccess | DlmsConformance.BlockTransferWithGet
        | DlmsConformance.BlockTransferWithSet
        | DlmsConformance.DataNotification | DlmsConformance.PriorityManagement;

    /// <summary>Gets the requested services; the default includes all implemented services except priority management.</summary>
    public DlmsConformance Conformance { get; init; } = SupportedConformance & ~DlmsConformance.PriorityManagement;
    /// <summary>Optional wire-level conformance proposal. Extra service bits are advertised but are not exposed as implemented operations.</summary>
    public DlmsConformance? AdvertisedConformance
    {
        get; init;
    }
    /// <summary>Gets the optional response-allowed flag; null omits it and uses the protocol default (true).</summary>
    public bool? ResponseAllowed
    {
        get; init;
    }
    /// <summary>Allows a peer's unrequested SET block-transfer bit in AARE while excluding it from usable negotiated services.</summary>
    public bool AcceptUnexpectedBlockTransferWithSet
    {
        get; init;
    }
    /// <summary>Gets the advertised client receive limit; zero means unlimited by the protocol, while local resource limits still apply.</summary>
    public ushort MaxReceivePduSize { get; init; } = DefaultMaxReceivePduSize;
    /// <summary>Gets the authentication mechanism; no authentication is the default.</summary>
    public ClientAuthentication Authentication
    {
        get; init;
    }
    /// <summary>Gets immutable authentication secret octets; they are never added to diagnostics.</summary>
    public ImmutableArray<byte> Password { get; init; } = [];
    /// <summary>Overrides the Invoke-Id-And-Priority octet for the HLS ACTION exchange.</summary>
    /// <remarks>Null uses the session's normal invoke allocation and priority. An explicit value must encode a
    /// confirmed service with zero reserved bits. It can select high priority for HLS without negotiating PriorityManagement.
    /// The override does not use an ID from the session's normal allocation sequence.</remarks>
    public byte? HlsInvokeIdAndPriority
    {
        get; init;
    }

    internal void Validate()
    {
        ValidateConformance();

        if (ResponseAllowed == false)
        {
            throw new ArgumentException("An association request must allow a response.", nameof(ResponseAllowed));
        }

        if (MaxReceivePduSize != DlmsNegotiation.UnlimitedPduSize && MaxReceivePduSize < DlmsNegotiation.MinimumPduSize)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReceivePduSize));
        }

        ValidateInvokeIdAndPriority(HlsInvokeIdAndPriority, nameof(HlsInvokeIdAndPriority));
        ValidateAuthentication();
    }

    internal static void ValidateInvokeIdAndPriority(byte? value, string paramName)
    {
        const byte confirmed = InvokeIdEncoding.ConfirmedService;
        const byte allowedBits = confirmed | InvokeIdEncoding.IdMask | InvokeIdEncoding.HighPriority;
        if (value is byte invoke && ((invoke & confirmed) == 0 || (invoke & ~allowedBits) != 0))
        {
            throw new ArgumentOutOfRangeException(paramName,
                "The invoke-id-and-priority must encode a confirmed service with zero reserved bits.");
        }
    }

    private void ValidateConformance()
    {
        if ((Conformance & ~SupportedConformance) != DlmsConformance.None)
        {
            throw new NotSupportedException("The client proposal contains unsupported conformance flags.");
        }

        if (AdvertisedConformance is { } advertised
            && ((uint)advertised > MaximumAdvertisedConformance || (Conformance & ~advertised) != DlmsConformance.None))
        {
            throw new ArgumentException("The advertised conformance must be a 24-bit value containing all implemented requested services.",
                nameof(AdvertisedConformance));
        }
    }

    private void ValidateAuthentication()
    {
        if (!Enum.IsDefined(Authentication))
        {
            throw new NotSupportedException("The selected authentication mechanism is not implemented.");
        }

        if (Authentication == ClientAuthentication.None && !Password.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A password requires an authentication mechanism.");
        }

        if (Authentication != ClientAuthentication.High)
        {
            return;
        }

        if (Password.IsDefaultOrEmpty || Password.Length != HighSecretLength)
        {
            throw new ArgumentException($"High authentication requires a {HighSecretLength}-byte secret.");
        }

        if ((Conformance & DlmsConformance.Action) == DlmsConformance.None)
        {
            throw new ArgumentException("High authentication requires Action conformance.");
        }
    }
}
