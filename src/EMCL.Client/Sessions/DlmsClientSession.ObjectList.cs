using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;

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

        var operation = new CosemOperation(CosemOperationKind.Read, CosemClassIds.AssociationLn,
            associationObject ?? CurrentAssociation, AssociationLnMembers.Attributes.ObjectList);
        var data = StartGetStream(operation, assembleBlocks: true, cancellationToken);
        var stream = new AssociationObjectStream(data, importer, associationVersion, options.Limits);
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
