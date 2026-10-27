namespace EMCL.Core.Access;

/// <summary>Defines Association LN v1 method-access enumeration values.</summary>
public enum MethodAccessModeV1 : byte
{
    /// <summary>No method access.</summary>
    None = 0,
    /// <summary>Method access.</summary>
    Access = 1,
    /// <summary>Authenticated method access.</summary>
    AuthenticatedAccess = 2
}
