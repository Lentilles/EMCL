using System.Net.Sockets;
using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Examples.AssociationDump.Export;
using EMCL.Examples.AssociationDump.Import;
using EMCL.Examples.AssociationDump.Logging;
using EMCL.Examples.AssociationDump.Transport;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Wrapper;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Examples.AssociationDump;

internal static class AssociationDumpCommand
{
    internal static async Task RunAsync(string configurationPath, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        var configuration = await ConfigurationFile.LoadAsync(configurationPath, cancellationToken);
        var connection = configuration.Settings.Connection;
        string connectionDetails = connection.Transport == MeterTransport.WrapperTcp
            ? $"Wrapper/TCP (source={connection.ClientWrapperPort}, destination={connection.ServerWrapperPort})"
            : $"HDLC/TCP (client={connection.HdlcClientAddress}, server logical={connection.HdlcServerLogicalAddress}, "
                + $"physical={connection.HdlcServerPhysicalAddress?.ToString() ?? "none"})";
        output.WriteLine($"Connecting to {connection.Host}:{connection.TcpPort} using {connectionDetails}...");
        using var tcp = new TcpClient();
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(configuration.ConnectTimeout);
            try
            {
                await tcp.ConnectAsync(connection.Host, connection.TcpPort, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The TCP connection deadline expired.");
            }
        }

        // The TCP client owns the stream; dispose the session before closing the socket.
        var byteTransport = new StreamByteTransport(tcp.GetStream(), leaveOpen: true);
        bool redactAarq = configuration.Settings.Association.Authentication != ClientAuthentication.None;
        await using IApduChannel channel = connection.Transport switch
        {
            MeterTransport.WrapperTcp => new TracingApduChannel(
                new FramedApduChannel<WrapperFrame>(byteTransport, new WrapperCodec(configuration.Settings.Limits),
                    new WrapperApduAdapter(connection.ClientWrapperPort, connection.ServerWrapperPort), configuration.Settings.Limits),
                output, redactAarq),
            MeterTransport.HdlcTcp => await HdlcTcpApduChannel.ConnectAsync(byteTransport,
                HdlcAddress.Client(connection.HdlcClientAddress),
                HdlcAddress.Server(connection.HdlcServerLogicalAddress, connection.HdlcServerPhysicalAddress,
                    connection.HdlcExtendedAddress), configuration.Settings.Limits, configuration.Session.RequestTimeout,
                output, redactAarq, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported meter transport.")
        };
        await using var client = new DlmsClientSession(channel, configuration.Session with
        {
            LeaveOpen = true
        });
        var negotiated = await client.AssociateAsync(cancellationToken);
        output.WriteLine("Association established. Reading object_list...");
        var association = configuration.Settings.Association;
        var importer = new EmclBuilder { Limits = configuration.Settings.Limits }.CreateImporter();
        ImportedAssociation imported;
        string[] warnings = [];
        if (association.IgnoreIdenticalObjectListEntries)
        {
            var list = await client.GetAsync(CosemClassIds.AssociationLn, configuration.AssociationName,
                AssociationLnMembers.Attributes.ObjectList, cancellationToken: cancellationToken);
            var filtered = IdenticalObjectListDuplicateFilter.Apply(list, configuration.Settings.Limits);
            warnings = filtered.Warnings;
            imported = importer.Import(filtered.Value, association.LogicalDeviceId, association.ProfileId,
                association.AssociationId, association.Version);
        }
        else
        {
            imported = await client.LoadCatalogAsync(importer, association.LogicalDeviceId, association.ProfileId,
                association.AssociationId, association.Version, configuration.AssociationName, cancellationToken);
        }

        var document = AssociationDocumentFactory.Create(imported, configuration, negotiated, DateTimeOffset.UtcNow,
            warnings);
        await AssociationJsonFile.WriteAsync(configuration.OutputPath, document, configuration.Settings.Output.Overwrite, cancellationToken);
        output.WriteLine($"Saved {document.ObjectCount} objects to {configuration.OutputPath}");
        foreach (string warning in warnings)
        {
            error.WriteLine(warning);
        }

        try
        {
            await client.ReleaseAsync(cancellationToken);
        }
        catch (Exception releaseError) when (releaseError is IOException or TimeoutException or InvalidOperationException)
        {
            // A completed export remains usable even if the meter does not confirm RLRQ.
            error.WriteLine($"The JSON file was saved, but association release failed: {releaseError.Message}");
        }
    }
}
