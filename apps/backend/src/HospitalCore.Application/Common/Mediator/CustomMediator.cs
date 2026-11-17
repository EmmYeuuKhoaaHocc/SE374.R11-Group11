using Microsoft.Extensions.DependencyInjection;

namespace HospitalCore.Application.Common.Mediator;

public class CustomMediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public CustomMediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        var handler = _serviceProvider.GetService(handlerType);
        if (handler is null)
        {
            throw new InvalidOperationException($"No handler registered for request type '{requestType.FullName}'.");
        }

        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = _serviceProvider.GetServices(behaviorType).Cast<dynamic>().ToList();

        RequestHandlerDelegate<TResponse> executionDelegate = () =>
        {
            var method = handlerType.GetMethod("Handle")!;
            return (Task<TResponse>)method.Invoke(handler, new object[] { request, cancellationToken })!;
        };

        for (int i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = executionDelegate;
            executionDelegate = () => (Task<TResponse>)behavior.Handle((dynamic)request, next, cancellationToken);
        }

        return await executionDelegate();
    }
}
