using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Examples.AssociationDump.Export;
using EMCL.Examples.AssociationDump.Import;
using EMCL.Examples.AssociationDump.Logging;
using EMCL.Framing;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Transport.Channels;

namespace EMCL.Examples.AssociationDump;

internal static class AssociationDumpCommand
{
    internal static async Task RunAsync(string configurationPath, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        var configuration = await ConfigurationFile.LoadAsync(configurationPath, cancellationToken);
        var connection = configuration.Settings.Channel.Connection;
        var framing = configuration.Settings.Channel.Framing;
        string connectionDetails = framing switch
        {
            WrapperFramingSettings wrapper => $"Wrapper/TCP (source={wrapper.ClientPort}, destination={wrapper.ServerPort})",
            HdlcFramingSettings hdlc => $"HDLC/TCP (client={hdlc.ClientAddress}, server logical={hdlc.ServerLogicalAddress}, "
                + $"physical={hdlc.ServerPhysicalAddress?.ToString() ?? "none"}, "
                + $"server address bytes={(hdlc.ServerPhysicalAddress is null ? 1 : hdlc.ExtendedAddress ? 4 : 2)})",
            _ => throw new InvalidOperationException("Unsupported channel framing.")
        };
        await output.WriteLineAsync($"Connecting to {connection.Host}:{connection.TcpPort} using {connectionDetails}...");
        bool redactAuthenticationData = configuration.Settings.Association.Authentication != ClientAuthentication.None;
        var channelSettings = configuration.CreateChannelSettings(new HdlcFrameTracer(output, redactAuthenticationData).Log);
        IApduChannel opened = await ApduChannels.ConnectAsync(channelSettings, cancellationToken);
        await using IApduChannel channel = framing is WrapperFramingSettings
            ? new TracingApduChannel(opened, output, redactAuthenticationData)
            : opened;
        await using var client = new DlmsClientSession(channel, configuration.Session with
        {
            LeaveOpen = true
        });
        var negotiated = await client.AssociateAsync(cancellationToken);
        await output.WriteLineAsync("Association established. Reading object_list...");
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
            imported = importer.Import(filtered.Value, association.LogicalDeviceId,
                association.AssociationId, association.Version);
        }
        else
        {
            imported = await client.LoadCatalogAsync(importer, association.LogicalDeviceId,
                association.AssociationId, association.Version, configuration.AssociationName, cancellationToken);
        }

        var document = AssociationDocumentFactory.Create(imported, configuration, negotiated, DateTimeOffset.UtcNow,
            warnings);
        await AssociationJsonFile.WriteAsync(configuration.OutputPath, document, configuration.Settings.Output.Overwrite, cancellationToken);
        await output.WriteLineAsync($"Saved {document.ObjectCount} objects to {configuration.OutputPath}");
        foreach (string warning in warnings)
        {
            await error.WriteLineAsync(warning);
        }

        try
        {
            await client.ReleaseAsync(cancellationToken);
        }
        catch (Exception releaseError) when (releaseError is IOException or TimeoutException or InvalidOperationException)
        {
            // A completed export remains usable even if the meter does not confirm RLRQ.
            await error.WriteLineAsync($"The JSON file was saved, but association release failed: {releaseError.Message}");
        }
    }
}
