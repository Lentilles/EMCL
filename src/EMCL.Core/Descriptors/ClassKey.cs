namespace EMCL.Core.Descriptors;

/// <summary>Identifies an interface-class definition by class identifier and exact version.</summary>
/// <param name="ClassId">The 16-bit interface-class identifier.</param>
/// <param name="ClassVersion">The exact interface-class version.</param>
public readonly record struct ClassKey(ushort ClassId, byte ClassVersion);
