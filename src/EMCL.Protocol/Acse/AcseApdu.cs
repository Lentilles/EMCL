using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Internal;

namespace EMCL.Protocol.Acse;

/// <summary>Represents an ACSE envelope while preserving the ordered BER fields, including authentication data.</summary>
/// <remarks>Authentication and user-information octets are represented as data; this type does not negotiate a session or verify security.</remarks>
/// <param name="Kind">The operation or envelope variant.</param>
/// <param name="Fields">The ordered BER fields within the envelope.</param>
public sealed record AcseApdu(AcseKind Kind, ImmutableArray<BerElement> Fields) : DlmsApdu
{
    /// <summary>Finds the first field with the supplied single-octet BER tag.</summary>
    /// <param name="tag">The single-octet BER field tag.</param>
    /// <returns>The first matching BER field, or null when absent.</returns>
    public BerElement? Find(byte tag) => Fields.FirstOrDefault(f => f.Tag == tag);
    /// <summary>Gets the octets inside the explicit user-information field, or null when it is absent.</summary>
    public ImmutableArray<byte>? UserInformation => Find(AcseFieldTags.UserInformation) is { } element ? Unwrap(element, BerEncoding.OctetStringTag) : null;
    /// <summary>Gets the application-context object-identifier octets, or null when the field is absent.</summary>
    public ImmutableArray<byte>? ApplicationContext => Find(AcseFieldTags.ApplicationContextName) is { } element ? Unwrap(element, BerEncoding.ObjectIdentifierTag) : null;
    /// <summary>Gets the server challenge in an AARE, or null when it is absent.</summary>
    public ImmutableArray<byte>? RespondingAuthenticationValue => Find(AcseFieldTags.RespondingAuthenticationValue) is { } element
        ? Unwrap(element, AcseFieldTags.AuthenticationGraphicString) : null;
    /// <summary>Gets the client authentication value in an AARQ, or null when it is absent.</summary>
    public ImmutableArray<byte>? CallingAuthenticationValue => Find(AcseFieldTags.CallingAuthenticationValue) is { } element
        ? Unwrap(element, AcseFieldTags.AuthenticationGraphicString) : null;

    /// <summary>Gets the raw AARE association-result code, or null when absent.</summary>
    public byte? Result => Find(AcseFieldTags.AssociationResult) is { } element
        ? SingleOctet(Unwrap(element, BerEncoding.IntegerTag)) : null;

    /// <summary>Gets the raw AARE diagnostic and its source tag, or null when absent.</summary>
    public AcseDiagnostic? Diagnostic
    {
        get
        {
            if (Find(AcseFieldTags.ResultSourceDiagnostic) is not { } element)
            {
                return null;
            }

            var reader = new WireReader(element.Content.AsSpan());
            byte source = reader.Byte();
            if (source is not (AcseFieldTags.DiagnosticServiceUser or AcseFieldTags.DiagnosticServiceProvider))
            {
                throw new InvalidDataException("Invalid ACSE diagnostic source.");
            }

            var inner = new BerElement(source, reader.Take(reader.Length(element.Content.Length)).ToArray().ToImmutableArray());
            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Trailing ACSE diagnostic data.");
            }

            return new(source, SingleOctet(Unwrap(inner, BerEncoding.IntegerTag)));
        }
    }

    private static byte SingleOctet(ImmutableArray<byte> value) => value.Length == sizeof(byte)
        ? value[0] : throw new InvalidDataException("A one-octet ACSE integer is required.");

    internal static ImmutableArray<byte> Unwrap(BerElement field, byte tag)
    {
        var r = new WireReader(field.Content.AsSpan());
        if (r.Byte() != tag)
        {
            throw new WireException(DecodeStatus.InvalidData, "Unexpected explicit BER field tag.");
        }

        var content = r.Take(r.Length(field.Content.Length)).ToArray().ToImmutableArray();
        if (r.Remaining != 0)
        {
            throw new WireException(DecodeStatus.InvalidData, "Trailing bytes in explicit BER field.");
        }

        return content;
    }

    /// <summary>Creates an LN association request with optional mechanism identification and authentication bytes.</summary>
    /// <param name="initiateRequest">The already encoded InitiateRequest APDU.</param>
    /// <param name="contextId">The final arc of the DLMS application-context identifier; 1 denotes unciphered LN.</param>
    /// <param name="authenticationMechanism">The final arc of the mechanism identifier, or null to omit authentication fields.</param>
    /// <param name="authenticationValue">The authentication bytes to carry without interpreting or verifying them.</param>
    public static AcseApdu Aarq(ReadOnlySpan<byte> initiateRequest, byte contextId = AcseDefaults.UncipheredLnContext, byte? authenticationMechanism = null, ReadOnlySpan<byte> authenticationValue = default)
    {
        var fields = ImmutableArray.CreateBuilder<BerElement>();
        fields.Add(BerElement.Constructed(AcseFieldTags.ApplicationContextName, BerElement.Create(BerEncoding.ObjectIdentifierTag, DlmsObjectIdentifiers.ApplicationContext(contextId))));
        if (authenticationMechanism.HasValue)
        {
            fields.Add(BerElement.Create(AcseFieldTags.SenderAcseRequirements, AcseDefaults.AuthenticationUnusedBits, AcseDefaults.AuthenticationRequired));
            fields.Add(BerElement.Create(AcseFieldTags.MechanismName, DlmsObjectIdentifiers.AuthenticationMechanism(authenticationMechanism.Value)));
            fields.Add(BerElement.Constructed(AcseFieldTags.CallingAuthenticationValue, BerElement.Create(AcseFieldTags.AuthenticationGraphicString, authenticationValue.ToArray())));
        }

        fields.Add(BerElement.Constructed(AcseFieldTags.UserInformation, BerElement.Create(BerEncoding.OctetStringTag, initiateRequest.ToArray())));
        return new(AcseKind.Aarq, fields.ToImmutable());
    }

    /// <summary>Creates an LN association response with a result, user-source diagnostic, and InitiateResponse bytes.</summary>
    /// <param name="initiateResponse">The already encoded InitiateResponse APDU.</param>
    /// <param name="contextId">The final arc of the DLMS application-context identifier; 1 denotes unciphered LN.</param>
    /// <param name="result">The association result code.</param>
    /// <param name="diagnostic">The ACSE user-source diagnostic code.</param>
    public static AcseApdu Aare(ReadOnlySpan<byte> initiateResponse, byte contextId = AcseDefaults.UncipheredLnContext, byte result = AcseDefaults.Accepted, byte diagnostic = AcseDefaults.NoDiagnostic) => new(
        AcseKind.Aare,

        [
            BerElement.Constructed(AcseFieldTags.ApplicationContextName, BerElement.Create(BerEncoding.ObjectIdentifierTag, DlmsObjectIdentifiers.ApplicationContext(contextId))),
            BerElement.Constructed(AcseFieldTags.AssociationResult, BerElement.Create(BerEncoding.IntegerTag, result)),
            BerElement.Constructed(AcseFieldTags.ResultSourceDiagnostic, BerElement.Constructed(AcseFieldTags.DiagnosticServiceUser, BerElement.Create(BerEncoding.IntegerTag, diagnostic))),
            BerElement.Constructed(AcseFieldTags.UserInformation, BerElement.Create(BerEncoding.OctetStringTag, initiateResponse.ToArray()))
        ]
    );
    /// <summary>Creates a release request or response with an optional reason.</summary>
    /// <param name="response">Whether to create response-direction framing or a release response.</param>
    /// <param name="reason">The release reason, or null to omit it.</param>
    public static AcseApdu Release(bool response = false, byte? reason = AcseDefaults.NormalRelease) => new(response ? AcseKind.Rlre : AcseKind.Rlrq, reason.HasValue ? [BerElement.Create(AcseFieldTags.ReleaseReason, reason.Value)] : []);
}
