using HospitalCore.Application.Common.Interfaces.Repositories;
using HospitalCore.Application.Common.Mediator;
using Microsoft.Extensions.Logging;

namespace HospitalCore.Application.Common.Behaviors;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(IUnitOfWork unitOfWork, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ITransactionalCommand)
        {
            return await next();
        }

        var requestName = typeof(TRequest).Name;
        _logger.LogDebug("Beginning UnitOfWork transaction for command {CommandName}", requestName);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            _logger.LogDebug("Committed transaction for command {CommandName}", requestName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rolling back transaction for command {CommandName} due to unhandled error", requestName);
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
