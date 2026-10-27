namespace EMCL.Core.Access;

/// <summary>Defines Association LN v1 attribute-access enumeration values.</summary>
public enum AttributeAccessModeV1 : byte
{
    /// <summary>No access.</summary>
    None = 0,
    /// <summary>Read access.</summary>
    ReadOnly = 1,
    /// <summary>Write access.</summary>
    WriteOnly = 2,
    /// <summary>Read and write access.</summary>
    ReadWrite = 3,
    /// <summary>Authenticated read access.</summary>
    AuthenticatedReadOnly = 4,
    /// <summary>Authenticated write access.</summary>
    AuthenticatedWriteOnly = 5,
    /// <summary>Authenticated read and write access.</summary>
    AuthenticatedReadWrite = 6
}
