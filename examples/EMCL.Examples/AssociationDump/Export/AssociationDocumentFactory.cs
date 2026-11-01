using EMCL.Client.Association;
using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Objects.Association;
using EMCL.Protocol.Apdu;

namespace EMCL.Examples.AssociationDump.Export;

internal static class AssociationDocumentFactory
{
    internal static AssociationDocument Create(ImportedAssociation imported, LoadedConfiguration configuration,
        NegotiatedAssociation negotiated, DateTimeOffset exportedAtUtc, IReadOnlyList<string>? warnings = null)
    {
        var settings = configuration.Settings;
        var connection = settings.Channel.Connection;
        var framing = settings.Channel.Framing;
        var association = settings.Association;
        var wrapper = framing as WrapperFramingSettings;
        var hdlc = framing as HdlcFramingSettings;
        var tcp = connection as TcpConnectionSettings;
        var source = new AssociationSource(tcp?.Host, tcp?.TcpPort,
            $"{(wrapper != null ? "Wrapper" : "Hdlc")}{connection.Kind}",
            wrapper?.ClientPort, wrapper?.ServerPort,
            hdlc?.ClientAddress, hdlc?.ServerLogicalAddress, hdlc?.ServerPhysicalAddress,
            imported.Access.AssociationVersion,
            configuration.AssociationName.ToString(), imported.Catalog.LogicalDeviceId, imported.Access.AssociationId,
            association.Authentication.ToString())
        {
            Serial = connection is SerialConnectionSettings serial
                ? new(serial.PortName, serial.BaudRate, serial.DataBits, serial.Parity.ToString(),
                    serial.StopBits.ToString(), serial.Handshake.ToString(), serial.DtrEnable, serial.RtsEnable)
                : null
        };
        var negotiation = new NegotiationDocument(negotiated.DlmsVersion,
            Enum.GetValues<DlmsConformance>().Where(flag => flag != DlmsConformance.None && (negotiated.Conformance & flag) == flag)
                .Select(flag => flag.ToString()).ToArray(), negotiated.ClientMaxReceivePduSize, negotiated.ServerMaxReceivePduSize);
        var objects = imported.Catalog.Objects.OrderBy(obj => obj.LogicalName.Value)
            .Select(obj => Object(obj, imported.Access.Rights[obj.LogicalName], imported.Access.AssociationVersion)).ToArray();
        return new(AssociationDocument.CurrentSchemaVersion, exportedAtUtc, source, negotiation, objects.Length, objects,
            warnings ?? []);
    }

    private static ObjectDocument Object(CosemObject obj, ObjectAccessRights rights, byte version)
    {
        var descriptor = obj.Descriptor;
        // Association-only descriptors contain placeholder names, not known class semantics.
        var definedAttributes = descriptor.Attributes.Where(_ => descriptor.IsKnown).ToDictionary(attribute => attribute.Index);
        var definedMethods = descriptor.Methods.Where(_ => descriptor.IsKnown).ToDictionary(method => method.Index);
        var reportedAttributes = rights.Attributes.ToDictionary(attribute => attribute.Index);
        var reportedMethods = rights.Methods.ToDictionary(method => method.Index);
        var attributes = definedAttributes.Keys.Union(reportedAttributes.Keys).Order().Select(index =>
        {
            definedAttributes.TryGetValue(index, out var definition);
            reportedAttributes.TryGetValue(index, out var access);
            return new AttributeDocument(index, definition?.Name, definition?.Type?.ToString(),
                definition?.Type is { } type ? (byte)type : null, definition != null, access != null,
                access == null ? null : AccessDocuments.Attribute(access, version));
        }).ToArray();
        var methods = definedMethods.Keys.Union(reportedMethods.Keys).Order().Select(index =>
        {
            definedMethods.TryGetValue(index, out var definition);
            reportedMethods.TryGetValue(index, out var access);
            return new MethodDocument(index, definition?.Name, definition?.ParameterType?.ToString(),
                definition?.ParameterType is { } type ? (byte)type : null, definition != null, access != null,
                access == null ? null : AccessDocuments.Method(access, version));
        }).ToArray();
        return new(obj.LogicalName.ToString(), descriptor.Key.ClassId, descriptor.Key.ClassVersion,
            descriptor.IsKnown ? descriptor.Name : null, descriptor.IsKnown, attributes, methods);
    }
}
