using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using Microsoft.Extensions.Logging;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using CluedIn.QualityAssurance.Cli.Services.Probes;
using System.Text.Json;
using System.Collections.Generic;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract partial class ClueSendingOperation<TOptions> : MultiIterationOperation<TOptions, MultiIterationOperationResult, SingleIterationOperationResult>
    where TOptions : IClueSendingOperationOptions
{
    protected const string ApplicationJsonContentType = "application/json";
    private static readonly TimeSpan DelayBeforeOperation = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions HeaderSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public ClueSendingOperation(
        ILogger<ClueSendingOperation<TOptions>> logger,
        IEnvironment environment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMQCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory,
        IProbeService probeService)
        : base(logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        ResultWriters = resultWriters ?? throw new ArgumentNullException(nameof(resultWriters));
        CompletionChecker = rabbitMQCompletionChecker ?? throw new ArgumentNullException(nameof(rabbitMQCompletionChecker));
        PostOperationActions = postOperationActions ?? throw new ArgumentNullException(nameof(postOperationActions));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        ProbeService = probeService ?? throw new ArgumentNullException(nameof(probeService));
    }

    private ILogger<ClueSendingOperation<TOptions>> Logger { get; }

    protected Organization? Organization { get; set; }

    protected IEnvironment Environment { get; }

    private IEnumerable<IResultWriter> ResultWriters { get; }

    private IRabbitMQCompletionChecker CompletionChecker { get; }

    private IEnumerable<IPostOperationAction> PostOperationActions { get; }

    private SingleIterationOperationResult? PreIngestionResult { get; set; }

    protected IHttpClientFactory HttpClientFactory { get; }

    private IProbeService ProbeService { get; }

    protected override async Task SetUpOperationAsync(CancellationToken cancellationToken)
    {
        if (Options.ShouldCreateSubdirectoryForOutput)
        {
            Options.OutputDirectory = Path.Combine(Options.OutputDirectory, GetTestIdPrefix());
            _ = Directory.CreateDirectory(Options.OutputDirectory);
            Logger.LogInformation("Updating output directory to be {OutputDirectoryPath}' because create-subdirectory-for-output is set.", Options.OutputDirectory);
        }

        await Environment.SetupAsync(cancellationToken).ConfigureAwait(false);
        if (Options.IsReingestion)
        {
            await CreateOperationData(0).ConfigureAwait(false);
            PreIngestionResult = await ExecuteIterationInternalAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override Task TearDownOperationAsync(CancellationToken cancellationToken)
    {
        return Environment.TearDownAsync(cancellationToken);
    }

    protected override async Task ProcessResultAsync(MultiIterationOperationResult results, CancellationToken cancellationToken)
    {
        foreach (var currentWriter in ResultWriters)
        {
            try
            {
                Logger.LogDebug("Begin writing result using. '{ResultWriter}'.", currentWriter.GetType().FullName);
                await currentWriter.ProcessAsync(Options.OutputDirectory, results, cancellationToken).ConfigureAwait(false);
                Logger.LogDebug("End writing result using. '{ResultWriter}'.", currentWriter.GetType().FullName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error when writing result using '{ResultWriter}'.", currentWriter.GetType().FullName);
            }
        }
    }

    private static Dictionary<string, object> CreateIterationScope(string clientId)
    {
        return new Dictionary<string, object>
        {
            ["ClientId"] = clientId,
        };
    }

    protected override async Task<SingleIterationOperationResult> ExecuteIterationAsync(int iterationNumber, CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        try
        {
            await SetOrganizationAsync(iterationNumber);
        }
        catch(Exception ex)
        {
            return createErrorResult(ex);
        }

        using var scope = Logger.BeginScope(CreateIterationScope(Organization.ClientId));
        try
        {
            return await ExecuteIterationInternalAsync(Options.IsReingestion, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "A http exception has occurred while trying to perform test run.");
            if (ex.StatusCode.HasValue)
            {
                var statusCode = (int)ex.StatusCode.Value;
                if (statusCode >= 400 && statusCode <= 403)
                {
                    Logger.LogError(ex, "Something is wrong with the HTTP request sent or the credentials. Exiting test.");
                    throw;
                }
            }
            var result = new SingleIterationOperationResult
            {
                HasErrors = true,
                Error = ex.Message + System.Environment.NewLine + ex.StackTrace
            };
            return result;
        }
        catch (Exception ex)
        {
            return createErrorResult(ex);
        }

        SingleIterationOperationResult createErrorResult(Exception ex)
        {
            Logger.LogError(ex, "An exception has occurred while trying to perform test run.");
            var result = new SingleIterationOperationResult
            {
                StartTime = startTime,
                EndTime = DateTimeOffset.UtcNow,
                HasErrors = true,
                Error = ex.Message + System.Environment.NewLine + ex.StackTrace
            };
            return result;
        }
    }

    protected virtual async Task SetOrganizationAsync(int iterationNumber)
    {
        if (!Options.IsReingestion)
        {
            await CreateOperationData(iterationNumber).ConfigureAwait(false);
        }
        else
        {
            if (PreIngestionResult == null)
            {
                throw new InvalidOperationException("Pre ingestion is null.");
            }
            Organization = PreIngestionResult.Organization;
        }
    }

    private async Task<SingleIterationOperationResult> ExecuteIterationInternalAsync(bool isReingestion, CancellationToken cancellationToken)
    {
        var result = new SingleIterationOperationResult
        {
            MemoryStatistics = new MemoryStatistics
            {
                Before = await Environment.GetAvailableMemoryInMegabytesAsync(cancellationToken).ConfigureAwait(false),
            },
            Organization = Organization,
        };

        var operations = await GetSetupOperationsAsync(isReingestion, cancellationToken).ConfigureAwait(false);
        await ExecuteSetupOperationsAsync(operations, cancellationToken).ConfigureAwait(false);

        await ProbeService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await CompletionChecker.InitializeAsync(cancellationToken).ConfigureAwait(false);
        result.StartTime = DateTimeOffset.UtcNow;

        using var ingestionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(Options.TimeoutInMinutes), cancellationToken);
        var completionCheckerTask = CompletionChecker.PollForCompletionAsync(ingestionCancellationTokenSource.Token);
        var probeTask = ProbeService.PollUntilCancellationAsync(ingestionCancellationTokenSource.Token);
        var testRunTask = Task.WhenAll(ExecuteIngestionAsync(ingestionCancellationTokenSource.Token), completionCheckerTask);//, probeTask);

        var hasTimedOut = false;
        if (await Task.WhenAny(timeoutTask, testRunTask).ConfigureAwait(false) == timeoutTask)
        {
            hasTimedOut = true;
            Logger.LogWarning("Time out waiting for completion.");
        }
        else
        {
            Logger.LogInformation("Successfully waited for completion. Test ran from {Start} to {End}.", result.StartTime, result.EndTime);
        }

        ingestionCancellationTokenSource.Cancel();
        var completionCheckerResult = await completionCheckerTask.ConfigureAwait(false);
        var probeResults = await probeTask.ConfigureAwait(false);
        result.HasTimedOut = hasTimedOut;
        result.QueuePollingHistory = completionCheckerResult.QueuePollingHistory;
        result.Probes = probeResults.ToList();
        result.EndTime = hasTimedOut ? DateTimeOffset.UtcNow : completionCheckerResult.EndTime;

        PopulateQueueStats(result, completionCheckerResult, cancellationToken);
        await CustomizeResultAsync(result, cancellationToken).ConfigureAwait(false);
        result.MemoryStatistics.After = await Environment.GetAvailableMemoryInMegabytesAsync(cancellationToken).ConfigureAwait(false);

        if (Options.SkipPostOperationActions)
        {
            Logger.LogInformation("Skipping post operation actions because it is set to skipped in options.");
        }
        else
        {
            try
            {
                await RunPostOperationActions(result, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error performing post oepration actions.");
            }
        }

        Logger.LogInformation("Finished processing result for {Organization}.", Organization?.ClientId);
        return result;
    }

    private async Task RunPostOperationActions(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Running post operation actions with allowed list {AllowedList}.", Options.AllowedPostOperationActions);
        
        foreach (var current in PostOperationActions)
        {
            // TODO: Create a http client to login automatically
            await LoginAsync(cancellationToken);
            var currentActionName = current.GetType().Name;
            if (Options.AllowedPostOperationActions != null && Options.AllowedPostOperationActions.Any()
                && !Options.AllowedPostOperationActions.Contains(currentActionName))
            {
                Logger.LogInformation("Skipping post operation actions {PostOperationActionName} because it's not in allowed list.", currentActionName);
                continue;
            }

            try
            {
                Logger.LogInformation("Begin running post operation actions {PostOperationActionName}.", currentActionName);
                await current.ExecuteAsync(result, cancellationToken).ConfigureAwait(false);
                Logger.LogInformation("End running post operation actions {PostOperationActionName}.", currentActionName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error running post operation actions {PostOperationActionName}.", currentActionName);
            }
        }
    }

    protected virtual Task CustomizeResultAsync(SingleIterationOperationResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task ExecuteIngestionAsync(CancellationToken cancellationToken);

    private async Task ExecuteSetupOperationsAsync(IEnumerable<SetupOperation> operations, CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            using var scope = operation.LoggingScopeState != null ? Logger.BeginScope(operation.LoggingScopeState) : null;
            var operationName = operation.Name ?? operation.Function.Method.Name;
            Logger.LogInformation("Begin operation {OperationName}.", operationName);
            await Task.Delay(DelayBeforeOperation, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("Aborting test because cancellation is requested.");
                return;
            }

            await operation.Function(cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("End operation {OperationName}.", operationName);
        }
    }

    protected virtual Task CreateOperationData(int iterationNumber)
    {
        var testId = CreateTestId(iterationNumber);
        var clientId = Options.ClientIdPrefix + testId;
        Organization = new Organization()
        {
            ClientId = clientId,
            Password = Options.Password,
            UserName = $"{Options.UserName}@{clientId}.com",
            EmailDomain = $"{clientId}.com",
        };
        return Task.CompletedTask;
    }

    protected virtual string CreateTestId(int iterationNumber)
    {
        return $"{GetTestIdPrefix()}x{iterationNumber}";
    }

    protected virtual string CreateOutputFolderName(int iterationNumber)
    {
        return GetTestIdPrefix();
    }

    protected virtual string GetTestIdPrefix()
    {
        if (Options.UseShortTestIdPrefix)
        {
            return $"{OverallResult.StartTime:MMddHHmm}";
        }
        else
        {
            return $"{OverallResult.StartTime:yyyyMMddHHmmss}";
        }
    }

    protected abstract Task<IEnumerable<SetupOperation>> GetSetupOperationsAsync(bool isReingestion, CancellationToken cancellationToken);

    protected virtual async Task CreateOrganizationAsync(CancellationToken cancellationToken)
    {
        var newAccountAccessKey = await Environment.GetNewAccountAccessKeyAsync(cancellationToken).ConfigureAwait(false);
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = new Uri(serverUris.AuthApiUri, "api/account/new");

        var requestBody = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["allowEmailDomainSignup"] = "False",
            ["email"] = Organization.UserName,
            ["username"] = Organization.UserName,
            ["password"] = Organization.Password,
            ["confirmpassword"] = Organization.Password,
            ["emailDomain"] = Organization.EmailDomain,
            ["applicationSubDomain"] = Organization.ClientId,
            ["organizationName"] = Organization.ClientId,
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new FormUrlEncodedContent(requestBody),
        };

        if (!string.IsNullOrWhiteSpace(newAccountAccessKey))
        {
            Logger.LogInformation("Using newAccountAccessKey ");
            requestMessage.Headers.Add("x-cluedin-newaccountaccesskey", newAccountAccessKey);
        }

        _ = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }

    protected async Task LoginAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = new Uri(serverUris.AuthApiUri, "connect/token");

        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);

        var requestBody = new Dictionary<string, string>
        {
            ["username"] = Organization.UserName,
            ["password"] = Organization.Password,
            ["grant_type"] = "password",
            ["client_id"] = Organization.ClientId,
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new FormUrlEncodedContent(requestBody),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content.DeserializeToAnonymousTypeAsync(new { access_token = "" }).ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        Organization.AccessToken = result.access_token;

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(Organization.AccessToken);
        var userIdClaimValue = token.Payload.Claims.FirstOrDefault(claim => claim.Type == "Id")?.Value ?? throw new InvalidOperationException("Id claim is not found in access token.");
        var organizationIdClaimValue = token.Payload.Claims.FirstOrDefault(claim => claim.Type == "OrganizationId")?.Value ?? throw new InvalidOperationException("OrganizationId claim is not found in access token.");

        if (!Guid.TryParse(userIdClaimValue, out var parsedUserId))
        {
            throw new InvalidOperationException($"Id claim value '{userIdClaimValue}' is not a valid guid.");
        }

        if (!Guid.TryParse(organizationIdClaimValue, out var parsedOrganizationId))
        {
            throw new InvalidOperationException($"OrganizationId claim value '{organizationIdClaimValue}' is not a valid guid.");
        }

        Organization.UserId = parsedUserId;
        Organization.OrganizationId = parsedOrganizationId;
    }

    protected virtual async Task<ServerUriCollection> GetServerUris(CancellationToken cancellationToken)
    {
        return await Environment.GetServerUriCollectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddAuthorizationHeader(HttpRequestMessage requestMessage)
    {
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Organization.AccessToken);
    }

    protected async Task<HttpResponseMessage> SendGraphQlRequestAsync(
        string body,
        CancellationToken cancellationToken,
        bool requireAuthorization = false,
        Action<HttpClient>? configureClient = null,
        bool suppressDebug = false,
        bool throwIfNotSuccessCode = true)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(body, Encoding.UTF8, ApplicationJsonContentType),
        };

        return await SendRequestAsync(
            requestMessage,
            cancellationToken,
            requireAuthorization: requireAuthorization,
            configureClient: configureClient,
            suppressDebug: suppressDebug,
            throwIfNotSuccessCode: throwIfNotSuccessCode)
            .ConfigureAwait(false);
    }

    protected async Task<HttpResponseMessage> SendRequestAsync(
        HttpRequestMessage requestMessage,
        CancellationToken cancellationToken,
        bool requireAuthorization = false,
        Action<HttpClient>? configureClient = null,
        bool suppressDebug = false,
        bool throwIfNotSuccessCode = true)
    {
        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);

        configureClient?.Invoke(client);

        if (requireAuthorization)
        {
            AddAuthorizationHeader(requestMessage);
        }

        if (!suppressDebug && (requestMessage.Content is StringContent || requestMessage.Content is FormUrlEncodedContent))
        {
            var requestContent = await requestMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var headersWithRedactedAuthorization = GetHeaderWithRedactedAuthorization(requestMessage);
            Logger.LogDebug("""
                        Making request to {Uri}
                        {Headers}
                        {Content}
                        """,
                        requestMessage.RequestUri,
                        SerializeHeaders(headersWithRedactedAuthorization),
                        requestContent);
        }
        else
        {
            Logger.LogDebug("Making request to {Uri}.", requestMessage.RequestUri);
        }

        var response = await client.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!suppressDebug)
        {
            Logger.LogDebug("""
                        Got response from request to {Uri}
                        {Headers}
                        {Content}
                        """,
                        requestMessage.RequestUri,
                        SerializeHeaders(response.Headers),
                        content);
        }

        if (!response.IsSuccessStatusCode && throwIfNotSuccessCode)
        {
            throw new InvalidOperationException("Failed to perform request successfully.");
        }
        return response;
    }

    private static string SerializeHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var simplified = headers.ToDictionary(kvp => kvp.Key, kvp => string.Join(",", kvp.Value));
        return JsonSerializer.Serialize(simplified, HeaderSerializerOptions);
    }

    private Dictionary<string, IEnumerable<string>> GetHeaderWithRedactedAuthorization(HttpRequestMessage requestMessage)
    {
        var headersWithRedactedAuthorization = new Dictionary<string, IEnumerable<string>>();
        foreach (var kvp in requestMessage.Headers)
        {
            if (kvp.Key == "Authorization" && kvp.Value.Count() == 1 && kvp.Value.SingleOrDefault() == $"Bearer {Organization?.AccessToken}")
            {
                headersWithRedactedAuthorization.Add("Authorization", ["Bearer [Redacted]"]);
                continue;
            }
            headersWithRedactedAuthorization.Add(kvp.Key, kvp.Value);
        }

        return headersWithRedactedAuthorization;
    }

    protected async Task SubmitSampleClueAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = new Uri(serverUris.PublicApiUri, "api/v1/clue?save=true");

        var replacedBody = RequestTemplates.SubmitSampleClueAsync(
            organizationId: Organization.OrganizationId,
            currentClueUuid: Guid.NewGuid(),
            currentClueCount: 1);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }

    [Obsolete("Use C# 11 Raw string literals (triple quotes)")]
    protected async Task<string> GetRequestTemplateAsync(string requestName)
    {
        var currentType = typeof(ClueSendingOperation<TOptions>);
        var assembly = currentType.Assembly;
        var resourceName = $"{currentType.Namespace}.Data.RequestTemplates.{requestName}.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            var available = string.Join(',', assembly.GetManifestResourceNames());
            throw new InvalidOperationException($"Failed to read manifest resource stream for '{resourceName}'. Available resource streams are '{available}'.");
        }
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    protected static SetupOperation CreateSetupOperation<T1>(T1 t1, Func<T1, CancellationToken, Task> func, Dictionary<string, object>? loggingScopeState = null)
    {
        return new SetupOperation(
            cancellationToken => func(t1, cancellationToken),
            func.Method.Name,
            loggingScopeState);
    }

    protected static SetupOperation CreateSetupOperation<T1, T2>(T1 t1, T2 t2, Func<T1, T2, CancellationToken, Task> func, Dictionary<string, object>? loggingScopeState = null)
    {
        return new SetupOperation(
            cancellationToken => func(t1, t2, cancellationToken),
            func.Method.Name,
            loggingScopeState);
    }

    protected static SetupOperation CreateSetupOperation<T1, T2, T3>(T1 t1, T2 t2, T3 t3, Func<T1, T2, T3, CancellationToken, Task> func, Dictionary<string, object>? loggingScopeState = null)
    {
        return new SetupOperation(
            cancellationToken => func(t1, t2, t3, cancellationToken),
            func.Method.Name,
            loggingScopeState);
    }

    private void PopulateQueueStats(SingleIterationOperationResult result, RabbitMQCompletionResult rabbitMqCompletionResult, CancellationToken cancellationToken)
    {
        foreach (var current in rabbitMqCompletionResult.QueuePollingHistory)
        {
            var history = current.Value.HistoricalQueueInfo;
            var first = history.First();
            var last = history.Last();
            result.TotalMessages.Add(current.Key, new QueueStatistics
            {
                Published = new QueueCountStatistics
                {
                    Before = first.Published.Count,
                    After = last.Published.Count,
                },
                Delivered = new QueueCountStatistics
                {
                    Before = first.Delivered.Count,
                    After = last.Delivered.Count,
                }
            });
        }
    }
    protected record SetupOperation(Func<CancellationToken, Task> Function, string? Name = null, Dictionary<string, object>? LoggingScopeState = null);
}
