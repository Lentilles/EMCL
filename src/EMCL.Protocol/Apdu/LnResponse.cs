using EMCL.Core.Operations;

namespace EMCL.Protocol.Apdu;

/// <summary>Represents a normal Logical Name response, preserving a separate ACTION return-data error when present.</summary>
/// <param name="Kind">The operation or envelope variant.</param>
/// <param name="InvokeIdAndPriority">The original invoke-id-and-priority octet.</param>
/// <param name="Result">The operation result and optional return data.</param>
/// <param name="ReturnDataError">The separate ACTION return-data access error, when present.</param>
public sealed record LnResponse(CosemOperationKind Kind, byte InvokeIdAndPriority, OperationResult Result, CosemError? ReturnDataError = null) : DlmsApdu;
