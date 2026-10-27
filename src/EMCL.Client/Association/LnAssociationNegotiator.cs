using System.Buffers;
using EMCL.Client.Exceptions;
using EMCL.Core.Codecs;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Association;

/// <summary>Negotiates confirmed, unciphered LN associations with optional low-level authentication.</summary>
public sealed class LnAssociationNegotiator : IClientAssociationNegotiator
{
    private const byte AcceptedResult = 0;
    private const byte NoDiagnostic = 0;
    private const byte NormalReleaseReason = 0;
    private readonly ApduCodec codec;

    /// <summary>Creates a negotiator using shared protocol resource bounds.</summary>
    /// <param name="limits">The protocol limits; null selects validated defaults.</param>
    public LnAssociationNegotiator(ProtocolLimits? limits = null) => codec = new(limits);

    /// <inheritdoc />
    public AcseApdu CreateRequest(LnAssociationOptions proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        proposal.Validate();
        var initiate = new InitiateRequest([], true, null, DlmsNegotiation.Version6,
            (uint)proposal.Conformance, proposal.MaxReceivePduSize);
        return AcseApdu.Aarq(codec.EncodeBytes(initiate),
            authenticationMechanism: proposal.Authentication == ClientAuthentication.None ? null : (byte)proposal.Authentication,
            authenticationValue: proposal.Password.IsDefault ? [] : proposal.Password.AsSpan());
    }

    /// <inheritdoc />
    public NegotiatedAssociation AcceptResponse(AcseApdu response, LnAssociationOptions proposal)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(proposal);
        proposal.Validate();
        if (response.Kind != AcseKind.Aare || response.Result != AcceptedResult || response.Diagnostic?.Code != NoDiagnostic)
        {
            throw new DlmsAssociationException("The peer rejected the association or requires an unsupported authentication exchange.", response);
        }

        var proposedContext = AcseApdu.Aarq([]).ApplicationContext!.Value;
        if (response.ApplicationContext is not { } context || !context.AsSpan().SequenceEqual(proposedContext.AsSpan()))
        {
            throw new DlmsAssociationException("The peer selected a different application context.", response);
        }

        if (response.UserInformation is not { } information)
        {
            throw new DlmsAssociationException("The AARE does not contain an InitiateResponse.", response);
        }

        var decoded = codec.Decode(new ReadOnlySequence<byte>(information.ToArray()));
        if (decoded.Status != DecodeStatus.Done || decoded.Consumed != information.Length || decoded.Value is not InitiateResponse initiate)
        {
            throw new DlmsAssociationException("The AARE user information is not one complete InitiateResponse.", response);
        }

        var conformance = (DlmsConformance)initiate.Conformance;
        if (initiate.DlmsVersion != DlmsNegotiation.Version6 || initiate.VaaName != DlmsNegotiation.LogicalNameVaa
            || (conformance & ~proposal.Conformance) != DlmsConformance.None
            || initiate.MaxReceivePduSize != DlmsNegotiation.UnlimitedPduSize && initiate.MaxReceivePduSize < DlmsNegotiation.MinimumPduSize)
        {
            throw new DlmsAssociationException("The negotiated version, LN VAA, conformance, or PDU size is incompatible with the proposal.", response);
        }

        return new(initiate.DlmsVersion, conformance, initiate.MaxReceivePduSize, proposal.MaxReceivePduSize);
    }

    /// <inheritdoc />
    public AcseApdu CreateReleaseRequest() => AcseApdu.Release();

    /// <inheritdoc />
    public void AcceptReleaseResponse(AcseApdu response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var reason = response.Find(AcseFieldTags.ReleaseReason);
        if (response.Kind != AcseKind.Rlre || reason != null
            && (reason.Content.Length != sizeof(byte) || reason.Content[0] != NormalReleaseReason))
        {
            throw new DlmsAssociationException("The peer did not confirm a normal association release.", response);
        }
    }
}
