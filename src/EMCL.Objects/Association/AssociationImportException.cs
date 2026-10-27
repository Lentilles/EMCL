namespace EMCL.Objects.Association;

/// <summary>Reports an invalid or incomplete Association LN object_list without publishing a partial catalog.</summary>
/// <param name="message">The diagnostic describing why the association import failed.</param>
/// <param name="inner">The underlying parsing or validation exception, or null.</param>
public sealed class AssociationImportException(string message, Exception? inner = null) : FormatException(message, inner);
