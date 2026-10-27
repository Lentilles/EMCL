using System.Buffers;
using System.Threading.Channels;
using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Messaging;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;

namespace EMCL.Examples.Client;

// A no-authentication example peer backed by the local object pipeline, not a production server session.
internal sealed class LocalMeterChannel(EmclDevice device) : IApduChannel
{
    private const byte RejectedAssociation = 1;
    private const byte AuthenticationMechanismNotRecognized = 11;
    private readonly ApduCodec codec = new();
    private readonly Channel<byte[]> replies = Channel.CreateUnbounded<byte[]>();

    public async ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = codec.Decode(new ReadOnlySequence<byte>(apdu));
        if (request.Status != DecodeStatus.Done || request.Consumed != apdu.Length)
        {
            throw new InvalidDataException("The example peer requires one complete APDU.");
        }

        DlmsApdu response;
        switch (request.Value)
        {
            case AcseApdu { Kind: AcseKind.Aarq } association:
                if (association.Find(AcseFieldTags.MechanismName) != null)
                {
                    response = AcseApdu.Aare([], result: RejectedAssociation, diagnostic: AuthenticationMechanismNotRecognized);
                    break;
                }

                var information = association.UserInformation ?? throw new InvalidDataException("Missing InitiateRequest.");
                var initiate = codec.Decode(new(information.ToArray())).Value as InitiateRequest
                    ?? throw new InvalidDataException("Invalid InitiateRequest.");
                response = AcseApdu.Aare(codec.EncodeBytes(new InitiateResponse(null, DlmsNegotiation.Version6,
                    initiate.Conformance, initiate.MaxReceivePduSize)));
                break;
            case LnRequest operation:
                response = await DlmsEndpoint.ExecuteAsync(operation, device.Pipeline, device.LocalContext, cancellationToken);
                break;
            case AcseApdu { Kind: AcseKind.Rlrq }:
                response = AcseApdu.Release(response: true);
                break;
            default:
                throw new NotSupportedException("The example peer supports normal LN requests only.");
        }

        await replies.Writer.WriteAsync(codec.EncodeBytes(response), cancellationToken);
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (!await replies.Reader.WaitToReadAsync(cancellationToken))
        {
            return null;
        }

        return await replies.Reader.ReadAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        replies.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
