using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using CluedIn.QualityAssurance.Cli.Operations;
using CluedIn.QualityAssurance.Cli.Services;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;
using Serilog.Core;

namespace CluedIn.QualityAssurance.Cli;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var operations = GetAllOperations();
        int exitCode = 1;

        var result = Parser.Default.ParseArguments(args, operations.Select(o => o.OptionType).ToArray());
        await result.WithParsedAsync(async options =>
        {
            var operationType = operations
                                .Where(x => x.OptionType == options.GetType())
                                .Select(x => x.OperationType)
                                .Single();

            var host = Host.CreateDefaultBuilder(args)
                        .ConfigureServices((hostContext, services) =>
                        {
                            services.AddTransient(operationType);
                            ConfigureServices(services, options);
                        })
                        .UseSerilog((hostBuilder, loggerConfiguration) =>
                        {
                            const string OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}";
                            loggerConfiguration
                                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                                .MinimumLevel.Override("System", LogEventLevel.Warning)
                                .Enrich.FromLogContext()
                                .Enrich.With(new RemovePropertiesEnricher())
                                .WriteTo.Console(outputTemplate: OutputTemplate);

                            var operationOptions = options as IOperationOptions;
                            if (operationOptions != null)
                            {
                                var serilogLevel = operationOptions.LogLevel switch
                                {
                                    LogLevel.Trace => LogEventLevel.Verbose,
                                    LogLevel.Debug => LogEventLevel.Debug,
                                    LogLevel.Information => LogEventLevel.Information,
                                    LogLevel.Warning => LogEventLevel.Warning,
                                    LogLevel.Error => LogEventLevel.Error,
                                    LogLevel.Critical => LogEventLevel.Fatal,
                                    _ => LogEventLevel.Debug,
                                };
                                loggerConfiguration.MinimumLevel.Is(serilogLevel);

                                if (!string.IsNullOrWhiteSpace(operationOptions.LogFilePath))
                                {
                                    loggerConfiguration.WriteTo.File(operationOptions.LogFilePath, outputTemplate: OutputTemplate);
                                }
                            }
                            else
                            {
                                if (!Debugger.IsAttached)
                                {
                                    loggerConfiguration.MinimumLevel.Warning();
                                }
                            }
                        })
                        .Build();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

            var context = new ValidationContext(options);

            var errorResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(options, context, errorResults, true))
            {
                foreach (var validationResult in errorResults)
                {
                    await Console.Error.WriteLineAsync(validationResult.ErrorMessage);
                }

                return;
            }

            try
            {
                logger.LogDebug($"Executing {options.GetType().Name}");

                var operation = host.Services.GetRequiredService(operationType);

                var executeAsyncMethod = operationType.GetMethod(nameof(Operation<IOperationOptions>.ExecuteAsync));
                var executeTask = executeAsyncMethod?.Invoke(operation, new[] { options, lifetime.ApplicationStopping }) as Task;
                if (executeTask is null)
                {
                    throw new InvalidOperationException($"Unable to find method '{nameof(Operation<IOperationOptions>.ExecuteAsync)}'.");
                }
                await executeTask.ConfigureAwait(false);

                exitCode = 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);
            }
        });
        return exitCode;
    }

    public static IEnumerable<Type> GetAllDescendantsOf(
    Assembly assembly,
    Type genericTypeDefinition)
    {
        IEnumerable<Type> GetAllAscendants(Type t)
        {
            var current = t;

            while (current.BaseType != null && current.BaseType != typeof(object))
            {
                yield return current.BaseType;
                current = current.BaseType;
            }
        }

        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        if (genericTypeDefinition == null)
            throw new ArgumentNullException(nameof(genericTypeDefinition));

        if (!genericTypeDefinition.IsGenericTypeDefinition)
            throw new ArgumentException(
                "Specified type is not a valid generic type definition.",
                nameof(genericTypeDefinition));

        return assembly.GetTypes()
                       .Where(t => GetAllAscendants(t).Any(d =>
                           d.IsGenericType &&
                           d.GetGenericTypeDefinition()
                            .Equals(genericTypeDefinition)));
    }

    private static IEnumerable<(Type OperationType, Type OptionType)> GetAllOperations()
    {
        return from t in GetAllDescendantsOf(Assembly.GetExecutingAssembly(), typeof(Operation<>))
               let genericArgType = t.BaseType.GetGenericArguments()[0]
               where genericArgType.GetCustomAttribute<VerbAttribute>() != null
               select (OperationType: t, OptionType: genericArgType);
    }

    private static IServiceCollection ConfigureServices(IServiceCollection services, object options)
    {
        services.AddTransient<EdgeExporter>();

        services = AddClueSendingOperations(services, options);
        return services;
    }

    private static IServiceCollection AddClueSendingOperations(IServiceCollection services, object options)
    {
        if (options is not IClueSendingOperationOptions organizationOptions)
        {
            return services;
        }

        services
            .AddHttpClient()
            .AddHttpClient(Constants.AllowUntrustedSSLClient)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator

            });
        services.AddTransient<IRabbitMQCompletionChecker, RabbitMQCompletionChecker>();
        services.AddTransient<RabbitMQService>();

        AddEnvironmentOptions(services, organizationOptions);
        AddResultWriters(services);
        AddPostOperationActions(services, organizationOptions);
        return services;
    }

    private static void AddEnvironmentOptions(IServiceCollection services, IClueSendingOperationOptions options)
    {

        if (options is IKubernetesEnvironmentOptions kubernetesOptions && kubernetesOptions.IsKubernetesEnvironment)
        {
            services.AddSingleton<IEnvironment, KubernetesEnvironment>();
            services.AddTransient(_ => Options.Create(kubernetesOptions));
        }
        else if(options is ILocalEnvironmentOptions localOptions && localOptions.IsLocalEnvironment)
        {
            services.AddTransient<IEnvironment, LocalEnvironment>();
            services.AddTransient(_ => Options.Create(localOptions));
        }
        else
        {
            throw new NotSupportedException("One of the environment must be set.");
        }
    }

    private static void AddResultWriters(IServiceCollection services)
    {
        services.AddTransient<IResultWriter, ConsoleResultWriter>();
        services.AddTransient<IResultWriter, CsvFileResultWriter>();
        services.AddTransient<IResultWriter, JsonFileResultWriter>();
        services.AddTransient<IResultWriter, HtmlFileResultWriter>();
    }

    private static void AddPostOperationActions(IServiceCollection services, IClueSendingOperationOptions options)
    {
        services.AddTransient(_ => options);
        services.AddTransient<IPostOperationAction, PersistHashAssertion>();

        if (options is IFileSourceOperationOptions fileSourceOperationOptions)
        {
            services.AddTransient(_ => fileSourceOperationOptions);
            services.AddTransient<IPostOperationAction, CustomQueryAction>();
        }
        else if (options is IRawCluesOptions rawCluesOptions)
        {
            services.AddTransient(_ => rawCluesOptions);
            services.AddTransient<IPostOperationAction, CodesAssertionAction>();
            services.AddTransient<IPostOperationAction, CluesSubmissionAssertion>();
        }

        services.AddTransient<IPostOperationAction, EntitiesCountAssertionAction>();
        services.AddTransient<IPostOperationAction, MetricsAssertionAction>();
    }

    /// <summary>
    /// Adapted from: https://stackoverflow.com/a/47191498
    /// </summary>
    class RemovePropertiesEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent le, ILogEventPropertyFactory logEventPropertyFactory)
        {
            le.RemovePropertyIfPresent("SourceContext");
        }
    }
}