using EMCL.Core.Operations;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Exceptions;

/// <summary>Reports a remote data-access, action, or ACTION return-data error without faulting the association.</summary>
public sealed class DlmsOperationException : Exception
{
    /// <summary>Gets the complete response, preserving separate ACTION result and return-data errors.</summary>
    public LnResponse Response
    {
        get;
    }
    /// <summary>Gets the primary operation error or, on successful ACTION, its return-data error.</summary>
    public CosemResultCode Error => Response.Result.Error != CosemResultCode.Success
        ? Response.Result.Error : Response.ReturnDataError ?? CosemResultCode.Success;
    /// <summary>Creates an exception for an unsuccessful remote operation.</summary>
    /// <param name="response">The unsuccessful response.</param>
    public DlmsOperationException(LnResponse response) : base("The remote DLMS operation returned an error.")
    {
        ArgumentNullException.ThrowIfNull(response);
        Response = response;
    }
}
