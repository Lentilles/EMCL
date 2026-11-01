using System.Threading.Channels;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Axdr;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    /// <inheritdoc />
    public async ValueTask<AssociationObjectStream> OpenObjectListAsync(AssociationImporter importer,
        byte associationVersion, LogicalName? associationObject = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importer);
        if (associationVersion is not (AssociationLnMembers.Version1 or AssociationLnMembers.Version3))
        {
            throw new NotSupportedException("An explicit Association LN version 1 or 3 is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var chunks = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

        async Task ProduceAsync()
        {
            bool receivedBlocks = false;
            try
            {
                async ValueTask OnBlock(ReadOnlyMemory<byte> data, CancellationToken token)
                {
                    receivedBlocks = true;
                    await chunks.Writer.WriteAsync(data.ToArray(), token).ConfigureAwait(false);
                }

                var operation = new CosemOperation(CosemOperationKind.Read, CosemClassIds.AssociationLn,
                    associationObject ?? CurrentAssociation, AssociationLnMembers.Attributes.ObjectList);
                var response = await ExecuteObservedAsync(operation, OnBlock, source.Token).ConfigureAwait(false);
                ThrowRemoteError(response);
                if (!receivedBlocks)
                {
                    await chunks.Writer.WriteAsync(new AxdrCodec(options.Limits).EncodeBytes(response.Result.Value!),
                        source.Token).ConfigureAwait(false);
                }

                chunks.Writer.TryComplete();
            }
            catch (Exception error)
            {
                chunks.Writer.TryComplete(error);
            }
        }

        var producer = ProduceAsync();
        var stream = new AssociationObjectStream(chunks.Reader, producer, source, importer, associationVersion, options.Limits);
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
}
