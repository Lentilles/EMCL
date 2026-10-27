using EMCL.Core.Codecs;

namespace EMCL.Protocol.Internal;

internal sealed class WireException(DecodeStatus status, string message) : Exception(message)
{
    public DecodeStatus Status { get; } = status;
}
