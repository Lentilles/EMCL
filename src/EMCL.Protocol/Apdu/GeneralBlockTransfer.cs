using System.Collections.Immutable;

namespace EMCL.Protocol.Apdu;

/// <summary>Carries a general block transfer fragment or acknowledgement.</summary>
/// <param name="LastBlock">Whether the transfer has no more blocks in this direction.</param>
/// <param name="Streaming">Whether consecutive blocks can follow without waiting for acknowledgement.</param>
/// <param name="WindowSize">The six-bit window size. Zero selects unconfirmed transmission.</param>
/// <param name="BlockNumber">The current block number.</param>
/// <param name="BlockNumberAcknowledged">The last acknowledged peer block number.</param>
/// <param name="Data">The fragment or an empty acknowledgement.</param>
public sealed record GeneralBlockTransfer(bool LastBlock, bool Streaming, byte WindowSize, ushort BlockNumber,
    ushort BlockNumberAcknowledged, ImmutableArray<byte> Data) : DlmsApdu;
