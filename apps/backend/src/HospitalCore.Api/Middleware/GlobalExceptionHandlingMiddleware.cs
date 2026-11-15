using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace HospitalCore.Api.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(ex, "Unhandled exception occurred after response started.");
                throw;
            }

            await WriteProblemDetailsAsync(context, ex);
        }
    }

    private async Task WriteProblemDetailsAsync(HttpContext context, Exception exception)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ApplicationValidationException validationEx:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation Failed",
                    Detail = validationEx.Message
                };
                problem.Extensions["code"] = "VALIDATION_ERROR";
                problem.Extensions["errors"] = validationEx.Errors;
                break;

            case DomainValidationException domainEx:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Domain Validation Failed",
                    Detail = domainEx.Message
                };
                problem.Extensions["code"] = "DOMAIN_VALIDATION_ERROR";
                break;

            case HospitalNotActiveException hospitalEx:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Hospital Not Onboarded",
                    Detail = hospitalEx.Message
                };
                problem.Extensions["code"] = "HOSPITAL_NOT_ONBOARDED";
                break;

            case PermissionDeniedException permissionEx:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Permission Denied",
                    Detail = permissionEx.Message
                };
                problem.Extensions["code"] = permissionEx.Code;
                break;

            case NotFoundException notFoundEx:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Resource Not Found",
                    Detail = notFoundEx.Message
                };
                problem.Extensions["code"] = "NOT_FOUND";
                break;

            default:
                _logger.LogError(exception, "Unhandled exception caught by GlobalExceptionHandlingMiddleware.");
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Internal Server Error",
                    Detail = _environment.IsDevelopment()
                        ? exception.Message
                        : "An unexpected error occurred while processing the request."
                };
                problem.Extensions["code"] = "INTERNAL_SERVER_ERROR";
                break;
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
