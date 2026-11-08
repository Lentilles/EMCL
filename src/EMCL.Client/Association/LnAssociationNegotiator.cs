using System.Buffers;
using EMCL.Client.Exceptions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Association;

/// <summary>Negotiates confirmed, unciphered LN associations with optional low-level or AES high-level authentication.</summary>
public sealed class LnAssociationNegotiator : IClientAssociationNegotiator, IClientHighLevelAuthenticator
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
        var initiate = new InitiateRequest([], proposal.ResponseAllowed, null, DlmsNegotiation.Version6,
            (uint)(proposal.AdvertisedConformance ?? proposal.Conformance), proposal.MaxReceivePduSize);
        var authenticationValue = proposal.Authentication == ClientAuthentication.High
            ? HighAesAuthentication.NewChallenge()
            : proposal.Password.IsDefault ? [] : proposal.Password.ToArray();
        return AcseApdu.Aarq(codec.EncodeBytes(initiate),
            authenticationMechanism: proposal.Authentication == ClientAuthentication.None ? null : (byte)proposal.Authentication,
            authenticationValue: authenticationValue);
    }

    /// <inheritdoc />
    public NegotiatedAssociation AcceptResponse(AcseApdu response, LnAssociationOptions proposal)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(proposal);
        proposal.Validate();
        ValidateAare(response, proposal.Authentication);

        var initiate = DecodeInitiateResponse(response);
        var conformance = (DlmsConformance)initiate.Conformance;
        var allowedConformance = proposal.AdvertisedConformance ?? proposal.Conformance;
        if (proposal.AcceptUnexpectedBlockTransferWithSet)
        {
            allowedConformance |= DlmsConformance.BlockTransferWithSet;
        }

        if (initiate.DlmsVersion != DlmsNegotiation.Version6 || initiate.VaaName != DlmsNegotiation.LogicalNameVaa
            || (conformance & ~allowedConformance) != DlmsConformance.None
            || initiate.MaxReceivePduSize != DlmsNegotiation.UnlimitedPduSize && initiate.MaxReceivePduSize < DlmsNegotiation.MinimumPduSize)
        {
            throw new DlmsAssociationException("The negotiated version, LN VAA, conformance, or PDU size is incompatible with the proposal.", response);
        }

        return new(initiate.DlmsVersion, conformance & proposal.Conformance,
            initiate.MaxReceivePduSize, proposal.MaxReceivePduSize);
    }

    private static void ValidateAare(AcseApdu response, ClientAuthentication authentication)
    {
        bool authenticationPending = authentication == ClientAuthentication.High
            && response.Diagnostic?.Code == (byte)AcseServiceUserDiagnostic.AuthenticationRequired;
        if (response.Kind != AcseKind.Aare || response.Result != AcceptedResult
            || response.Diagnostic?.Code != NoDiagnostic && !authenticationPending)
        {
            var diagnostic = response.Diagnostic;
            string reason = diagnostic?.SourceTag switch
            {
                AcseFieldTags.DiagnosticServiceUser => Enum.GetName((AcseServiceUserDiagnostic)diagnostic.Code) ?? "Unknown",
                AcseFieldTags.DiagnosticServiceProvider => Enum.GetName((AcseServiceProviderDiagnostic)diagnostic.Code) ?? "Unknown",
                _ => "Unknown"
            };
            throw new DlmsAssociationException($"stage=AARE; association rejected: result={response.Result?.ToString() ?? "missing"}, "
                + $"diagnostic={diagnostic?.Code.ToString() ?? "missing"} ({reason}).", response);
        }

        var proposedContext = AcseApdu.Aarq([]).ApplicationContext!.Value;
        if (response.ApplicationContext is not { } context || !context.AsSpan().SequenceEqual(proposedContext.AsSpan()))
        {
            throw new DlmsAssociationException("The peer selected a different application context.", response);
        }
    }

    private InitiateResponse DecodeInitiateResponse(AcseApdu response)
    {
        if (response.UserInformation is not { } information)
        {
            throw new DlmsAssociationException("The AARE does not contain an InitiateResponse.", response);
        }

        var decoded = codec.Decode(new ReadOnlySequence<byte>(information.ToArray()));
        if (decoded.Status != DecodeStatus.Done || decoded.Consumed != information.Length || decoded.Value is not InitiateResponse initiate)
        {
            throw new DlmsAssociationException("The AARE user information is not one complete InitiateResponse.", response);
        }

        return initiate;
    }

    /// <inheritdoc />
    public CosemOperation? CreateAuthenticationOperation(AcseApdu request, AcseApdu response, LnAssociationOptions proposal)
    {
        if (proposal.Authentication != ClientAuthentication.High)
        {
            return null;
        }

        var challenge = HighAesAuthentication.RequireChallenge(response.RespondingAuthenticationValue, response);
        var reply = HighAesAuthentication.Encrypt(proposal.Password.AsSpan(), challenge);
        return new(CosemOperationKind.Invoke, CosemClassIds.AssociationLn, LogicalName.Parse("0.0.40.0.0.255"),
            AssociationLnMembers.Methods.ReplyToHlsAuthentication, DlmsValue.Octets(reply));
    }

    /// <inheritdoc />
    public void ValidateAuthenticationResponse(DlmsValue? value, AcseApdu request, AcseApdu response, LnAssociationOptions proposal)
    {
        if (proposal.Authentication != ClientAuthentication.High)
        {
            return;
        }

        var challenge = HighAesAuthentication.RequireChallenge(request.CallingAuthenticationValue, response);
        var expected = HighAesAuthentication.Encrypt(proposal.Password.AsSpan(), challenge);
        HighAesAuthentication.Verify(value, expected, response);
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
