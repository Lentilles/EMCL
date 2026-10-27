namespace EMCL.Core.Operations;

/// <summary>Identifies a local COSEM read, write, or method invocation.</summary>
public enum CosemOperationKind
{
    /// <summary>Reads an attribute or selected attribute content.</summary>
    Read,
    /// <summary>Writes an attribute value.</summary>
    Write,
    /// <summary>Invokes an object method.</summary>
    Invoke
}
