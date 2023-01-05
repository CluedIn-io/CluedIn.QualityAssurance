namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;

internal interface IRawCluesOptions : IClueSendingOperationOptions
{
    string CluesDirectory { get; set; }
}