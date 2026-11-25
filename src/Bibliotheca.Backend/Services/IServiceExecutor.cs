namespace Bibliotheca.Backend.Services;

public interface IServiceExecutor<TArgs, TResult>
{
    Task<TResult> ExecuteAsync(TArgs args, CancellationToken cancellationToken = default);
}