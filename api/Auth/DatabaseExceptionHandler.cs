using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace api.Auth;

public sealed class DatabaseExceptionHandler(ILogger<DatabaseExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbException && exception.InnerException is not DbException) return false;
        logger.LogWarning("A database operation failed. Returning a temporary service error.");
        await Results.Problem(statusCode: 503, title: "The service is temporarily unavailable. Try again shortly.",
            extensions: new Dictionary<string, object?> { ["code"] = "service_unavailable" }).ExecuteAsync(context);
        return true;
    }
}
