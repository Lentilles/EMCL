using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Access;
using EMCL.Core.Codecs;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.Catalog;
using EMCL.Objects.Definitions;
using EMCL.Objects.Internal;
using EMCL.Protocol.Axdr;

namespace EMCL.Objects.Association;

/// <summary>Imports raw A-XDR Association LN object lists using an explicit association version.</summary>
/// <remarks>Import does not infer attribute values or unknown class semantics. Unsupported class versions retain their exact identity in an untyped descriptor.</remarks>
public sealed class AssociationImporter
{
    private const int ImportBufferBytes = 4096;

    private readonly IClassDefinitionProvider definitions;
    private readonly ICosemObjectFactory factory;
    private readonly ProtocolLimits limits;
    /// <summary>Creates an importer with explicit class resolution, object creation, and resource limits.</summary>
    /// <param name="definitions">The provider or registry used to resolve exact class definitions.</param>
    /// <param name="factory">The object factory; null selects the universal factory.</param>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    public AssociationImporter(IClassDefinitionProvider definitions, ICosemObjectFactory? factory = null, ProtocolLimits? limits = null)
    {
        this.definitions = definitions;
        this.factory = factory ?? new DefaultCosemObjectFactory();
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <summary>Builds a new catalog and permission snapshot from one complete Association LN object_list.</summary>
    /// <param name="input">The input byte sequence, decoded value, or stream to process.</param>
    /// <param name="logicalDeviceId">The caller-defined logical-device identity.</param>
    /// <param name="associationId">The caller-defined association identity.</param>
    /// <param name="associationVersion">The Association LN version, either 1 or 3.</param>
    /// <returns>A new, fully validated catalog and immutable association permissions.</returns>
    /// <exception cref="AssociationImportException">The object list is malformed, incomplete, inconsistent, or contains trailing bytes.</exception>
    /// <exception cref="NotSupportedException">The association version is neither 1 nor 3.</exception>
    public ImportedAssociation Import(ReadOnlySequence<byte> input, string logicalDeviceId, string associationId, byte associationVersion)
    {
        var result = new AxdrCodec(limits).Decode(input);
        if (result.Status != DecodeStatus.Done || result.Consumed != input.Length)
        {
            throw new AssociationImportException($"Invalid object_list: {result.Status}; {result.Error ?? "trailing bytes"}.");
        }

        return Import(result.Value!, logicalDeviceId, associationId, associationVersion);
    }

    /// <summary>Reads one raw A-XDR object_list from a stream through EOF and then imports it atomically.</summary>
    /// <remarks>The stream is read through EOF and remains open. Live network data should first be delimited by the APDU or block-transfer layer.</remarks>
    /// <param name="input">The input byte sequence, decoded value, or stream to process.</param>
    /// <param name="logicalDeviceId">The caller-defined logical-device identity.</param>
    /// <param name="associationId">The caller-defined association identity.</param>
    /// <param name="associationVersion">The Association LN version, either 1 or 3.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>A new, fully validated catalog and immutable association permissions.</returns>
    /// <exception cref="AssociationImportException">The object list is invalid or exceeds configured limits.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<ImportedAssociation> ImportAsync(Stream input, string logicalDeviceId, string associationId, byte associationVersion, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[ImportBufferBytes];
        while (true)
        {
            int count = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            if (buffer.Length + count > limits.MaxMessageBytes)
            {
                throw new AssociationImportException("object_list exceeds message limit.");
            }

            buffer.Write(chunk, 0, count);
        }

        return Import(new ReadOnlySequence<byte>(buffer.ToArray()), logicalDeviceId, associationId, associationVersion);
    }

    /// <summary>Builds a new catalog and permission snapshot from one complete Association LN object_list.</summary>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    /// <param name="logicalDeviceId">The caller-defined logical-device identity.</param>
    /// <param name="associationId">The caller-defined association identity.</param>
    /// <param name="associationVersion">The Association LN version, either 1 or 3.</param>
    /// <returns>A new, fully validated catalog and immutable association permissions.</returns>
    /// <exception cref="AssociationImportException">The object list is malformed, incomplete, inconsistent, or contains trailing bytes.</exception>
    /// <exception cref="NotSupportedException">The association version is neither 1 nor 3.</exception>
    public ImportedAssociation Import(DlmsValue value, string logicalDeviceId, string associationId, byte associationVersion)
    {
        if (associationVersion is not (AssociationLnMembers.Version1 or AssociationLnMembers.Version3))
        {
            throw new NotSupportedException("Association LN versions 1 and 3 are supported.");
        }

        try
        {
            var list = Schema.Collection(value, DlmsType.Array);
            if (list.Length > limits.MaxCollectionItems)
            {
                throw new FormatException("Too many objects.");
            }

            var catalog = new ObjectCatalog(logicalDeviceId);
            var rights = ImmutableDictionary.CreateBuilder<LogicalName, ObjectAccessRights>();
            foreach (var entry in list)
            {
                var parsed = ParseEntry(entry, associationVersion);
                catalog.Add(factory.Create(parsed.Descriptor, parsed.Name));
                rights.Add(parsed.Name, parsed.Rights);
            }

            return new(catalog, new(associationId, associationVersion, rights.ToImmutable()));
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new AssociationImportException($"Invalid object_list: {e.Message}", e);
        }
    }

    /// <summary>Validates one object_list element and resolves its exact class definition or an untyped fallback.</summary>
    /// <param name="entry">One four-field Association LN object_list element.</param>
    /// <param name="version">The Association LN version used to interpret raw permission modes.</param>
    /// <returns>The resolved descriptor, logical name, and raw association permissions.</returns>
    public (CosemClassDescriptor Descriptor, LogicalName Name, ObjectAccessRights Rights) ParseEntry(DlmsValue entry, byte version)
    {
        if (version is not (AssociationLnMembers.Version1 or AssociationLnMembers.Version3))
        {
            throw new NotSupportedException("Unsupported association version.");
        }

        var fields = Schema.Structure(entry, AssociationStructures.ObjectEntry.Count);
        ushort classId = checked((ushort)Schema.Number(fields[AssociationStructures.ObjectEntry.ClassId], DlmsType.UInt16));
        byte classVersion = checked((byte)Schema.Number(fields[AssociationStructures.ObjectEntry.Version], DlmsType.UInt8));
        if (fields[AssociationStructures.ObjectEntry.LogicalName].Type != DlmsType.OctetString)
        {
            throw new FormatException("logical_name must be an octet-string.");
        }

        var name = LogicalName.FromBytes(fields[AssociationStructures.ObjectEntry.LogicalName].Bytes.AsSpan());
        var access = Schema.Structure(fields[AssociationStructures.ObjectEntry.AccessRights], AssociationStructures.AccessRights.Count);
        var attrs = Schema.Collection(access[AssociationStructures.AccessRights.Attributes], DlmsType.Array);
        var methods = Schema.Collection(access[AssociationStructures.AccessRights.Methods], DlmsType.Array);
        if (attrs.Length > byte.MaxValue || methods.Length > byte.MaxValue)
        {
            throw new FormatException("Too many member indices.");
        }

        var attributeRights = ImmutableArray.CreateBuilder<AttributeAccess>();
        var methodRights = ImmutableArray.CreateBuilder<MethodAccess>();
        var indices = new HashSet<sbyte>();
        foreach (var attr in attrs)
        {
            var a = Schema.Structure(attr, AssociationStructures.AttributeAccess.Count);
            sbyte index = checked((sbyte)Schema.Number(a[AssociationStructures.AttributeAccess.Index], DlmsType.Int8));
            if (index == 0 || !indices.Add(index))
            {
                throw new FormatException("Invalid or repeated attribute index.");
            }

            byte mode = checked((byte)Schema.Number(a[AssociationStructures.AttributeAccess.Mode], DlmsType.Enum));
            if (version == AssociationLnMembers.Version1 && mode > (byte)AttributeAccessModeV1.AuthenticatedReadWrite)
            {
                throw new FormatException("Invalid v1 attribute access mode.");
            }

            var selectors = a[AssociationStructures.AttributeAccess.Selectors].Type == DlmsType.Null ? ImmutableArray<byte>.Empty : Schema.Collection(a[AssociationStructures.AttributeAccess.Selectors], DlmsType.Array).Select(s => unchecked((byte)checked((sbyte)Schema.Number(s, DlmsType.Int8)))).ToImmutableArray();
            if (selectors.Distinct().Count() != selectors.Length)
            {
                throw new FormatException("Repeated access selector.");
            }

            attributeRights.Add(new(index, mode, selectors));
        }

        indices.Clear();
        foreach (var method in methods)
        {
            var m = Schema.Structure(method, AssociationStructures.MethodAccess.Count);
            sbyte index = checked((sbyte)Schema.Number(m[AssociationStructures.MethodAccess.Index], DlmsType.Int8));
            if (index == 0 || !indices.Add(index))
            {
                throw new FormatException("Invalid or repeated method index.");
            }

            byte mode = checked((byte)Schema.Number(m[AssociationStructures.MethodAccess.Mode], DlmsType.Enum));
            if (version == AssociationLnMembers.Version1 && mode > (byte)MethodAccessModeV1.AuthenticatedAccess)
            {
                throw new FormatException("Invalid v1 method access mode.");
            }

            methodRights.Add(new(index, mode));
        }

        var key = new ClassKey(classId, classVersion);
        var descriptor = definitions.Find(key) ?? new(
            key,
            $"Unknown class {classId} v{classVersion}",
            attributeRights.Select(a => new CosemAttributeDescriptor(a.Index, $"attribute_{a.Index}", Selectors: a.Selectors)).ToImmutableArray(),
            methodRights.Select(m => new CosemMethodDescriptor(m.Index, $"method_{m.Index}")).ToImmutableArray(),
            false
        );
        return (descriptor, name, new(attributeRights.ToImmutable(), methodRights.ToImmutable()));
    }
}
