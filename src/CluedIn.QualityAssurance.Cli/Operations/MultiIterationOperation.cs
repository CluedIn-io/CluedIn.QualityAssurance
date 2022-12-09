using CluedIn.QualityAssurance.Cli.Models.Operations;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations;

internal abstract class MultiIterationOperation<TOptions, TOverallResult, TIterationResult> : Operation<TOptions>
    where TOverallResult : IMultiIterationOperationResult<TIterationResult>, new()
    where TIterationResult : class, IOperationResult, new()
    where TOptions : IMultiIterationOptions
{
    private static readonly TimeSpan DelayBetweenIterations = TimeSpan.FromSeconds(15);

    protected MultiIterationOperation(ILogger<MultiIterationOperation<TOptions, TOverallResult, TIterationResult>> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<MultiIterationOperation<TOptions, TOverallResult, TIterationResult>> Logger { get; }
    protected TOptions Options { get; private set; }
    protected TOverallResult OverallResult { get; private set; }

    public override async Task ExecuteAsync(TOptions options, CancellationToken cancellationToken)
    {
        Options = options;

        OverallResult = new TOverallResult
        {
            StartTime= DateTimeOffset.UtcNow,
        };
        await SetUpOperationAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < Options.TotalIterations; ++i)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("Cancellation has been requested. Will skip processing further iterations.");
                return;
            }

            try
            {
                var result = await ExecuteIterationAsync(i, cancellationToken).ConfigureAwait(false);
                OverallResult.IterationResults.Add(result);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An exception has occurred while trying to perform test run.");
                OverallResult.IterationResults.Add(new TIterationResult());
            }

            // Keep saving on each iteration in order to prevent a crash causing all data loss
            await ProcessResultAsync(OverallResult, cancellationToken).ConfigureAwait(false);
            if (i != Options.TotalIterations - 1)
            {
                Logger.LogInformation("Waiting for {Delay} before starting the next iteration.", DelayBetweenIterations);
                await Task.Delay(DelayBetweenIterations, cancellationToken).ConfigureAwait(false);
            }
        }
        await TearDownOperationAsync(cancellationToken).ConfigureAwait(false);

        OverallResult.EndTime = DateTimeOffset.UtcNow;
        Logger.LogInformation("Operation has been completed at {EndTime}. Total time taken was {TotalSeconds} seconds.", OverallResult.EndTime, OverallResult.TotalTimeInSeconds);
    }

    protected abstract Task SetUpOperationAsync(CancellationToken cancellationToken);
    protected abstract Task TearDownOperationAsync(CancellationToken cancellationToken);
    protected abstract Task ProcessResultAsync(TOverallResult results, CancellationToken cancellationToken);
    protected abstract Task<TIterationResult> ExecuteIterationAsync(int iterationNumber, CancellationToken cancellationToken);
}
