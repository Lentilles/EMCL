using EMCL.Client.Internal;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Axdr;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    /// <inheritdoc />
    public async ValueTask<ProfileBufferStream> OpenProfileBufferAsync(LogicalName logicalName,
        SelectiveAccess? selection = null, CancellationToken cancellationToken = default)
    {
        var operation = new CosemOperation(CosemOperationKind.Read, CosemClassIds.ProfileGeneric,
            logicalName, ProfileGenericMembers.Attributes.Buffer, Selection: selection);
        var data = StartGetStream(operation, assembleBlocks: false, cancellationToken);
        var stream = new ProfileBufferStream(data, options.Limits, Fault);
        try
        {
            await stream.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private GetDataStream StartGetStream(CosemOperation operation, bool assembleBlocks, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = new GetDataStream(cancellationToken, assembleBlocks);
        stream.Producer = ProduceGetStreamAsync(operation, stream);
        return stream;
    }

    private async Task ProduceGetStreamAsync(CosemOperation operation, GetDataStream stream)
    {
        try
        {
            var response = await ExecuteObservedAsync(operation, stream, stream.Token).ConfigureAwait(false);
            ThrowRemoteError(response);
            if (!stream.ReceivedBlocks)
            {
                await stream.WriteAsync(new AxdrCodec(options.Limits).EncodeBytes(response.Result.Value!))
                    .ConfigureAwait(false);
            }

            stream.Complete();
        }
        catch (Exception error)
        {
            stream.Complete(error);
        }
    }
}
