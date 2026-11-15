using System.Diagnostics;
using HospitalCore.Application.Common.Mediator;
using Microsoft.Extensions.Logging;

namespace HospitalCore.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var correlationId = Guid.NewGuid().ToString("N");

        _logger.LogInformation("[{CorrelationId}] Processing request: {RequestName}", correlationId, requestName);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();
            _logger.LogInformation("[{CorrelationId}] Completed request: {RequestName} in {ElapsedMs}ms",
                correlationId, requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[{CorrelationId}] Request {RequestName} failed after {ElapsedMs}ms",
                correlationId, requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
