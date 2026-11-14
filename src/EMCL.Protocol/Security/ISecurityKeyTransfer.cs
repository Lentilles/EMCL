namespace EMCL.Protocol.Security;

/// <summary>Wraps and unwraps the keys exchanged through a Security Setup object.</summary>
public interface ISecurityKeyTransfer
{
    /// <summary>Wraps local key material for the peer.</summary>
    /// <param name="key">The raw key material.</param>
    byte[] WrapKey(ReadOnlySpan<byte> key);

    /// <summary>Authenticates and unwraps key material sent by the peer.</summary>
    /// <param name="wrapped">The wrapped material.</param>
    byte[] UnwrapKey(ReadOnlySpan<byte> wrapped);
}
