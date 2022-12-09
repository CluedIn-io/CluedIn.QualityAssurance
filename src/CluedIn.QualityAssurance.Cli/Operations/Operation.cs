namespace CluedIn.QualityAssurance.Cli.Operations;

internal abstract class Operation<TOptions>
    where TOptions : IOperationOptions
{
    public abstract Task ExecuteAsync(TOptions options, CancellationToken cancellationToken);
}
