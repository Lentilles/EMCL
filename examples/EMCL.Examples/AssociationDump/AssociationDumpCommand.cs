using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Values;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Examples.AssociationDump.Export;
using EMCL.Examples.AssociationDump.Import;
using EMCL.Examples.AssociationDump.Logging;
using EMCL.Framing;
using EMCL.Objects.Association;
using EMCL.Transport.Channels;

namespace EMCL.Examples.AssociationDump;

internal static class AssociationDumpCommand
{
    internal static async Task RunAsync(string configurationPath, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        // Frame tracing and progress reporting share writers across the receive loop and caller.
        output = TextWriter.Synchronized(output);
        error = TextWriter.Synchronized(error);
        var configuration = await ConfigurationFile.LoadAsync(configurationPath, cancellationToken);
        var connection = configuration.Settings.Channel.Connection;
        var framing = configuration.Settings.Channel.Framing;
        string connectionDetails = framing switch
        {
            WrapperFramingSettings wrapper => $"Wrapper/{connection.Kind} (source={wrapper.ClientPort}, destination={wrapper.ServerPort})",
            HdlcFramingSettings hdlc => $"HDLC/{connection.Kind} (client={hdlc.ClientAddress}, server logical={hdlc.ServerLogicalAddress}, "
                + $"physical={hdlc.ServerPhysicalAddress?.ToString() ?? "none"}, "
                + $"server address bytes={(hdlc.ServerPhysicalAddress is null ? 1 : hdlc.ExtendedAddress ? 4 : 2)})",
            _ => throw new InvalidOperationException("Unsupported channel framing.")
        };
        await output.WriteLineAsync($"Connecting to {connection.Endpoint} using {connectionDetails}...");
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
        await using var objectStream = await client.OpenObjectListAsync(importer, association.Version,
            configuration.AssociationName, cancellationToken);
        await output.WriteLineAsync($"The meter reports {objectStream.TotalCount} object_list entries.");
        var entries = new List<DlmsValue>(objectStream.TotalCount);
        await foreach (var entry in objectStream.WithCancellation(cancellationToken))
        {
            entries.Add(entry.RawValue);
            await output.WriteLineAsync($"[{objectStream.CurrentCount}/{objectStream.TotalCount}] "
                + $"{entry.LogicalName} class={entry.Descriptor.Key.ClassId} version={entry.Descriptor.Key.ClassVersion}");
        }

        var list = DlmsValue.Array([.. entries]);
        string[] warnings = [];
        if (association.IgnoreIdenticalObjectListEntries)
        {
            var filtered = IdenticalObjectListDuplicateFilter.Apply(list, configuration.Settings.Limits);
            warnings = filtered.Warnings;
            list = filtered.Value;
        }

        ImportedAssociation imported = importer.Import(list, association.LogicalDeviceId,
            association.AssociationId, association.Version);

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
