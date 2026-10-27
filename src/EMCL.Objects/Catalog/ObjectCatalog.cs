using System.Collections.Immutable;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;

namespace EMCL.Objects.Catalog;

/// <summary>Maintains the object instances of one logical device and enforces profile and class-version consistency.</summary>
/// <param name="logicalDeviceId">The caller-defined logical-device identity.</param>
/// <param name="profileId">The standard-profile identifier, including its edition.</param>
public sealed class ObjectCatalog(string logicalDeviceId, string profileId)
{
    private readonly Dictionary<LogicalName, CosemObject> objects = [];
    /// <summary>Gets the identity used to isolate operations for this logical device.</summary>
    public string LogicalDeviceId { get; } = logicalDeviceId;
    /// <summary>Gets the standard-profile identifier required by every object in the catalog.</summary>
    public string ProfileId { get; } = profileId;

    /// <summary>Gets an immutable snapshot of the currently registered object instances.</summary>
    public ImmutableArray<CosemObject> Objects
    {
        get
        {
            lock (objects)
            {
                return objects.Values.ToImmutableArray();
            }
        }
    }

    /// <summary>Finds an object by its logical name within this logical device.</summary>
    /// <param name="name">The logical name to find, remove, or override.</param>
    /// <returns>The object with that logical name, or null when absent.</returns>
    public CosemObject? Find(LogicalName name)
    {
        lock (objects)
        {
            return objects.GetValueOrDefault(name);
        }
    }

    /// <summary>Adds an object after checking its profile, unique logical name, and class-version consistency.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <exception cref="ArgumentException">The profile, logical name, or class version conflicts with the existing catalog.</exception>
    public void Add(CosemObject cosemObject)
    {
        lock (objects)
        {
            if (cosemObject.Descriptor.Key.ProfileId != ProfileId)
            {
                throw new ArgumentException("Profile does not match the logical device.");
            }

            if (objects.ContainsKey(cosemObject.LogicalName))
            {
                throw new ArgumentException($"Duplicate logical name {cosemObject.LogicalName}.");
            }

            if (objects.Values.Any(o => o.Descriptor.Key.ClassId == cosemObject.Descriptor.Key.ClassId && o.Descriptor.Key.ClassVersion != cosemObject.Descriptor.Key.ClassVersion))
            {
                throw new ArgumentException($"Inconsistent versions of class {cosemObject.Descriptor.Key.ClassId} in one logical device.");
            }

            objects.Add(cosemObject.LogicalName, cosemObject);
        }
    }

    /// <summary>Removes an object from the device catalog, independently of association visibility.</summary>
    /// <remarks>This removes the device object. Association remove_object only changes visibility within that association and does not call this method.</remarks>
    /// <param name="name">The logical name to find, remove, or override.</param>
    /// <returns>True if the object was removed; otherwise false.</returns>
    public bool Remove(LogicalName name)
    {
        lock (objects)
        {
            return objects.Remove(name);
        }
    }

    /// <summary>Rebinds objects for which the provider supplies a matching descriptor, preserving identity and state.</summary>
    /// <param name="definitions">The provider or registry used to resolve exact class definitions.</param>
    public void Rebind(IClassDefinitionProvider definitions)
    {
        lock (objects)
        {
            foreach (var obj in objects.Values)
            {
                if (definitions.Find(obj.Descriptor.Key) is { } descriptor)
                {
                    obj.Rebind(descriptor);
                }
            }
        }
    }
}
