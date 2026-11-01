using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Processing;

/// <summary>Resolves instance overrides, class overrides, default handlers, and a universal fallback in that order.</summary>
/// <remarks>Configure registrations before concurrent use. Instance handlers have priority over user class handlers, default handlers, and Fallback.</remarks>
public sealed class HandlerRegistry
{
    private readonly Dictionary<(string Device, LogicalName Name), ICosemObjectHandler> instances = [];
    private readonly Dictionary<ClassKey, ICosemObjectHandler> classes = [];
    private readonly Dictionary<ClassKey, ICosemObjectHandler> defaults = [];
    /// <summary>Gets or sets the handler used when no instance, class, or default handler matches.</summary>
    public ICosemObjectHandler Fallback { get; set; } = new StoredValueHandler();

    /// <summary>Registers or replaces a handler for one object in one logical device.</summary>
    /// <param name="logicalDeviceId">The caller-defined logical-device identity.</param>
    /// <param name="name">The logical name to find, remove, or override.</param>
    /// <param name="handler">The handler or operation delegate to register.</param>
    public void ForInstance(string logicalDeviceId, LogicalName name, ICosemObjectHandler handler) => instances[(logicalDeviceId, name)] = handler;
    /// <summary>Registers or replaces a user handler for one exact class definition.</summary>
    /// <param name="key">The exact class key or independently scoped GET transfer key.</param>
    /// <param name="handler">The handler or operation delegate to register.</param>
    public void ForClass(ClassKey key, ICosemObjectHandler handler) => classes[key] = handler;
    /// <summary>Registers or replaces the default handler supplied by the library.</summary>
    /// <param name="key">The exact class key or independently scoped GET transfer key.</param>
    /// <param name="handler">The handler or operation delegate to register.</param>
    public void ForDefaultClass(ClassKey key, ICosemObjectHandler handler) => defaults[key] = handler;
    /// <summary>Resolves instance, user-class, and default handlers before returning the fallback.</summary>
    /// <param name="device">The logical-device identity used to resolve instance overrides.</param>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <returns>The highest-priority matching handler.</returns>
    public ICosemObjectHandler Resolve(string device, CosemObject cosemObject) => instances.GetValueOrDefault((device, cosemObject.LogicalName)) ?? classes.GetValueOrDefault(cosemObject.Descriptor.Key) ?? defaults.GetValueOrDefault(cosemObject.Descriptor.Key) ?? Fallback;
}
