namespace EMCL.Protocol.Acse;

internal static class DlmsObjectIdentifiers
{
    // Joint ISO/ITU-T DLMS UA arcs: 2.16.756.5.8, followed by context-name (1) or mechanism-name (2).
    private static ReadOnlySpan<byte> ApplicationContextPrefix => [0x60, 0x85, 0x74, 5, 8, 1];
    private static ReadOnlySpan<byte> AuthenticationMechanismPrefix => [0x60, 0x85, 0x74, 5, 8, 2];

    internal static byte[] ApplicationContext(byte context) => Append(ApplicationContextPrefix, context);
    internal static byte[] AuthenticationMechanism(byte mechanism) => Append(AuthenticationMechanismPrefix, mechanism);

    private static byte[] Append(ReadOnlySpan<byte> prefix, byte finalArc)
    {
        var result = new byte[prefix.Length + (finalArc < 128 ? 1 : 2)];
        prefix.CopyTo(result);
        if (finalArc >= 128)
        {
            result[^2] = 0x81;
        }

        result[^1] = (byte)(finalArc & 0x7f);
        return result;
    }
}
