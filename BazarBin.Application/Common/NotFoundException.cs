using System.Net;

namespace BazarBin.Application.Common;

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message, string errorCode)
        : base(message, errorCode, HttpStatusCode.NotFound)
    {
    }
}
