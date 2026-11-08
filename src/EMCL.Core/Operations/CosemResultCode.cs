namespace EMCL.Core.Operations;

/// <summary>Defines the success and failure codes used by COSEM object operations and LN protocol responses.</summary>
public enum CosemResultCode : byte
{
    /// <summary>The operation completed successfully.</summary>
    Success = 0,
    /// <summary>The object reports a hardware fault.</summary>
    HardwareFault = 1,
    /// <summary>The operation cannot complete in the current transient state.</summary>
    TemporaryFailure = 2,
    /// <summary>The requested access or method execution is denied.</summary>
    ReadWriteDenied = 3,
    /// <summary>The requested object or member is not defined.</summary>
    ObjectUndefined = 4,
    /// <summary>The requested class does not match the object definition.</summary>
    ObjectClassInconsistent = 9,
    /// <summary>The object or requested value is currently unavailable.</summary>
    ObjectUnavailable = 11,
    /// <summary>The value or parameter does not match the required type or structure.</summary>
    TypeUnmatched = 12,
    /// <summary>The requested selection or parameter range is invalid.</summary>
    ScopeOfAccessViolated = 13,
    /// <summary>The requested data block is unavailable.</summary>
    DataBlockUnavailable = 14,
    /// <summary>The long GET transfer was aborted.</summary>
    LongGetAborted = 15,
    /// <summary>No matching long GET transfer is active.</summary>
    NoLongGetInProgress = 16,
    /// <summary>The long SET transfer was aborted.</summary>
    LongSetAborted = 17,
    /// <summary>No matching long SET transfer is active.</summary>
    NoLongSetInProgress = 18,
    /// <summary>The block number or transfer identifier does not match the expected next block.</summary>
    DataBlockNumberInvalid = 19,
    /// <summary>The operation failed for an otherwise unspecified reason.</summary>
    OtherReason = 250
}
