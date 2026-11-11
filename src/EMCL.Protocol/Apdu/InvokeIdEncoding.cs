namespace EMCL.Protocol.Apdu;

/// <summary>Defines the invoke-id-and-priority octet used by normal LN services.</summary>
public static class InvokeIdEncoding
{
    /// <summary>The four-bit invoke identifier mask and maximum identifier.</summary>
    public const byte IdMask = 0x0f;
    /// <summary>The confirmed service-class flag.</summary>
    public const byte ConfirmedService = 0x40;
    /// <summary>The high-priority flag.</summary>
    public const byte HighPriority = 0x80;
    /// <summary>The number of distinct simultaneous invoke identifiers.</summary>
    public const int Capacity = IdMask + 1;

    /// <summary>Creates a confirmed-service invoke octet with a validated identifier.</summary>
    /// <param name="id">The four-bit invoke identifier.</param>
    /// <param name="priority">The request priority.</param>
    /// <returns>The encoded invoke-id-and-priority octet.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The identifier exceeds four bits or the priority is undefined.</exception>
    public static byte Create(byte id, InvokePriority priority = InvokePriority.Normal)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, IdMask);
        byte priorityFlag = priority switch
        {
            InvokePriority.Normal => 0,
            InvokePriority.High => HighPriority,
            _ => throw new ArgumentOutOfRangeException(nameof(priority))
        };
        return (byte)(id | ConfirmedService | priorityFlag);
    }
}
