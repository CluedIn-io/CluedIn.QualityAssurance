using System.Text;
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
using System.Text.RegularExpressions;
using CluedIn.Core;
using CluedIn.Core.Data.Vocabularies;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract class FileSourceOperationBase<TOptions> : ClueSendingOperation<TOptions>
    where TOptions : IClueSendingOperationOptions, IFileSourceOperationOptions
{
    protected const string ApplicationJsonContentType = "application/json";
    private const int MaximumKeyPrefixLength = 50;
    private const int MaximumVocabularyCreationPoll = 10;
    private static readonly TimeSpan DelayAfterVocabularyCreationPoll = TimeSpan.FromSeconds(1);
    private static readonly Regex InvalidEntityTypeNameRegex = new Regex(@"[^a-zA-Z0-9]");
    private static readonly Regex InvalidVocabularyNameRegex = new Regex(@"[^a-zA-Z0-9\.]");

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

    protected ICollection<FileSource> FileSources { get; set; }

    protected string EntityTypePrefix { get; set; }

    private ILogger<FileSourceOperationBase<TOptions>> Logger { get; }

    protected override async Task CreateOperationData(int iterationNumber)
    {
        CreateFileSources(CreateTestId(iterationNumber));
        await base.CreateOperationData(iterationNumber);
    }

    private async Task<(CustomMappingOptions MappingOptions, string json)> GetTestResultCustomizationsAsync(FileSource fileSource)
    {
        var customizationFileStream = TestFileHelper.GetCustomizationFileStream(fileSource.UploadFilePath);
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

            //var vocabularies = customMapping?["vocabularies"]?.AsArray()?.Select(x => new CustomVocabulary
            //{
            //    Name = x["name"].Deserialize<string>(),
            //}).ToList() ?? new List<CustomVocabulary>();

            return (new CustomMappingOptions
            {
                ShouldAutoGenerateOriginEntityCodeKey = customMapping?["shouldAutoGenerateOriginEntityCodeKey"]?.AsValue().GetValue<bool?>() ?? true,
                ShouldAutoMap = customMapping?["shouldAutoMap"]?.AsValue().GetValue<bool?>() ?? true,
                EntityType = customMapping?["entityType"]?.AsValue().GetValue<string?>(),
                VocabularyName = customMapping?["vocabularyName"]?.AsValue().GetValue<string?>(),
                MappingRequests = requests,
                //Vocabularies = vocabularies,
            }, json);
        }

        return (new CustomMappingOptions(), null);
    }


    protected async Task AddMappingOperationsAsync(List<SetupOperation> operations, FileSource fileSource, CancellationToken cancellationToken)
    {
        using var scope = CreateLoggingScope(fileSource);
        var (customMapping, customizationFileBody) = await GetTestResultCustomizationsAsync(fileSource).ConfigureAwait(false);
        if (customMapping.EntityType != null) 
        {
            var mappedType = SanitizeForEntityType(customMapping.EntityType);
            Logger.LogInformation("Using custom EntityType {EntityType}, mapped to {MappedEntityType}.", customMapping.EntityType, mappedType);
            fileSource.EntityType = mappedType;
        }
        if (customMapping.VocabularyName != null)
        {
            var mappedType = SanitizeForVocabularyName(customMapping.VocabularyName);
            Logger.LogInformation("Using custom VocabularyName {VocabularyName}, mapped to {MappedVocabularyName}.", customMapping.VocabularyName, mappedType);
            fileSource.VocabularyName = mappedType;
        }

        AddMappingCreationOperation(operations, fileSource, customMapping);

        operations.Add(CreateSetupOperation(fileSource, GetAnnotationIdAsync));

        if (customMapping.ShouldAutoGenerateOriginEntityCodeKey)
        {
            operations.Add(CreateSetupOperation(fileSource, "--auto-generated--", SetOriginEntityCodeKeyAsync));
        }

        if (customMapping.MappingRequests.Any())
        {
            var entityTypeRegex = new Regex(@"{{EntityType\[([a-zA-Z0-9]+)\]}}");
            var foundEntityTypes = entityTypeRegex.Matches(customizationFileBody)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();

            if (foundEntityTypes.Any())
            {
                Logger.LogInformation("Found additional EntityType(s) {EntityTypes}.", string.Join(",", foundEntityTypes));
                foreach (var currentEntityType in foundEntityTypes)
                {
                    var mappedType = SanitizeForEntityType(currentEntityType);
                    Logger.LogInformation("Using additional entityType {CustomEntityType}, mapped to {MappedCustomEntityType}.", currentEntityType, mappedType);
                    operations.Add(CreateSetupOperation(mappedType, CreateEntityTypeIfNotExistsAsync, CreateLoggingScopeState(fileSource)));
                    fileSource.CustomEntityTypesMapping.Add(currentEntityType, mappedType);
                }
            }


            var vocabularyRegex = new Regex(@"{{Vocabulary\[([a-zA-Z0-9\.]+)\].(Id|Name)}}");
            var vocabularyKeyRegex = new Regex(@"{{VocabularyKey\[([a-zA-Z0-9\.]+)\].(Id|Name)}}");

            var foundVocabularies = vocabularyRegex.Matches(customizationFileBody)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();
            var foundVocabularyKeys = vocabularyKeyRegex.Matches(customizationFileBody)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();

            // Should probably be a dictionary
            var vocabulariesToCreate = foundVocabularies.Select(vocabulary => new CustomVocabulary
            {
                Name = vocabulary,
            }).ToList();

            if (foundVocabularies.Any())
            {
                Logger.LogInformation("Found additional Vocabulary {Vocabularies}.", string.Join(",", foundVocabularies));
            }

            if (foundVocabularyKeys.Any())
            {
                Logger.LogInformation("Found additional VocabularyKey {VocabularyKeys}.", string.Join(",", foundVocabularyKeys));
                foreach (var currentKey in foundVocabularyKeys)
                {
                    var currentKeyParts = currentKey.Split('.');
                    var vocabularyName = string.Join(".", currentKeyParts.SkipLast(1));
                    if (!vocabulariesToCreate.Any(x => x.Name == vocabularyName))
                    {
                        vocabulariesToCreate.Add(new CustomVocabulary
                        {
                            Name = vocabularyName,
                        });

                    }

                    var vocab = vocabulariesToCreate.Single(x => x.Name == vocabularyName);
                    if (!vocab.Keys.Any(key => key.Name == currentKey))
                    {
                        vocab.Keys.Add(new CustomVocabularyKey { Name = currentKeyParts.Last() });
                    }
                }
            }

            Logger.LogInformation("Using custom vocabularies {CustomVocabularies} and its keys.", vocabulariesToCreate.Select(x => x.Name));
            foreach (var current in vocabulariesToCreate)
            {
                var loggingScopeState = CreateLoggingScopeState(fileSource);
                loggingScopeState.Add("CustomVocabularyName", current.Name);
                operations.Add(CreateSetupOperation(fileSource, current, CreateCustomVocabularyAsync, loggingScopeState));
            }
        }

        Logger.LogInformation("Using custom mapping {CustomMapping}.", customMapping.MappingRequests.Select(x => x.Name));
        foreach (var current in customMapping.MappingRequests)
        {
            var loggingScopeState = CreateLoggingScopeState(fileSource);
            loggingScopeState.Add("MappingRequestName", current.Name);
            operations.Add(CreateSetupOperation(fileSource, current, SendCustomMappingRequestAsync, loggingScopeState));
        }
    }

    private void AddMappingCreationOperation(List<SetupOperation> operations, FileSource fileSource, FileSourceOperationBase<TOptions>.CustomMappingOptions customMapping)
    {
        operations.Add(CreateSetupOperation(fileSource, CreateEntityTypeIfNotExistsAsync));
        operations.Add(CreateSetupOperation(fileSource, CreateVocabularyIfNotExistsAsync));
        Logger.LogInformation("Using AutoMapping {ShouldAutoMap}.", customMapping.ShouldAutoMap);
        if (customMapping.ShouldAutoMap)
        {
            operations.Add(CreateSetupOperation(fileSource, CreateAutoAnnotationAsync));
        }
        else
        {
            operations.Add(CreateSetupOperation(fileSource, CreateManualAnnotationAsync));
        }
    }

    private IDisposable? CreateLoggingScope(FileSource fileSource)
    {
        return this.Logger.BeginScope(CreateLoggingScopeState(fileSource));
    }

    protected SetupOperation CreateSetupOperation(FileSource fileSource, Func<FileSource, CancellationToken, Task> func)
    {
        return CreateSetupOperation(fileSource, func, CreateLoggingScopeState(fileSource));
    }

    private static Dictionary<string, object> CreateLoggingScopeState(FileSource fileSource)
    {
        return new Dictionary<string, object>
        {
            ["File"] = Path.GetFileName(fileSource.UploadFilePath),
        };
    }

    protected override Task CustomizeResultAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        result.Output["FileSources"] = FileSources;
        return Task.CompletedTask;
    }

    protected async Task SendCustomMappingRequestAsync(
        FileSource fileSource,
        CustomMappingRequest mappingRequest,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Begin sending custom mapping request '{CustomMappingRequestName}'", mappingRequest.Name);
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = mappingRequest.Request;
        string replacedBody = ReplaceParameters(fileSource, body);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
        await CheckResponse(response).ConfigureAwait(false);

        Logger.LogDebug("End sending custom mapping request '{CustomMappingRequestName}'", mappingRequest.Name);
    }

    private string ReplaceParameters(FileSource fileSource, string body)
    {
        var replacedBody = body
            .Replace("{{AnnotationId}}", fileSource.AnnotationId.ToString())
            .Replace("{{VocabularyName}}", fileSource.VocabularyName.ToString())
            .Replace("{{VocabularyId}}", fileSource.VocabularyId.ToString())
            .Replace("{{EntityType}}", fileSource.EntityType.ToString())
            .Replace("{{OrganizationId}}", Organization.OrganizationId.ToString())
            .Replace("{{UserId}}", Organization.UserId.ToString())
            .Replace("{{DataSetId}}", fileSource.DataSetId.ToString())
            .Replace("{{DataSourceId}}", fileSource.DataSourceId.ToString());

        foreach (var currentMapping in fileSource.CustomEntityTypesMapping)
        {
            replacedBody = replacedBody.Replace($"{{{{EntityType[{currentMapping.Key}]}}}}", currentMapping.Value);
        }

        foreach (var currentMapping in fileSource.CustomVocabulariesMapping)
        {
            replacedBody = replacedBody.Replace($"{{{{Vocabulary[{currentMapping.Key}].Id}}}}", currentMapping.Value.Id.ToString());
            replacedBody = replacedBody.Replace($"{{{{Vocabulary[{currentMapping.Key}].Name}}}}", currentMapping.Value.Name);
            foreach (var currentKeyMapping in currentMapping.Value.KeysMapping)
            {
                replacedBody = replacedBody.Replace($"{{{{VocabularyKey[{currentMapping.Key}.{currentKeyMapping.Key}].Id}}}}", currentKeyMapping.Value.Id.ToString());
                replacedBody = replacedBody.Replace($"{{{{VocabularyKey[{currentMapping.Key}.{currentKeyMapping.Key}].Name}}}}", currentKeyMapping.Value.Name);
            }
        }

        return replacedBody;
    }

    protected async Task CreateEntityTypeIfNotExistsAsync(string entityType, CancellationToken cancellationToken)
    {
        var entityId = await GetEntityTypeInfoAsync(entityType, cancellationToken).ConfigureAwait(false);

        if (entityId != null)
        {
            Logger.LogInformation("Skiping creation of Entity Type {EntityType} because it exists.", entityType);
            return;
        }

        Logger.LogInformation("Creating Entity Type {EntityType} because it does not exist.", entityType);
        await CreateEntityTypeAsync(entityType, cancellationToken).ConfigureAwait(false);
    }


    protected async Task<Guid?> GetEntityTypeInfoAsync(string entityType, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(GetEntityTypeInfoAsync)).ConfigureAwait(false);
        var replacedBody = body
            .Replace("{{EntityType}}", entityType);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    management = new
                    {
                        getEntityTypeInfo = new
                        {
                            id = (Guid?)null,
                            type = (string?)null,
                            route = (string?)null,
                            icon = (string?)null,
                        }
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        return result.data?.management?.getEntityTypeInfo?.id;
    }

    protected Task CreateEntityTypeIfNotExistsAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        return CreateEntityTypeIfNotExistsAsync(fileSource.EntityType, cancellationToken);
    }

    protected async Task CreateEntityTypeAsync(string entityType, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateEntityTypeAsync)).ConfigureAwait(false);
        var replacedBody = body
            .Replace("{{EntityType}}", entityType)
            .Replace("{{EntityTypeRoute}}", entityType.ToLower());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    management = new
                    {
                        createEntityTypeConfigurationV2 = new
                        {
                            type = (string?)null,
                            route = (string?)null,
                            icon = (string?)null,
                        }
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        var type = result.data?.management?.createEntityTypeConfigurationV2?.type ?? throw new InvalidOperationException("Entity type not found in result.");
    }

    protected async Task CreateVocabularyIfNotExistsAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var vocabularyId = await CreateVocabularyIfNotExistsAsync(fileSource.EntityType, fileSource.VocabularyName, cancellationToken);
        fileSource.VocabularyId = vocabularyId;
    }

    protected async Task CreateCustomVocabularyAsync(FileSource fileSource, CustomVocabulary vocabulary, CancellationToken cancellationToken)
    {
        var mappedName = SanitizeForVocabularyName(vocabulary.Name);
        Logger.LogInformation("Mapping CustomVocabularyName {CustomVocabularyName} to {MappedCustomVocabularyName}.", vocabulary.Name, mappedName);
        using var vocabularyScope = Logger.BeginScope(new Dictionary<string, object>
        {
            ["MappedCustomVocabularyName"] = mappedName,
        });
        // TODO: allow setting of custom entity type instead of just fileSource.EntityType
        var vocabularyId = await CreateVocabularyIfNotExistsAsync(mappedName, fileSource.EntityType, cancellationToken);


        var mapping = new CustomVocabularyMappingEntry
        {
            Name = mappedName,
            Id = vocabularyId,
        };


        var keys = await GetVocabularyKeysFromVocabularyIdAsync(vocabularyId, cancellationToken).ConfigureAwait(false);

        
        foreach (var vocabularyKey in vocabulary.Keys)
        {
            using var vocabularyKeyScope = Logger.BeginScope(new Dictionary<string, object>
            {
                ["CustomVocabularyKeyName"] = vocabularyKey.Name,
            });
            if (keys.Any(key => key.Name == vocabularyKey.Name))
            {
                Logger.LogInformation("Skiping creation of VocabularyKeyName {VocabularyKeyName} because it exists.", vocabularyKey.Name);
                continue;
            }

            Logger.LogInformation("Creating VocabularyKeyName {VocabularyKeyName} because it does not exist.", vocabularyKey.Name);
            await Task.Delay(TimeSpan.FromMilliseconds(Options.DelayAfterVocabularyKeyCreationInMilliseconds));

            var keyId = Guid.Empty;
            try
            {
                keyId = await CreateVocabularyKeyAsync(vocabularyId, vocabularyKey, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                var result = await PollForVocabularyKeyCreationCompletionAsync(vocabularyId, vocabularyKey.Name, cancellationToken).ConfigureAwait(false);
                keyId = result.VocabularyKeyId;
            }
            mapping.KeysMapping.Add(vocabularyKey.Name, new CustomVocabularyKeyMappingEntry
            {
                Name = vocabularyKey.Name,
                Id = keyId,
            });
        }
        fileSource.CustomVocabulariesMapping.Add(vocabulary.Name, mapping);
    }

    protected async Task<(Guid VocabularyKeyId, List<(Guid KeyId, string Name)> AllKeys)> PollForVocabularyKeyCreationCompletionAsync(Guid vocabularyId, string keyName, CancellationToken cancellationToken)
    {
        // Server has issues if we create multiple vocabularies in quick succession,
        // Sometimes it says vocabulary does not exist, when it does,
        // We need to ensure that it exists first before processing

        for (int i = 0; i < MaximumVocabularyCreationPoll; ++i)
        {
            Logger.LogInformation("Waiting for {DelayAfterVocabularyCreation} before checking whether vocabulary key {VocabularyKeyName} exists.",
                DelayAfterVocabularyCreationPoll,
                keyName);
            await Task.Delay(DelayAfterVocabularyCreationPoll).ConfigureAwait(false);
            try
            {
                var keys = await GetVocabularyKeysFromVocabularyIdAsync(vocabularyId, cancellationToken).ConfigureAwait(false);

                var foundKey = keys.SingleOrDefault(key => key.Name == keyName);
                if (foundKey != default)
                {
                    Logger.LogInformation("Finish polling for {VocabularyKeyName}.", keyName);
                    return (foundKey.KeyId, keys);
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogWarning(ex, "Failed to poll for {VocabularyKeyName}.", keyName);
            }
        }

        throw new InvalidOperationException($"Failed to ensure that vocabulary key {keyName} exists.");
    }

    protected async Task<Guid> PollForVocabularyCreationCompletionAsync(string vocabularyName, CancellationToken cancellationToken)
    {
        // Server has issues if we create multiple vocabularies in quick succession,
        // Sometimes it says vocabulary does not exist, when it does,
        // We need to ensure that it exists first before processing

        for (int i = 0; i < MaximumVocabularyCreationPoll; ++i)
        {
            Logger.LogInformation("Waiting for {DelayAfterVocabularyCreation} before checking whether vocabulary {VocabularyName} exists.",
                DelayAfterVocabularyCreationPoll,
                vocabularyName);
            await Task.Delay(DelayAfterVocabularyCreationPoll).ConfigureAwait(false);
            try
            {
                var vocabularyId = await GetVocabularyIdFromName(vocabularyName, cancellationToken).ConfigureAwait(false);

                if (vocabularyId != null)
                {
                    Logger.LogInformation("Finish polling for {VocabularyName}.", vocabularyName);
                    return vocabularyId.Value;
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogWarning(ex, "Failed to poll for {VocabularyName}.", vocabularyName);
            }
        }

        throw new InvalidOperationException($"Failed to ensure that vocabulary {vocabularyName} exists.");
    }

    protected async Task<Guid> CreateVocabularyIfNotExistsAsync(string vocabularyName, string entityType, CancellationToken cancellationToken)
    {
        var vocabularyId = await GetVocabularyIdFromName(vocabularyName, cancellationToken).ConfigureAwait(false);

        if (vocabularyId != null)
        {
            Logger.LogInformation("Skiping creation of VocabularyName {VocabularyName} because it exists.", vocabularyName);
            return vocabularyId.Value;
        }

        Logger.LogInformation("Creating VocabularyName {VocabularyName} because it does not exist.", vocabularyName);
        return await CreateVocabularyAsync(vocabularyName, entityType, cancellationToken).ConfigureAwait(false);
    }

    protected async Task<Guid?> GetVocabularyIdFromName(string vocabularyName, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync("GetAllVocabulariesAsync").ConfigureAwait(false);
        var replacedBody = body
            .Replace("{{VocabularyName}}", vocabularyName);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    management = new
                    {
                        vocabularies = new
                        {
                            data = new[]
                            {
                                new
                                {
                                    vocabularyId = (Guid?)null,
                                    vocabularyName = (string?)null,
                                    keyPrefix = (string?)null,
                                }
                            }
                        }
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        return result.data?.management?.vocabularies?.data?.SingleOrDefault(vocabulary => vocabulary.vocabularyName == vocabularyName)?.vocabularyId;
    }

    protected async Task<Guid> CreateVocabularyAsync(string vocabularyName, string entityType, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateVocabularyAsync)).ConfigureAwait(false);
        var replacedBody = body
            .Replace("{{VocabularyName}}", vocabularyName)
            .Replace("{{EntityType}}", entityType);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    management = new
                    {
                        createVocabulary = new 
                        {
                            vocabularyId = (Guid?)null,
                        }
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        return await PollForVocabularyCreationCompletionAsync(vocabularyName, cancellationToken).ConfigureAwait(false);
    }

    protected async Task<List<(Guid KeyId, string Name)>> GetVocabularyKeysFromVocabularyIdAsync(Guid vocabularyId, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync("GetVocabularyKeysFromVocabularyIdAsync").ConfigureAwait(false);
        var replacedBody = body
            .Replace("{{VocabularyId}}", vocabularyId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    management = new
                    {
                        vocabularyKeysFromVocabularyId = new
                        {
                            data = new[]
                            {
                                new
                                {
                                    vocabularyKeyId = (Guid?)null,
                                    name = (string?)null,
                                    key = (string?)null,
                                    dataType = (string?)null,
                                    groupName = (string?)null,
                                }
                            }
                        }
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        return result.data?.management?.vocabularyKeysFromVocabularyId?.data?.Select(key => (key.vocabularyKeyId.Value, key.name!))?.ToList() ?? new ();
    }

    protected async Task<Guid> CreateVocabularyKeyAsync(Guid vocabularyId, CustomVocabularyKey vocabularyKey, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateVocabularyKeyAsync)).ConfigureAwait(false);
        var replacedBody = body
            .Replace("{{VocabularyId}}", vocabularyId.ToString())
            .Replace("{{VocabularyKeyName}}", vocabularyKey.Name)
            .Replace("{{VocabularyKeyType}}", "Text") // TODO: allow setting of type
            .Replace("{{VocabularyKeyGroup}}", "Metadata");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    management = new
                    {
                        createVocabularyKey = new
                        {
                            vocabularyKeyId = (Guid?)null,
                        }
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        var keyId = result.data?.management?.createVocabularyKey?.vocabularyKeyId ?? throw new InvalidOperationException("VocabularyKeyId is not found in result.");
        return keyId;
    }

    protected async Task CreateDataSourceSetAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateDataSourceSetAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{UserId}}", Organization.UserId.ToString())
            .Replace("{{DataSourceSetName}}", Path.GetFileNameWithoutExtension(fileSource.UploadFilePath));

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
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

        fileSource.DataSourceSetId = resultDataSourceSetId.Value;
    }

    private void CreateFileSources(string testId)
    {
        IEnumerable<string> files = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(Options.InputDirectoryPath))
        {
            if (!Directory.Exists(Options.InputDirectoryPath))
            {
                throw new InvalidOperationException($"Input directory '{Options.InputDirectoryPath}' does not exist.");
            }
            files = Directory.GetFiles(Options.InputDirectoryPath)
                .Where(file => !file.EndsWith(".customization.json"));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Options.InputFilePath))
            {
                throw new InvalidOperationException($"Input file '{Options.InputFilePath}' is invalid.");
            }
            files = new string[]
            {
                Options.InputFilePath
            };
        }

        if (!files.Any())
        {
            throw new InvalidOperationException("No input files found.");
        }

        this.Logger.LogInformation("There are {TotalFiles} files to be processed.", files.Count());
        EntityTypePrefix = $"testX{testId}";
        FileSources = files.Select((file, fileIndex) =>
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            string entityType = SanitizeForEntityType($"{fileIndex}x{fileNameWithoutExtension}");

            return new FileSource
            {
                UploadFilePath = file,
                IsExternalUploadFilePath = TestFileHelper.IsExternalTestFile(file),
                VocabularyName = entityType,
                EntityType = entityType,
            };
        }).ToList();
    }

    private string SanitizeForEntityType(string suffix)
    {
        var sanitizedSuffix = InvalidEntityTypeNameRegex.Replace(suffix, string.Empty);
        var entityType = $"{EntityTypePrefix}x{sanitizedSuffix}";
        if (entityType.Length > MaximumKeyPrefixLength)
        {
            entityType = entityType.Substring(0, MaximumKeyPrefixLength);
        }

        return entityType;
    }

    private string SanitizeForVocabularyName(string suffix)
    {
        var sanitizedSuffix = InvalidVocabularyNameRegex.Replace(suffix, string.Empty);
        var entityType = $"{EntityTypePrefix}x{sanitizedSuffix}";
        if (entityType.Length > MaximumKeyPrefixLength)
        {
            entityType = entityType.Substring(0, MaximumKeyPrefixLength);
        }

        return entityType;
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

    protected Stream GetUploadFileStream(FileSource fileSource)
    {
        return TestFileHelper.GetTestFileStream(fileSource.UploadFilePath);
    }

    protected async Task<HttpResponseMessage> GetDataSourceByIdAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(GetDataSourceByIdAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSourceId}}", fileSource.DataSourceId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
        return response;
    }

    protected async Task CreateAutoAnnotationAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateAutoAnnotationAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSetId}}", fileSource.DataSetId.ToString())
            .Replace("{{VocabularyName}}", fileSource.VocabularyName)
            .Replace("{{VocabularyId}}", fileSource.VocabularyId.ToString())
            .Replace("{{EntityType}}", fileSource.EntityType);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
        await CheckResponse(response).ConfigureAwait(false); 
        _ = await PollForVocabularyCreationCompletionAsync(fileSource.VocabularyName, cancellationToken).ConfigureAwait(false);
    }

    protected async Task CreateManualAnnotationAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateManualAnnotationAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSetId}}", fileSource.DataSetId.ToString())
            .Replace("{{VocabularyName}}", fileSource.VocabularyName)
            .Replace("{{VocabularyId}}", fileSource.VocabularyId.ToString())
            .Replace("{{EntityType}}", fileSource.EntityType);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
        await CheckResponse(response).ConfigureAwait(false);
    }

    private static async Task CheckResponse(HttpResponseMessage response)
    {
        var result = await response.Content
                    .DeserializeToAnonymousTypeAsync(new
                    {
                        errors = (GraphQLError[]?)null,
                    }).ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        CheckForErrors(result.errors);
    }

    private static void CheckForErrors(GraphQLError[] errors)
    {
        if (errors != null && errors.Any())
        {
            throw new InvalidOperationException($"Failed to perform operation because '{string.Join(',', errors.Select(error => error.Message))}'.");
        }
    }

    protected async Task GetAnnotationIdAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var response = await GetDataSourceByIdAsync(fileSource, cancellationToken).ConfigureAwait(false);
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

        fileSource.AnnotationId = annotationId.Value;
    }

    protected async Task SetOriginEntityCodeKeyAsync(FileSource fileSource, string origin, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(SetOriginEntityCodeKeyAsync)).ConfigureAwait(false);
        var replacedBody = body
            .Replace("{{AnnotationId}}", fileSource.AnnotationId.ToString())
            .Replace("{{Origin}}", origin);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }

    public class CustomMappingOptions
    {

        public bool ShouldAutoGenerateOriginEntityCodeKey { get; set; } = true;
        public bool ShouldAutoMap { get; set; } = true;
        public string? EntityType { get; set; }
        public string? VocabularyName { get; set; }

        //public IEnumerable<CustomVocabulary> Vocabularies { get; set; } = Enumerable.Empty<CustomVocabulary>();

        public IEnumerable<CustomMappingRequest> MappingRequests { get; set; } = Enumerable.Empty<CustomMappingRequest>();
    }

    public class CustomMappingRequest
    {
        public string Name { get; set; }

        public string Request { get; set; }
    }

    public class CustomVocabulary
    {
        public string Name { get; set; }

        public List<CustomVocabularyKey> Keys { get; set; } = new List<CustomVocabularyKey>();
    }


    public class CustomVocabularyKey
    {
        public string Name { get; set; }
    }

    public class GraphQLError
    {
        public string Message { get; set; }
    }
}
