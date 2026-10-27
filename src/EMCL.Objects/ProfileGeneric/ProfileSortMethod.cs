namespace EMCL.Objects.ProfileGeneric;

/// <summary>Defines Profile Generic buffer sorting rules.</summary>
public enum ProfileSortMethod : byte
{
    /// <summary>Retain the newest rows in capture order.</summary>
    Fifo = 1,
    /// <summary>Retain the newest rows in reverse capture order.</summary>
    Lifo = 2,
    /// <summary>Sort values in descending order.</summary>
    Largest = 3,
    /// <summary>Sort values in ascending order.</summary>
    Smallest = 4,
    /// <summary>Sort absolute values in ascending order.</summary>
    NearestToZero = 5,
    /// <summary>Sort absolute values in descending order.</summary>
    FarthestFromZero = 6
}
