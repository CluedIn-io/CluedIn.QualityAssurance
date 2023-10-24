using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

internal class CustomQueryAction : IPostOperationAction
{
    public CustomQueryAction(ILogger<CustomQueryAction> logger, IEnvironment testEnvironment, IFileSourceOperationOptions options)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Environment = testEnvironment ?? throw new ArgumentNullException(nameof(testEnvironment));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private ILogger<CustomQueryAction> Logger { get; }
    private IEnvironment Environment { get; }
    private IFileSourceOperationOptions Options { get; }
    private CustomOutputOptions? CustomOptions { get; set; }

    public async Task ExecuteAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        CustomOptions = await GetTestResultCustomizationsAsync(Options.InputFilePath).ConfigureAwait(false);
        Logger.LogInformation("Begin populating custom test result values.");
        var invalidSources = CustomOptions.TestResultValues.Where(value => value.Source != TestResultCustomizationSource.Neo4j);
        if (invalidSources.Any())
        {
            throw new InvalidOperationException($"Only source type '{TestResultCustomizationSource.Neo4j}' is supported. '{string.Join(',', invalidSources)}' found.");
        }

        var invalidTypes = CustomOptions.TestResultValues.Where(value => value.Type != TestResultCustomizationType.SingleValue);
        if (invalidSources.Any())
        {
            throw new InvalidOperationException($"Only value type '{TestResultCustomizationType.SingleValue}' is supported. '{string.Join(',', invalidTypes)}' found.");
        }

        var neo4jConnectionInfo = await Environment.GetNeo4jConnectionInfoAsync(cancellationToken);
        var driver = GraphDatabase.Driver(neo4jConnectionInfo.BoltUri, AuthTokens.Basic(neo4jConnectionInfo.UserName, neo4jConnectionInfo.Password));

        using var session = driver.AsyncSession();

        foreach (var current in CustomOptions.TestResultValues)
        {
            string query = FormatString(result.Organization, current.Query);
            Logger.LogInformation("Begin running query to Neo4j with query '{Query}'.", query);
            var queryResult = await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
                var record = await result.SingleAsync().ConfigureAwait(false);
                return record.Values.First();
            }).ConfigureAwait(false);
            Logger.LogInformation("End runing query to Neo4j with query '{Query}' and got result '{Result}'.", query, queryResult);

            result.Output.Add(current.Name, queryResult);
        }

        Logger.LogInformation("End populating custom test result values.");
    }

    protected string FormatString(Organization organization, string stringToFormat)
    {
        return stringToFormat
                .Replace("{{ClientId}}", organization.ClientId.ToString())
                .Replace("{{OrganizationId}}", organization.OrganizationId.ToString())
                .Replace("{{UserId}}", organization.UserId.ToString());
    }

    private async Task<CustomOutputOptions> GetTestResultCustomizationsAsync(string testFilePath)
    {
        if (TestFileHelper.TryGetCustomizationFileStream(testFilePath, out var customizationFileStream))
        {
            using var reader = new StreamReader(customizationFileStream);
            var json = await reader.ReadToEndAsync();

            var jsonObj = JsonNode.Parse(json);
            var serializeOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters =
            {
                new JsonStringEnumConverter()
            }
            };

            var customResults = jsonObj?["customResult"];

            var resultValues = customResults?["testResultValues"]?.AsArray()?.Deserialize<List<CustomTestResultValue>>(serializeOptions) ?? new List<CustomTestResultValue>();

            return new CustomOutputOptions
            {
                TestResultValues = resultValues,
            };
        }

        return new CustomOutputOptions();
    }

    public class CustomOutputOptions
    {
        public IEnumerable<CustomTestResultValue> TestResultValues { get; set; } = Enumerable.Empty<CustomTestResultValue>();
    }

    public class CustomTestResultValue
    {
        public string Name { get; set; }

        public TestResultCustomizationSource Source { get; set; }

        public TestResultCustomizationType Type { get; set; }

        public string? Query { get; set; }
    }

    public enum TestResultCustomizationSource
    {
        Undefined = 0,
        Neo4j = 1,
    }

    public enum TestResultCustomizationType
    {
        Undefined = 0,
        SingleValue = 1,
    }
}
