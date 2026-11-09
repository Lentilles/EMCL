using EMCL.Client.Association;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Association;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Sessions;

/// <summary>Provides a replaceable, transport-independent client association and confirmed LN operations.</summary>
public interface IDlmsClientSession : IAsyncDisposable
{
    /// <summary>Gets the current association lifecycle state.</summary>
    ClientSessionState State
    {
        get;
    }
    /// <summary>Gets the last accepted context, retained after release for inspection, or null before association.</summary>
    NegotiatedAssociation? NegotiatedParameters
    {
        get;
    }
    /// <summary>Establishes the session's single association.</summary>
    /// <param name="cancellationToken">Cancellation of the association exchange.</param>
    /// <returns>The accepted context.</returns>
    ValueTask<NegotiatedAssociation> AssociateAsync(CancellationToken cancellationToken = default);
    /// <summary>Refuses new operations, drains existing operations, and exchanges RLRQ/RLRE.</summary>
    /// <param name="cancellationToken">Cancellation of draining and the release exchange.</param>
    /// <returns>Completion of the confirmed release.</returns>
    ValueTask ReleaseAsync(CancellationToken cancellationToken = default);
    /// <summary>Executes one confirmed operation and preserves remote error codes and ACTION return-data errors.</summary>
    /// <param name="operation">The LN attribute or method operation.</param>
    /// <param name="cancellationToken">Cancellation of queue waiting and execution.</param>
    /// <returns>The response, with GET blocks automatically assembled and oversized SET values transferred in negotiated blocks.</returns>
    /// <exception cref="EMCL.Client.Exceptions.DlmsWriteUnconfirmedException">A SET was attempted without a validated final result; no retry is performed.</exception>
    ValueTask<LnResponse> ExecuteAsync(CosemOperation operation, CancellationToken cancellationToken = default);
    /// <summary>Reads an attribute and throws a DlmsOperationException on a remote access error.</summary>
    /// <param name="classId">The remote interface-class identifier.</param>
    /// <param name="logicalName">The remote object identity.</param>
    /// <param name="attribute">The signed attribute index.</param>
    /// <param name="selection">Optional negotiated selective access.</param>
    /// <param name="cancellationToken">Cancellation of queue waiting and execution.</param>
    /// <returns>The typed attribute value.</returns>
    ValueTask<DlmsValue> GetAsync(ushort classId, LogicalName logicalName, sbyte attribute,
        SelectiveAccess? selection = null, CancellationToken cancellationToken = default);
    /// <summary>Starts a Profile Generic buffer GET and returns a single-use stream after reading the response header.</summary>
    /// <param name="logicalName">The remote Profile Generic object identity.</param>
    /// <param name="selection">Optional negotiated selective access by range or entry.</param>
    /// <param name="cancellationToken">Cancellation of the GET exchange.</param>
    /// <returns>The incrementally decoded rows and progress for the selected response.</returns>
    /// <exception cref="NotSupportedException">This session implementation does not support profile streaming, or a required service was not negotiated.</exception>
    ValueTask<ProfileBufferStream> OpenProfileBufferAsync(LogicalName logicalName,
        SelectiveAccess? selection = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<ProfileBufferStream>(new NotSupportedException("This session does not support profile streaming."));
    /// <summary>Writes an attribute and throws a DlmsOperationException on a remote access error.</summary>
    /// <remarks>Uses one normal SET when it fits, otherwise negotiated service-specific SET blocks. No automatic retry is performed. A deadline covers the whole transfer.</remarks>
    /// <param name="classId">The remote interface-class identifier.</param>
    /// <param name="logicalName">The remote object identity.</param>
    /// <param name="attribute">The signed attribute index.</param>
    /// <param name="value">The typed value to write.</param>
    /// <param name="selection">Optional negotiated selective access.</param>
    /// <param name="cancellationToken">Cancellation of queue waiting and execution.</param>
    /// <returns>Completion of the remote write.</returns>
    /// <exception cref="NotSupportedException">SET or required SET block transfer was not negotiated.</exception>
    /// <exception cref="EMCL.Client.Exceptions.DlmsWriteUnconfirmedException">A send was attempted but no valid final result arrived, including timeout, disconnect, cancellation, or an invalid acknowledgement. Inspect InnerException for the cause; reconcile the value before retrying.</exception>
    ValueTask SetAsync(ushort classId, LogicalName logicalName, sbyte attribute, DlmsValue value,
        SelectiveAccess? selection = null, CancellationToken cancellationToken = default);
    /// <summary>Invokes a method and throws a DlmsOperationException on an action or return-data error.</summary>
    /// <param name="classId">The remote interface-class identifier.</param>
    /// <param name="logicalName">The remote object identity.</param>
    /// <param name="method">The signed method index.</param>
    /// <param name="parameter">An optional typed method parameter.</param>
    /// <param name="cancellationToken">Cancellation of queue waiting and execution.</param>
    /// <returns>The optional return value.</returns>
    ValueTask<DlmsValue?> ActionAsync(ushort classId, LogicalName logicalName, sbyte method,
        DlmsValue? parameter = null, CancellationToken cancellationToken = default);
    /// <summary>Starts one object_list GET and returns a single-use stream after its array count is known.</summary>
    /// <param name="importer">The importer used to validate each object and resolve its class descriptor.</param>
    /// <param name="associationVersion">The Association LN version used to interpret access rights.</param>
    /// <param name="associationObject">The remote association name; null selects the standard current association.</param>
    /// <param name="cancellationToken">Cancellation of the complete GET exchange.</param>
    /// <returns>A stream with the advertised object count and incrementally validated entries.</returns>
    ValueTask<AssociationObjectStream> OpenObjectListAsync(AssociationImporter importer, byte associationVersion,
        LogicalName? associationObject = null, CancellationToken cancellationToken = default);
    /// <summary>Reads object_list and atomically imports it using an explicit Association LN version.</summary>
    /// <param name="importer">The application's configured importer and object factory.</param>
    /// <param name="logicalDeviceId">The imported logical-device identity.</param>
    /// <param name="associationId">The imported association identity.</param>
    /// <param name="associationVersion">The Association LN version used to interpret access rights.</param>
    /// <param name="associationObject">The remote association name; null selects the standard current association.</param>
    /// <param name="cancellationToken">Cancellation of the complete GET exchange.</param>
    /// <returns>The validated catalog and association permission snapshot; values remain absent.</returns>
    ValueTask<ImportedAssociation> LoadCatalogAsync(AssociationImporter importer, string logicalDeviceId,
        string associationId, byte associationVersion, LogicalName? associationObject = null,
        CancellationToken cancellationToken = default);
    /// <summary>Consumes DataNotification messages independently of outstanding confirmed requests.</summary>
    /// <remarks>Enumeration can start receiving before association. Once an AARE is accepted, DataNotification requires negotiated support.</remarks>
    /// <param name="cancellationToken">Cancellation of notification consumption, without canceling the session.</param>
    /// <returns>Notifications until release, disposal, or session failure; concurrent consumers divide the sequence.</returns>
    IAsyncEnumerable<DataNotification> ReadNotificationsAsync(CancellationToken cancellationToken = default);
}
