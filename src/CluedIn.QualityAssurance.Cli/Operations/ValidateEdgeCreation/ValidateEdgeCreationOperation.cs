using System.Xml;
using CluedIn.QualityAssurance.Cli.Models.CluedIn;
using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Operations.VerifyEdges;
using CluedIn.QualityAssurance.Cli.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Authenticators;

namespace CluedIn.QualityAssurance.Cli.Operations.ValidateEdgeCreation;

internal class ValidateEdgeCreationOperation : Operation<ValidateEdgeCreationOptions>
{
    private readonly ILogger<ValidateEdgeCreationOperation> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private EdgeExporter _edgeExporter;

    public ValidateEdgeCreationOperation(ILogger<ValidateEdgeCreationOperation> logger, EdgeExporter edgeExporter)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _edgeExporter = edgeExporter ?? throw new ArgumentNullException(nameof(edgeExporter));
    }

    public override async Task ExecuteAsync(ValidateEdgeCreationOptions options, CancellationToken cancellationToken)
    {
        var nowInTicks = DateTime.Now.Ticks;
        var outputFolder = Path.Combine(options.OutputDirectory, nowInTicks.ToString());

        _edgeExporter.Initialize(outputFolder, options.Neo4jUrl, options.Neo4jUsername, options.Neo4jPassword);

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

        var startedAt = DateTime.Now;
        var d = DateTime.Now;
        int idx;
        for (idx = 0; idx < files.Length; idx++)
        {
            var f = files[idx];

            var xml = File.ReadAllText(f);

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);

            var clue = xmlDoc.SelectSingleNode("/clue");

            //foreach (var node in xmlDoc.SelectNodes("//entityData/name").OfType<XmlNode>())
            //{
            //    node.InnerText += idSuffix;
            //}

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

            //var entityData = xmlDoc.SelectSingleNode("//data//entityData");

            //var modifiedDate = xmlDoc.CreateElement("modifiedDate");
            //modifiedDate.InnerText = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.ffff");
            //Thread.Sleep(10);

            //entityData.AppendChild(modifiedDate);

            xml = xmlDoc.OuterXml;


            request = new RestRequest($"{options.PublicApiUrl}api/v2/clue", Method.Post);
            request.RequestFormat = DataFormat.Json;
            request.AddParameter("save", "true", ParameterType.QueryString);
            request.AddParameter("bearer", token, ParameterType.QueryString);
            request.AddParameter("text/xml", xml, ParameterType.RequestBody);

            //Thread.Sleep(5000);

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
        var errorrQueueRequest =
            new RestRequest(
                $"{options.RabbitMQAdminUrl}api/queues/%2F/EasyNetQ_Default_Error_Queue?lengths_age=600&lengths_incr=5&msg_rates_age=600&msg_rates_incr=5&data_rates_age=600&data_rates_incr=5");
        var queueRequest =
            new RestRequest(
                $"{options.RabbitMQAdminUrl}api/overview?lengths_age=600&lengths_incr=5&msg_rates_age=600&msg_rates_incr=5");

        _logger.LogInformation("Waiting for messages to arrive in rabbitmq");
        RabbitOverview rabbitOverview;
        RabbitQueue deadLetterQueue;
        RabbitQueue errorQueue;

        int i = 0;
        do
        {
            Thread.Sleep(1000);
            rabbitOverview = rabbitClient.Execute<RabbitOverview>(queueRequest).Data;
            deadLetterQueue = rabbitClient.Execute<RabbitQueue>(deadLetterQueueRequest).Data;
            errorQueue = rabbitClient.Execute<RabbitQueue>(errorrQueueRequest).Data;

            if (cancellationToken.IsCancellationRequested)
                return;
        } while (rabbitOverview.QueueTotals.Messages - deadLetterQueue.BackingQueueStatus.Len - (errorQueue.BackingQueueStatus?.Len ?? 0l) <= 0 && i++ < 60);

        _logger.LogInformation("Waiting for all messages to be processed in rabbitmq");
        do
        {
            Thread.Sleep(1000);
            rabbitOverview = rabbitClient.Execute<RabbitOverview>(queueRequest).Data;
            deadLetterQueue = rabbitClient.Execute<RabbitQueue>(deadLetterQueueRequest).Data;
            errorQueue = rabbitClient.Execute<RabbitQueue>(errorrQueueRequest).Data;

            if (cancellationToken.IsCancellationRequested)
                return;
        } while (rabbitOverview.QueueTotals.Messages - deadLetterQueue.BackingQueueStatus.Len - (errorQueue.BackingQueueStatus?.Len ?? 0l) > 0);

        var timeTaken = DateTime.Now.Subtract(startedAt);
        var mapping = new Dictionary<string, string>
        {
            [$"^{org}~"] = "",
            ["-testrun.*"] = ""
        };
        await _edgeExporter.ExportEdges(org, organisationId, timeTaken, mapping).ConfigureAwait(false);

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

        Console.WriteLine(
            JsonConvert.SerializeObject(
                _edgeExporter.Summary.ToDictionary(x => x.Hash, x => x.NumberOfOccurances),
                Newtonsoft.Json.Formatting.Indented));
    }
}
