using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using CluedIn.QualityAssurance.Cli.Models.CluedIn;
using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Operations.VerifyEdges;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Authenticators;

namespace CluedIn.QualityAssurance.Cli.Operations.ValidateEdgeCreation;

internal class ValidateEdgeCreationOperation : Operation<ValidateEdgeCreationOptions>
{
    private readonly ILogger<ValidateEdgeCreationOperation> _logger;
    private static Dictionary<string, int> HashFileCounts { get; set; } = new();
    private static List<OrganizationEdgeDetails> OrganizationIdToHash { get; set; } = new();

    class OrganizationEdgeDetails
    {
        public string OrganizationId { get; set; }
        public string EdgeHash { get; set; }
        public int NumberOfEdges { get; set; }
    }

    public ValidateEdgeCreationOperation(ILogger<ValidateEdgeCreationOperation> logger)
    {
        _logger = logger;
    }

    public override async Task ExecuteAsync(ValidateEdgeCreationOptions options, CancellationToken cancellationToken)
    {
        var nowInTicks = DateTime.Now.Ticks;
        var outputFolder = Path.Combine(options.OutputDirectory, nowInTicks.ToString());

        for (int i = 0; i < options.Iterations; i++)
        {
            await Run(options, cancellationToken, outputFolder);

            if (cancellationToken.IsCancellationRequested)
                return;
        }
    }

    private async Task Run(ValidateEdgeCreationOptions options, CancellationToken cancellationToken, string outputFolder)
    {
        var files = Directory.GetFiles(options.CluesFolder, "*.xml").OrderBy(x => x).ToArray();

        var client = new RestClient();

        var nowInTicks = DateTime.Now.Ticks;

        var org = $"myorg{nowInTicks}";
        var adminEmail = $"admin@{org}.com";

        Directory.CreateDirectory(outputFolder);

        _logger.LogInformation($"Creating account");

        var newAccount = new RestRequest($"{options.AuthApiUrl}api/account/new", Method.Post);
        newAccount.RequestFormat = DataFormat.Json;
        newAccount.AddHeader("content-type", "application/x-www-form-urlencoded");
        newAccount.AddParameter("grant_type", "password");
        newAccount.AddParameter("allowEmailDomainSignup", "true");
        newAccount.AddParameter("username", adminEmail);
        newAccount.AddParameter("email", adminEmail);
        newAccount.AddParameter("password", "Foobar23!");
        newAccount.AddParameter("confirmpassword", "Foobar23!");
        newAccount.AddParameter("applicationSubDomain", org);
        newAccount.AddParameter("organizationName", org);
        newAccount.AddParameter("emailDomain", adminEmail.Split('@')[1]);
        await client.ExecuteAsync(newAccount);

        _logger.LogInformation($"Created account {adminEmail}");

        _logger.LogInformation($"Generating access token");

        var getTokenRequest = new RestRequest($"{options.AuthApiUrl}connect/token", Method.Post);
        getTokenRequest.RequestFormat = DataFormat.Json;
        getTokenRequest.AddParameter("client_id", org);
        getTokenRequest.AddParameter("grant_type", "password");
        getTokenRequest.AddParameter("username", adminEmail);
        getTokenRequest.AddParameter("password", "Foobar23!");
        var getTokenResponse = await client.ExecuteAsync<GetTokenResponse>(getTokenRequest);

        var token = getTokenResponse.Data.access_token;

        _logger.LogInformation($"Generating access token generated");

        _logger.LogInformation($"Fetching organization id");

        var request = new RestRequest($"{options.WebApiUrl}api/organization");
        request.RequestFormat = DataFormat.Json;
        request.AddParameter("bearer", token, ParameterType.QueryString);
        var response = client.Execute<GetOrgResponse>(request);
        var organisationId = response.Data.Id;

        _logger.LogInformation($"Fetched organization id {organisationId}");

        var idSuffix = "-testrun-" + organisationId;

        long serverLogOffset = 0;

        if (!string.IsNullOrWhiteSpace(options.ServerLogFile))
        {
            using (var fs = File.Open(options.ServerLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                serverLogOffset = fs.Length;
            }
        }

        _logger.LogInformation("Sending clues");

        var d = DateTime.Now;
        int idx;
        for (idx = 0; idx < files.Length; idx++)
        {
            var f = files[idx];

            var xml = File.ReadAllText(f);

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);

            var clue = xmlDoc.SelectSingleNode("/clue");

            clue.Attributes["organization"].Value = organisationId;

            foreach (var node in xmlDoc.SelectNodes("//codes/value").OfType<XmlNode>())
            {
                node.InnerText += idSuffix;
            }

            foreach (var node in xmlDoc.SelectNodes("//edge").OfType<XmlNode>())
            {
                node.Attributes["from"].Value += idSuffix;
                node.Attributes["to"].Value += idSuffix;
            }

            foreach (var node in xmlDoc.SelectNodes("//*[@origin]").OfType<XmlNode>())
            {
                node.Attributes["origin"].Value += idSuffix;
            }

            xml = xmlDoc.OuterXml;


            request = new RestRequest($"{options.PublicApiUrl}api/v2/clue", Method.Post);
            request.RequestFormat = DataFormat.Json;
            request.AddParameter("save", "true", ParameterType.QueryString);
            request.AddParameter("bearer", token, ParameterType.QueryString);
            request.AddParameter("text/xml", xml, ParameterType.RequestBody);

            await client.ExecuteAsync(request);

            if (DateTime.Now.Subtract(d).TotalSeconds > 5)
            {
                _logger.LogInformation($"Sent {idx}/{files.Length} clues");
                d = DateTime.Now;
            }

            if (cancellationToken.IsCancellationRequested)
                return;
        }

        _logger.LogInformation($"Sent {idx}/{files.Length} clues");

        var rabbitClient = new RestClient();
        rabbitClient.Authenticator = new HttpBasicAuthenticator("guest", "guest");

        var deadLetterQueueRequest =
            new RestRequest(
                $"{options.RabbitMQAdminUrl}api/queues/%2F/DeadLetterCommands?lengths_age=600&lengths_incr=5&msg_rates_age=600&msg_rates_incr=5&data_rates_age=600&data_rates_incr=5");
        var queueRequest =
            new RestRequest(
                $"{options.RabbitMQAdminUrl}api/overview?lengths_age=600&lengths_incr=5&msg_rates_age=600&msg_rates_incr=5");

        _logger.LogInformation("Waiting for messages to arrive in rabbitmq");
        RabbitOverview rabbitOverview;
        RabbitQueue deadLetterQueue;

        int i = 0;
        do
        {
            Thread.Sleep(1000);
            rabbitOverview = rabbitClient.Execute<RabbitOverview>(queueRequest).Data;
            deadLetterQueue = rabbitClient.Execute<RabbitQueue>(deadLetterQueueRequest).Data;

            if (cancellationToken.IsCancellationRequested)
                return;
        } while (rabbitOverview.QueueTotals.Messages - deadLetterQueue.BackingQueueStatus.Len <= 0 && i++ < 60);

        _logger.LogInformation("Waiting for all messages to be processed in rabbitmq");
        do
        {
            Thread.Sleep(1000);
            rabbitOverview = rabbitClient.Execute<RabbitOverview>(queueRequest).Data;
            deadLetterQueue = rabbitClient.Execute<RabbitQueue>(deadLetterQueueRequest).Data;

            if (cancellationToken.IsCancellationRequested)
                return;
        } while (rabbitOverview.QueueTotals.Messages - deadLetterQueue.BackingQueueStatus.Len > 0);

        _logger.LogInformation("Fetching edge data from neo");
        var driver = GraphDatabase.Driver("bolt://localhost:7687", AuthTokens.Basic("neo4j", "password"));

        {
            var query = $@"MATCH (n)-[e]->(r) 
WHERE type(e) <> '/Code'
AND type(e) <> '/DiscoveredAt'
AND type(e) <> '/ModifiedAt'
AND n.`Attribute-origin` STARTS WITH '{org}'
RETURN head(n.Codes) AS source, type(e) AS type, head(r.Codes) AS destination
ORDER BY source, type, destination";

            using (var session = driver.AsyncSession())
            {
                await session.ExecuteReadAsync(async tx =>
                {
                    var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
                    var resultList = await result.ToListAsync();
                    var resultObjectList = resultList.Select(currentResult => new Result(
                            RemapCode(currentResult.Values["source"].ToString(), org),
                            currentResult.Values["type"].ToString(),
                            RemapCode(currentResult.Values["destination"].ToString(), org)))
                        .Where(x => !x.source.Contains("#CluedIn"))
                        .Distinct()
                        .OrderBy(x => x.source)
                        .ThenBy(x => x.type)
                        .ThenBy(x => x.destination)
                        .ToArray();

                    var outputCsvFileName = $"{outputFolder}\\edges-{org}.csv";

                    using (var writer = new StreamWriter(outputCsvFileName))
                    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csv.WriteRecords(resultObjectList);
                    }

                    byte[] hash;
                    using (var md5 = MD5.Create())
                    using (var stream = File.OpenRead(outputCsvFileName))
                    {
                        hash = md5.ComputeHash(stream);
                    }

                    var sHash = HttpUtility.UrlEncode(Convert.ToBase64String(hash));
                    var hashFileName = $"{outputFolder}\\edges-{sHash}.csv";

                    if (!File.Exists(hashFileName))
                    {
                        File.Copy(outputCsvFileName, hashFileName);
                    }

                    if (!HashFileCounts.ContainsKey(sHash))
                    {
                        HashFileCounts.Add(sHash, 1);
                    }
                    else
                    {
                        HashFileCounts[sHash] += 1;
                    }

                    OrganizationIdToHash.Add(new OrganizationEdgeDetails
                    {
                        OrganizationId = organisationId,
                        EdgeHash = sHash,
                        NumberOfEdges = resultObjectList.Length,
                    });
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(options.ServerLogFile))
        {
            using (var fs = File.Open(options.ServerLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(serverLogOffset, SeekOrigin.Begin);

                using (var outputFs = File.OpenWrite($"{outputFolder}\\server-log-{org}.txt"))
                {
                    fs.CopyTo(outputFs);
                }
            }
        }

        Console.WriteLine(JsonConvert.SerializeObject(HashFileCounts, Newtonsoft.Json.Formatting.Indented));

        File.WriteAllText($"{outputFolder}\\results.json", JsonConvert.SerializeObject(new
        {
            EdgeSummary = HashFileCounts.Select(x => new
            {
                EdgeHash = x.Key,
                NumberOfOccurances = x.Value
            }).ToList(),
            OrganizationDetails = OrganizationIdToHash
        },
            Newtonsoft.Json.Formatting.Indented));
    }

    static string RemapCode(string s, string orgName)
    {
        s = Regex.Replace(s, $"^{orgName}~", "");
        s = Regex.Replace(s, "-testrun.*", "");

        return s;
    }

    public record Result(string source, string type, string destination);
}