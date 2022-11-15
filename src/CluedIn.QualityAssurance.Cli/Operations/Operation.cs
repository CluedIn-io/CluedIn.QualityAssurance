namespace CluedIn.QualityAssurance.Cli.Operations
{
    internal abstract class Operation<T>
    {
        public abstract Task ExecuteAsync(T options, CancellationToken cancellationToken);
    }
}
