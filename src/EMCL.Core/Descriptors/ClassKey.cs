namespace EMCL.Core.Descriptors;

/// <summary>Identifies an interface-class definition by standard profile, class identifier, and exact class version.</summary>
/// <param name="ProfileId">The standard-profile identifier, including its edition or revision.</param>
/// <param name="ClassId">The 16-bit interface-class identifier.</param>
/// <param name="ClassVersion">The exact interface-class version.</param>
public readonly record struct ClassKey(string ProfileId, ushort ClassId, byte ClassVersion);
