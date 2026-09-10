using MesXray.Graph.Queries;
using MesXray.Runtime;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MesXray.Api.Errors;

/// <summary>Maps domain exceptions to RFC 7807 problem details. Messages never include hosts or credentials.</summary>
public sealed class XRayExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public XRayExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, extensions) = exception switch
        {
            NodeNotFoundException e => (StatusCodes.Status404NotFound, "Node not found", new Dictionary<string, object?> { ["nodeId"] = e.NodeId }),
            AmbiguousFieldException e => (StatusCodes.Status400BadRequest, "Ambiguous field", new Dictionary<string, object?> { ["field"] = e.Field, ["candidates"] = e.Candidates }),
            RuntimeDataUnavailableException => (StatusCodes.Status404NotFound, "No runtime data", new Dictionary<string, object?>()),
            RuntimeAccessDeniedException => (StatusCodes.Status403Forbidden, "Runtime operation denied", new Dictionary<string, object?>()),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request", new Dictionary<string, object?>()),
            // Malformed / incomplete JSON bodies (missing required members etc.) are client errors, not server faults.
            BadHttpRequestException e => (e.StatusCode, "Invalid request body", new Dictionary<string, object?>()),
            _ => (0, string.Empty, new Dictionary<string, object?>()),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception is BadHttpRequestException { InnerException: { } inner } ? $"{exception.Message} {inner.Message}" : exception.Message,
            Instance = httpContext.Request.Path,
        };
        foreach (var (key, value) in extensions)
        {
            problem.Extensions[key] = value;
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
