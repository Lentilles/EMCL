using EMCL.Core.Objects;

namespace EMCL.Objects.Spodes;

/// <summary>Verifies and activates images for a local Image Transfer object.</summary>
public interface ISpodesImageTarget
{
    /// <summary>Verifies a complete image and returns its signature or an empty signature. Throw on verification failure.</summary>
    /// <param name="logicalName">The Image Transfer object's logical name.</param>
    /// <param name="identifier">The transmitted image identifier.</param>
    /// <param name="image">The complete image bytes.</param>
    /// <param name="cancellationToken">A token that cancels verification.</param>
    ValueTask<ReadOnlyMemory<byte>> VerifyAsync(LogicalName logicalName, ReadOnlyMemory<byte> identifier,
        ReadOnlyMemory<byte> image, CancellationToken cancellationToken);

    /// <summary>Activates a previously verified image. Throw if the activation fails.</summary>
    /// <param name="logicalName">The Image Transfer object's logical name.</param>
    /// <param name="identifier">The transmitted image identifier.</param>
    /// <param name="image">The complete image bytes.</param>
    /// <param name="cancellationToken">A token that cancels activation.</param>
    ValueTask ActivateAsync(LogicalName logicalName, ReadOnlyMemory<byte> identifier, ReadOnlyMemory<byte> image,
        CancellationToken cancellationToken);
}
