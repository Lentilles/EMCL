namespace EMCL.Objects.Definitions;

/// <summary>Selects the information model used to resolve interface classes.</summary>
public enum CosemProfile
{
    /// <summary>The default DLMS/COSEM definitions.</summary>
    Cosem,
    /// <summary>The information model in STO 34.01-5.1-006-2023.</summary>
    Spodes4,
    /// <summary>The information model in the supplied preliminary STO 34.01-5.1-006-2025.</summary>
    Spodes5
}
