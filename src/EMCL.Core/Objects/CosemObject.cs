using EMCL.Core.Descriptors;
using EMCL.Core.State;
using EMCL.Core.Values;

namespace EMCL.Core.Objects;

/// <summary>Represents one COSEM object instance with an immutable logical name and replaceable class definition.</summary>
public class CosemObject
{
    /// <summary>The standard logical-name attribute index shared by COSEM interface classes.</summary>
    public const sbyte LogicalNameAttributeIndex = 1;

    /// <summary>Gets the current class definition; use Rebind to replace it without changing the class key.</summary>
    public CosemClassDescriptor Descriptor
    {
        get; private set;
    }
    /// <summary>Gets the immutable six-octet object identity.</summary>
    public LogicalName LogicalName
    {
        get;
    }
    /// <summary>Gets the attribute store supplied by the object factory.</summary>
    public IObjectStateStore State
    {
        get;
    }
    /// <summary>Gets the semaphore used by the standard dispatcher to serialize operations on this object.</summary>
    /// <remarks>The standard dispatcher owns acquisition and release. Custom synchronization must avoid re-entering the same non-reentrant semaphore.</remarks>
    public SemaphoreSlim OperationLock { get; } = new(1, 1);

    /// <summary>Creates a universal object and initializes attribute 1 to its logical-name octets.</summary>
    /// <param name="descriptor">The exact class definition to apply.</param>
    /// <param name="logicalName">The six-octet identity of the object.</param>
    /// <param name="state">An optional attribute store; null creates a thread-safe in-memory store.</param>
    public CosemObject(CosemClassDescriptor descriptor, LogicalName logicalName, IObjectStateStore? state = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
        LogicalName = logicalName;
        State = state ?? new MemoryObjectStateStore();
        State.Set(LogicalNameAttributeIndex, DlmsValue.Octets(logicalName.ToBytes()));
    }

    /// <summary>Replaces the descriptor while requiring the class identifier and version to remain unchanged.</summary>
    /// <param name="descriptor">The exact class definition to apply.</param>
    /// <exception cref="ArgumentException">The replacement descriptor has a different class key.</exception>
    public void Rebind(CosemClassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Key != Descriptor.Key)
        {
            throw new ArgumentException("Rebinding must preserve the class key.");
        }

        Descriptor = descriptor;
    }
}
