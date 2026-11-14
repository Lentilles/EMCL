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

/// <summary>Negotiates confirmed LN associations with legacy authentication or a supplied security protocol.</summary>
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
        var initiate = new InitiateRequest(proposal.Security?.DedicatedKey ?? [], proposal.ResponseAllowed, null, DlmsNegotiation.Version6,
            (uint)(proposal.AdvertisedConformance ?? proposal.Conformance), proposal.MaxReceivePduSize);
        var clear = codec.EncodeBytes(initiate);
        if (proposal.Security is { } security)
        {
            try
            {
                return AcseApdu.Aarq(security.Protect(clear, false), security.ApplicationContext,
                    security.AuthenticationMechanism, security.CreateChallenge(), security.LocalSystemTitle.AsSpan());
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(clear);
            }
        }

        var authenticationValue = proposal.Authentication == ClientAuthentication.High
            ? HighAesAuthentication.NewChallenge()
            : proposal.Password.IsDefault ? [] : proposal.Password.ToArray();
        return AcseApdu.Aarq(clear,
            authenticationMechanism: proposal.Authentication == ClientAuthentication.None ? null : (byte)proposal.Authentication,
            authenticationValue: authenticationValue);
    }

    /// <inheritdoc />
    public NegotiatedAssociation AcceptResponse(AcseApdu response, LnAssociationOptions proposal)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(proposal);
        proposal.Validate();
        ValidateAare(response, proposal);

        var initiate = DecodeInitiateResponse(response, proposal);
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

    private static void ValidateAare(AcseApdu response, LnAssociationOptions proposal)
    {
        bool authenticationPending = (proposal.Authentication == ClientAuthentication.High || proposal.Security is not null)
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

        var proposedContext = AcseApdu.Aarq([], proposal.Security?.ApplicationContext ?? 1).ApplicationContext!.Value;
        if (response.ApplicationContext is not { } context || !context.AsSpan().SequenceEqual(proposedContext.AsSpan()))
        {
            throw new DlmsAssociationException("The peer selected a different application context.", response);
        }

        if (proposal.Security is { } security)
        {
            if (response.SystemTitle is { } title && !title.AsSpan().SequenceEqual(security.PeerSystemTitle.AsSpan()))
            {
                throw new DlmsAssociationException("The peer supplied a different system title.", response);
            }

            var expected = AcseApdu.Aarq([], security.ApplicationContext, security.AuthenticationMechanism)
                .Find(AcseFieldTags.MechanismName)!;
            if (response.Find(AcseFieldTags.RespondingMechanismName) is { } mechanism
                && !mechanism.Content.AsSpan().SequenceEqual(expected.Content.AsSpan()))
            {
                throw new DlmsAssociationException("The peer selected a different authentication mechanism.", response);
            }

            if (response.RespondingAuthenticationValue is not { Length: > 0 })
            {
                throw new DlmsAssociationException("The peer did not supply authentication data.", response);
            }
        }
    }

    internal InitiateResponse DecodeInitiateResponse(AcseApdu response, LnAssociationOptions proposal, bool established = false)
    {
        if (response.UserInformation is not { } information)
        {
            throw new DlmsAssociationException("The AARE does not contain an InitiateResponse.", response);
        }

        byte[] clear = proposal.Security is { } security ? security.Unprotect(information.AsSpan(), established) : information.ToArray();
        var decoded = codec.Decode(new ReadOnlySequence<byte>(clear));
        if (decoded.Status != DecodeStatus.Done || decoded.Consumed != clear.Length || decoded.Value is not InitiateResponse initiate)
        {
            throw new DlmsAssociationException("The AARE user information is not one complete InitiateResponse.", response);
        }

        return initiate;
    }

    /// <inheritdoc />
    public CosemOperation? CreateAuthenticationOperation(AcseApdu request, AcseApdu response, LnAssociationOptions proposal)
    {
        byte[] reply;
        if (proposal.Security is { } security)
        {
            reply = security.CreateReply(request.CallingAuthenticationValue!.Value.AsSpan(),
                response.RespondingAuthenticationValue!.Value.AsSpan());
        }
        else if (proposal.Authentication == ClientAuthentication.High)
        {
            var challenge = HighAesAuthentication.RequireChallenge(response.RespondingAuthenticationValue, response);
            reply = HighAesAuthentication.Encrypt(proposal.Password.AsSpan(), challenge);
        }
        else
        {
            return null;
        }

        return new(CosemOperationKind.Invoke, CosemClassIds.AssociationLn, LogicalName.Parse("0.0.40.0.0.255"),
            AssociationLnMembers.Methods.ReplyToHlsAuthentication, DlmsValue.Octets(reply));
    }

    /// <inheritdoc />
    public void ValidateAuthenticationResponse(DlmsValue? value, AcseApdu request, AcseApdu response, LnAssociationOptions proposal)
    {
        if (proposal.Security is { } security)
        {
            if (value is not { Type: DlmsType.OctetString })
            {
                throw new DlmsAssociationException("The HLS ACTION did not return authentication octets.", response);
            }

            security.VerifyReply(request.CallingAuthenticationValue!.Value.AsSpan(),
                response.RespondingAuthenticationValue!.Value.AsSpan(), value.Bytes.AsSpan());
            return;
        }

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

    internal AcseApdu CreateReleaseRequest(LnAssociationOptions proposal)
    {
        var request = CreateReleaseRequest();
        if (proposal.Security is not { } security || !proposal.ProtectRelease)
        {
            return request;
        }

        var initiate = new InitiateRequest([], proposal.ResponseAllowed, null, DlmsNegotiation.Version6,
            (uint)(proposal.AdvertisedConformance ?? proposal.Conformance), proposal.MaxReceivePduSize);
        return request with
        {
            Fields = [.. request.Fields, AcseApdu.OctetString(AcseFieldTags.UserInformation,
                security.Protect(codec.EncodeBytes(initiate), true))]
        };
    }

    internal void AcceptReleaseResponse(AcseApdu response, LnAssociationOptions proposal)
    {
        AcceptReleaseResponse(response);
        if (proposal.Security is not null && proposal.ProtectRelease)
        {
            _ = DecodeInitiateResponse(response, proposal, established: true);
        }
    }

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
