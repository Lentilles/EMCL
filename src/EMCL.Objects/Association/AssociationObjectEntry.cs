using EMCL.Core.Access;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Association;

/// <summary>One validated Association LN object_list entry, including its original A-XDR value.</summary>
public sealed record AssociationObjectEntry(DlmsValue RawValue, CosemClassDescriptor Descriptor,
    LogicalName LogicalName, ObjectAccessRights Rights);
