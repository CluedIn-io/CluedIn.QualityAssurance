using CluedIn.Core.Data;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Nest;
using System.Globalization;
using System.Net.Http.Headers;

namespace CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

internal class CluesSubmissionAssertion : IPostOperationAction
{
    public CluesSubmissionAssertion(ILogger<CluesSubmissionAssertion> logger, IEnvironment testEnvironment, IHttpClientFactory httpClientFactory, IRawCluesOptions rawCluesOptions)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TestEnvironment = testEnvironment ?? throw new ArgumentNullException(nameof(testEnvironment));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        RawCluesOptions = rawCluesOptions ?? throw new ArgumentNullException(nameof(rawCluesOptions));
    }

    private ILogger<CluesSubmissionAssertion> Logger { get; }
    private IEnvironment TestEnvironment { get; }
    public IHttpClientFactory HttpClientFactory { get; }
    private IRawCluesOptions RawCluesOptions { get; }

    public async Task ExecuteAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        var neo4jConnectionInfo = await TestEnvironment.GetNeo4jConnectionInfoAsync(cancellationToken);
        var elasticConnectionInfo = await TestEnvironment.GetElasticSearchConnectionInfoAsync(cancellationToken);

        var elasticSettings = new ConnectionSettings(elasticConnectionInfo.ServerUri)
            .DefaultIndex(result.Organization.ClientId)
            .BasicAuthentication(elasticConnectionInfo.UserName, elasticConnectionInfo.Password);
        var elasticClient = new ElasticClient(elasticSettings);

        var totalCodeWithNoEntities = 0;
        var totalCodeWithMultipleEntities = 0;
        var totalEntitiesWithMissingProperties = 0;
        var unprocessedCluesCount = 0;
        string cluesDirectory = RawCluesOptions.CluesDirectory;
        string organizationName = result.Organization.ClientId;
        var files = Directory.GetFiles(cluesDirectory, "*.xml").OrderBy(x => x);

        var failedClues = new List<FailedClue>();
        foreach (var file in files)
        {
            var contents = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var clue = CluedInSerializer.DeserializeFromXml<Clue>(CluesHelper.AppendTestRunSuffixToClueXml(contents, result.Organization.OrganizationId));

            var results = await elasticClient
                .SearchAsync<ElasticEntity>(
                document => document.Query(
                    q => q.Match(
                        m => m.Field(e => e.Codes).Query($"{clue.OriginEntityCode}")
                    )
                ).Source(sr => sr.Includes(fi => fi.Field(f => f.Id)))
            ).ConfigureAwait(false);

            if (!results.Documents.Any())
            {
                totalCodeWithNoEntities++;
                failedClues.Add(new FailedClue
                {
                    FileName = file,
                    OriginCode= clue?.OriginEntityCode?.ToString(),
                    IsMissing= true,
                });
                continue;
            }
            else if (results.Documents.Count > 1)
            {
                totalCodeWithMultipleEntities++;
                failedClues.Add(new FailedClue
                {
                    FileName = file,
                    OriginCode = clue?.OriginEntityCode?.ToString(),
                    IsMultiple = true,
                });
                continue;
            }

            var elasticEntity = results.Documents.First();

            var uris = await TestEnvironment.GetServerUriCollectionAsync(cancellationToken);
            var httpClient = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Organization.AccessToken);
            var response = await httpClient.GetStringAsync(new Uri(uris.WebApiUri, $"api/entity?id={elasticEntity.Id}&full=true"), cancellationToken).ConfigureAwait(false);

            var entity = CluedInSerializer.DeserializeFromJson<Entity>(response);

            var entityClues = entity.ConvertToClues();

            var entityPropertyKeys = entity.Properties.Keys;
            var cluePropertyKeys = clue.Details.Data.EntityData.Properties.Keys.ToHashSet();
            var hasAllClueProperties = cluePropertyKeys.All(key => entityPropertyKeys.Contains(key));
            if (!hasAllClueProperties)
            {
                totalEntitiesWithMissingProperties++;
            }

            var hasExactMatch = entityClues.Any(entityClue => entityClue.IsExactMatch(clue));
            if(!hasExactMatch)
            {
                unprocessedCluesCount++;
            }
            if (!hasAllClueProperties || !hasExactMatch)
            {
                failedClues.Add(new FailedClue
                {
                    FileName = file,
                    OriginCode = clue?.OriginEntityCode?.ToString(),
                    IsMissingProperties = !hasAllClueProperties,
                    IsUnprocessed = !hasExactMatch,
                    EntityId = elasticEntity.Id,
                });
            }
        }

        result.Output.Add("CodeWithNoEntitiesCount", totalCodeWithNoEntities);
        result.Output.Add("CodeWithMultipleEntitiesCount", totalCodeWithMultipleEntities);
        result.Output.Add("EntitiesWithMissingPropertiesCount", totalEntitiesWithMissingProperties);
        result.Output.Add("UnprocessedCluesCount", unprocessedCluesCount);

        using var writer = new StreamWriter(Path.Combine(RawCluesOptions.OutputDirectory, $"cluessubmissions-{result.Organization.ClientId}.csv"));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(failedClues);
    }

    private class ElasticEntity
    {
        public string Id { get; set; }

        public ICollection<string> Codes { get; set; }
    }

    private class FailedClue
    {
        public string FileName { get; init; }

        public bool IsMissing { get; init; }

        public bool IsMultiple { get; init; }

        public bool IsUnprocessed { get; init; }

        public bool IsMissingProperties { get; init; }

        public string EntityId { get; init; }

        public string? OriginCode { get; init; }
    }
}
