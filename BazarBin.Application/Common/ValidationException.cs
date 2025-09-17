using System.Net;

namespace BazarBin.Application.Common;

public sealed class ValidationException : AppException
{
    public ValidationException(string message, string errorCode)
        : base(message, errorCode, HttpStatusCode.BadRequest)
    {
    }
}
