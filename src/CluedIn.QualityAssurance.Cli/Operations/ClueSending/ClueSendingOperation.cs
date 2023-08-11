using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using Microsoft.Extensions.Logging;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract class ClueSendingOperation<TOptions> : MultiIterationOperation<TOptions, MultiIterationOperationResult, SingleIterationOperationResult>
    where TOptions : IClueSendingOperationOptions
{
    private static readonly TimeSpan DelayBeforeOperation = TimeSpan.FromSeconds(1);
    public ClueSendingOperation(
        ILogger<ClueSendingOperation<TOptions>> logger,
        IEnvironment environment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMqCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory)
        : base(logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        ResultWriters = resultWriters ?? throw new ArgumentNullException(nameof(resultWriters));
        CompletionChecker = rabbitMqCompletionChecker ?? throw new ArgumentNullException(nameof(rabbitMqCompletionChecker));
        PostOperationActions = postOperationActions ?? throw new ArgumentNullException(nameof(postOperationActions));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }
    private ILogger<ClueSendingOperation<TOptions>> Logger { get; }

    protected Organization? Organization { get; set; }
    protected IEnvironment Environment { get; }

    private IEnumerable<IResultWriter> ResultWriters { get; }

    private IRabbitMQCompletionChecker CompletionChecker { get; }

    private IEnumerable<IPostOperationAction> PostOperationActions { get; }

    private SingleIterationOperationResult? PreIngestionResult { get; set; }
    protected IHttpClientFactory HttpClientFactory { get; }


    protected override async Task SetUpOperationAsync(CancellationToken cancellationToken)
    {
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
                Logger.LogDebug($"Begin writing result using. '{currentWriter.GetType().FullName}'.");
                await currentWriter.ProcessAsync(Options.OutputDirectory, results, cancellationToken).ConfigureAwait(false);
                Logger.LogDebug($"End writing result using. '{currentWriter.GetType().FullName}'.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"Error when writing result using. '{currentWriter.GetType().FullName}'.");
            }
        }
    }

    protected override async Task<SingleIterationOperationResult> ExecuteIterationAsync(int iterationNumber, CancellationToken cancellationToken)
    {
        try
        {
            await SetOrganizationAsync(iterationNumber);
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
            var result = new SingleIterationOperationResult();
            result.HasErrors = true;
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An exception has occurred while trying to perform test run.");
            var result = new SingleIterationOperationResult();
            result.HasErrors = true;
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

        await CompletionChecker.InitializeAsync(cancellationToken).ConfigureAwait(false);
        result.StartTime = DateTimeOffset.UtcNow;
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(Options.TimeoutInMinutes));
        var completionCheckerTask = CompletionChecker.PollForCompletionAsync(cancellationToken);
        var testRunTask = Task.WhenAll(ExecuteIngestionAsync(cancellationToken), completionCheckerTask);

        if (await Task.WhenAny(timeoutTask, testRunTask).ConfigureAwait(false) == timeoutTask)
        {
            result.HasTimedOut = true;
            result.EndTime = DateTimeOffset.UtcNow;
            Logger.LogWarning("Time out waiting for completion.");
        }
        else
        {
            var completionCheckerResult = await completionCheckerTask.ConfigureAwait(false);
            result.EndTime = completionCheckerResult.EndTime;
            result.QueuePollingHistory = completionCheckerResult.QueuePollingHistory;
            Logger.LogInformation("Successfully waited for completion. Test ran from {Start} to {End}.", result.StartTime, result.EndTime);
        }

        PopulateQueueStats(result, await completionCheckerTask.ConfigureAwait(false), cancellationToken);
        await CustomizeResultAsync(result, cancellationToken).ConfigureAwait(false);
        result.MemoryStatistics.After = await Environment.GetAvailableMemoryInMegabytesAsync(cancellationToken).ConfigureAwait(false);

        if (Options.SkipPostOperationActions)
        {
            Logger.LogInformation("Skipping post operation actions because it is set to skipped in options.");
        }
        else
        {
            Logger.LogInformation("Running post operation actions with allowed list {AllowedList}.", Options.AllowedPostOperationActions);
            foreach (var current in PostOperationActions)
            {
                var currentActionName = current.GetType().Name;
                if (Options.AllowedPostOperationActions != null && Options.AllowedPostOperationActions.Any()
                    && !Options.AllowedPostOperationActions.Contains(currentActionName))
                {
                    Logger.LogInformation("Skipping post operation actions {PostOperationActionName} because it's not in allowed list.", currentActionName);
                    continue;
                }

                Logger.LogInformation("Running post operation actions {PostOperationActionName}.", currentActionName);
                await current.ExecuteAsync(result, cancellationToken).ConfigureAwait(false);
            }
        }

        Logger.LogInformation("Finished processing result for {Organization}.", Organization.ClientId);
        return result;
    }

    protected virtual Task CustomizeResultAsync(SingleIterationOperationResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task ExecuteIngestionAsync(CancellationToken cancellationToken);

    private async Task ExecuteSetupOperationsAsync(IEnumerable<SetupOperation> operations, CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            using var scope = operation.LoggingScopeState != null ? this.Logger.BeginScope(operation.LoggingScopeState) : null;
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
        string testId = CreateTestId(iterationNumber);
        var clientId = Options.ClientIdPrefix + testId;
        Organization = new Organization
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
        if (Options.UseShortTestIdPrefix)
        {
            return $"{OverallResult.StartTime.ToString("MMddHHmm")}x{iterationNumber}";
        }
        else
        {
            return $"{OverallResult.StartTime.ToString("yyyyMMddHHmmss")}x{iterationNumber}";
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

    protected async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage requestMessage, CancellationToken cancellationToken, bool requireAuthorization = false, Action<HttpClient> configureClient = null, bool supressDebug = false)
    {
        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);

        if (configureClient != null)
        {
            configureClient(client);
        }

        if (requireAuthorization)
        {
            AddAuthorizationHeader(requestMessage);
        }

        if (!supressDebug && (requestMessage.Content is StringContent || requestMessage.Content is FormUrlEncodedContent))
        {
            var requestContent = await requestMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
            Logger.LogDebug("Making request to {Uri} with {Content}.", requestMessage.RequestUri, requestContent);
        }
        else
        {
            Logger.LogDebug("Making request to {Uri}.", requestMessage.RequestUri);
        }

        var response = await client.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!supressDebug)
        {
            Logger.LogDebug("Got response from request to {Uri} {Content}", requestMessage.RequestUri, content);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Failed to perform request successfully.");
        }
        return response;
    }

    protected async Task SubmitSampleClueAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = new Uri(serverUris.PublicApiUri, "api/v1/clue?save=true");

        var body = await GetRequestTemplateAsync(nameof(SubmitSampleClueAsync)).ConfigureAwait(false);

        var replacedBody = body.Replace("{{OrganizationId}}", Organization.OrganizationId.ToString())
            .Replace("{{CurrentClueUuid}}", Guid.NewGuid().ToString())
            .Replace("{{CurrentClueCount}}", 1.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }


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

    protected SetupOperation CreateSetupOperation<T1>(T1 t1, Func<T1, CancellationToken, Task> func, Dictionary<string, object>? loggingScopeState = null)
    {
        return new SetupOperation(
            cancellationToken => func(t1, cancellationToken),
            func.Method.Name,
            loggingScopeState);
    }
    protected SetupOperation CreateSetupOperation<T1, T2>(T1 t1, T2 t2, Func<T1, T2, CancellationToken, Task> func, Dictionary<string, object>? loggingScopeState = null)
    {
        return new SetupOperation(
            cancellationToken => func(t1, t2, cancellationToken),
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
