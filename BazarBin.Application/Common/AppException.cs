using System.Net;

namespace BazarBin.Application.Common;

public abstract class AppException : Exception
{
    protected AppException(string message, string errorCode, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public HttpStatusCode StatusCode { get; }
}
