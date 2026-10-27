namespace EMCL.Core.Codecs;

/// <summary>Contains an incremental decode status, decoded value, byte consumption, and optional diagnostic.</summary>
/// <param name="Status">The incremental decode outcome.</param>
/// <param name="Value">The payload or decoded result; null denotes absence when the enclosing result permits it.</param>
/// <param name="Consumed">The number of input bytes consumed on success; zero for other statuses.</param>
/// <param name="Error">A local decoding diagnostic, or null when no error is reported.</param>
/// <typeparam name="T">The decoded or encoded value type.</typeparam>
public readonly record struct DecodeResult<T>(DecodeStatus Status, T? Value = default, long Consumed = 0, string? Error = null)
{
    /// <summary>Creates a successful decode result with the number of input octets consumed.</summary>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    /// <param name="consumed">The number of input bytes occupied by the decoded value.</param>
    public static DecodeResult<T> Done(T value, long consumed) => new(DecodeStatus.Done, value, consumed);
}
