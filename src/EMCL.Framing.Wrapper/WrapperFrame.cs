using System.Collections.Immutable;

namespace EMCL.Framing.Wrapper;

/// <summary>Represents a versioned DLMS Wrapper header and its application payload.</summary>
/// <param name="SourcePort">The source Wrapper wPort.</param>
/// <param name="DestinationPort">The destination Wrapper wPort.</param>
/// <param name="Payload">The complete application payload.</param>
/// <param name="Version">The Wrapper protocol version; only version 1 is supported.</param>
public sealed record WrapperFrame(ushort SourcePort, ushort DestinationPort, ImmutableArray<byte> Payload, ushort Version = WrapperHeader.Version);
