using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Core.Operations;

/// <summary>Describes a transport-independent operation on one object attribute or method.</summary>
/// <param name="Kind">The operation or envelope variant.</param>
/// <param name="ClassId">The 16-bit interface-class identifier.</param>
/// <param name="LogicalName">The six-octet logical name of the target object.</param>
/// <param name="Index">The signed attribute or method index; negative indices identify proprietary members.</param>
/// <param name="Value">The write value or method parameter, or null when no input value is supplied.</param>
/// <param name="Selection">Optional selective-access parameters for an attribute operation.</param>
public sealed record CosemOperation(CosemOperationKind Kind, ushort ClassId, LogicalName LogicalName, sbyte Index, DlmsValue? Value = null, SelectiveAccess? Selection = null);
