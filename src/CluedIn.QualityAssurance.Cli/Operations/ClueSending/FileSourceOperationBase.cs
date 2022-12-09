﻿using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using Spectre.Console;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract class FileSourceOperationBase<TOptions> : ClueSendingOperation<TOptions>
    where TOptions : IClueSendingOperationOptions, IFileSourceOperationOptions
{
    public FileSourceOperationBase(
        ILogger<FileSourceOperationBase<TOptions>> logger,
        IEnvironment testEnvironment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMqCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory)
        : base(logger, testEnvironment, resultWriters, rabbitMqCompletionChecker, postOperationActions, httpClientFactory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected FileSource FileSource { get; set; }

    protected CustomMappingOptions CustomMapping { get; set; }

    private ILogger<FileSourceOperationBase<TOptions>> Logger { get; }

    protected override async Task CreateOperationData(int iterationNumber)
    {
        FileSource = CreateFileSource(CreateTestId(iterationNumber));
        CustomMapping = await GetTestResultCustomizationsAsync(FileSource.UploadFilePath).ConfigureAwait(false);
        await base.CreateOperationData(iterationNumber);
    }

    private async Task<CustomMappingOptions> GetTestResultCustomizationsAsync(string testFilePath)
    {
        var customizationFileStream = TestFileHelper.GetCustomizationFileStream(testFilePath);
        if (customizationFileStream != null)
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

            var customMapping = jsonObj?["customMapping"];

            var requests = customMapping?["requests"]?.AsArray()?.Select(x => new CustomMappingRequest
            {
                Name = x["name"].Deserialize<string>(),
                Request = x["request"].ToString(),
            }).ToList() ?? new List<CustomMappingRequest>();

            var customMappingOptions = new CustomMappingOptions
            {
                ShouldAutoGenerateOriginEntityCodeKey = customMapping?["shouldAutoGenerateOriginEntityCodeKey"]?.AsValue().GetValue<bool?>() ?? true,
                MappingRequests = requests,
            };
        }

        return new CustomMappingOptions();
    }

    protected void AddMappingModifications(List<Func<CancellationToken, Task>> operations)
    {
        if (CustomMapping.ShouldAutoGenerateOriginEntityCodeKey)
        {
            operations.Add(SetAutoGeneratedOriginEntityCodeKeyAsync);
        }

        Logger.LogInformation("Using customization with ShouldAutoGenerateOriginEntityCodeKey {ShouldAutoGenerateOriginEntityCodeKey} and mapping {CustomMapping}",
            CustomMapping.ShouldAutoGenerateOriginEntityCodeKey,
            CustomMapping.MappingRequests.Select(x => x.Name));
        foreach (var current in CustomMapping.MappingRequests)
        {
            operations.Add(cancellationToken => SendCustomMappingRequest(current, cancellationToken));
        }
    }

    protected async Task SendCustomMappingRequest(CustomMappingRequest mappingRequest, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Begin sending custom mapping request '{CustomMappingRequestName}'", mappingRequest.Name);
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = mappingRequest.Request;
        var replacedBody = body
            .Replace("{{AnnotationId}}", FileSource.AnnotationId.ToString())
            .Replace("{{VocabularyKey}}", FileSource.VocabularyKey.ToString())
            .Replace("{{EntityType}}", FileSource.EntityType.ToString())
            .Replace("{{OrganizationId}}", Organization.OrganizationId.ToString())
            .Replace("{{UserId}}", Organization.UserId.ToString())
            .Replace("{{DataSetId}}", FileSource.DataSetId.ToString())
            .Replace("{{DataSourceId}}", FileSource.DataSourceId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };
        _ = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
        Logger.LogDebug("End sending custom mapping request '{CustomMappingRequestName}'", mappingRequest.Name);
    }

    protected async Task CreateDataSourceSetAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateDataSourceSetAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{UserId}}", Organization.UserId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    inbound = new
                    {
                        createDataSourceSet = (int?)null,
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        int? resultDataSourceSetId = result.data?.inbound?.createDataSourceSet ?? throw new InvalidOperationException("DataSourceSetId is not found in result.");

        FileSource.DataSourceSetId = resultDataSourceSetId.Value;
    }

    private FileSource CreateFileSource(string testId)
    {
        return new FileSource
        {
            UploadFilePath = Options.InputFilePath,
            IsExternalUploadFilePath = File.Exists(Options.InputFilePath),
            VocabularyKey = "testEntity" + testId.ToString(),
            EntityType = "testEntity" + testId.ToString(),
        };
    }


    private async Task<int> GetNumberOfCluesAsync(CancellationToken cancellationToken)
    {
        var counter = 0;
        string? line;

        using var streamReader = new StreamReader(TestFileHelper.GetTestFileStream(Options.InputFilePath));
        while ((line = await streamReader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            counter++;
        }

        return counter - 1; //assume header row exists
    }

    protected Stream GetUploadFileStream()
    {
        return TestFileHelper.GetTestFileStream(Options.InputFilePath);
    }
    protected async Task<HttpResponseMessage> GetDataSourceByIdAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(GetDataSourceByIdAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSourceId}}", FileSource.DataSourceId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
        return response;
    }

    protected async Task AutoAnnotateAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(AutoAnnotateAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSetId}}", FileSource.DataSetId.ToString())
            .Replace("{{VocabularyKey}}", FileSource.VocabularyKey)
            .Replace("{{EntityType}}", FileSource.EntityType);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }

    protected async Task GetAnnotationIdAsync(CancellationToken cancellationToken)
    {
        var response = await GetDataSourceByIdAsync(cancellationToken).ConfigureAwait(false);
        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    inbound = new
                    {
                        dataSource = new
                        {
                            dataSets = new[]
                            {
                            new
                            {
                                annotationId = (int?)null
                            },
                            },
                        },
                    },
                },
            }).ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        int? annotationId = result.data?.inbound?.dataSource?.dataSets?[0]?.annotationId ?? throw new InvalidOperationException("AnnotationId is not found in result.");

        FileSource.AnnotationId = annotationId.Value;
    }

    protected async Task SetAutoGeneratedOriginEntityCodeKeyAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(SetAutoGeneratedOriginEntityCodeKeyAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{AnnotationId}}", FileSource.AnnotationId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }

    public class CustomMappingOptions
    {

        public bool ShouldAutoGenerateOriginEntityCodeKey { get; set; } = true;

        public IEnumerable<CustomMappingRequest> MappingRequests { get; set; } = Enumerable.Empty<CustomMappingRequest>();
    }

    public class CustomMappingRequest
    {
        public string Name { get; set; }

        public string Request { get; set; }
    }
}
