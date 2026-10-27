namespace EMCL.Protocol.Acse;

/// <summary>Identifies an ACSE association or release envelope by its application tag.</summary>
public enum AcseKind : byte
{
    /// <summary>An association request, encoded with application tag 0x60.</summary>
    Aarq = 0x60,
    /// <summary>An association response, encoded with application tag 0x61.</summary>
    Aare = 0x61,
    /// <summary>A release request, encoded with application tag 0x62.</summary>
    Rlrq = 0x62,
    /// <summary>A release response, encoded with application tag 0x63.</summary>
    Rlre = 0x63
}
