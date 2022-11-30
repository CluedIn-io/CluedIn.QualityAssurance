using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using CluedIn.QualityAssurance.Cli.Operations;
using CluedIn.QualityAssurance.Cli.Services;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning);

                if (Debugger.IsAttached)
                    builder.AddConsole();
            })
            .AddTransient<EdgeExporter>()
            .BuildServiceProvider();

        var logger = serviceProvider.GetService<ILogger<Program>>();

        var operations = from t in Assembly.GetExecutingAssembly().GetTypes()
                         where t.BaseType != null && t.BaseType.IsGenericType
                         let genericType = t.BaseType.GetGenericTypeDefinition()
                         where genericType == typeof(Operation<>)
                         let genericArgType = t.BaseType.GetGenericArguments()[0]
                         where genericArgType.GetCustomAttribute<VerbAttribute>() != null
                         select (OperationType: t, OptionType: genericArgType);

        int exitCode = 1;

        var result = Parser.Default.ParseArguments(args, operations.Select(o => o.OptionType).ToArray());
        await result.WithParsedAsync(async o =>
        {
            var context = new ValidationContext(o);

            var errorResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(o, context, errorResults, true))
            {
                foreach (var validationResult in errorResults)
                {
                    await Console.Error.WriteLineAsync(validationResult.ErrorMessage);
                }

                return;
            }

            var source = new CancellationTokenSource(); // TODO connect to ctrl-c / sigterm

            try
            {
                logger.LogDebug($"Executing {o.GetType().Name}");

                var operationType = operations.Where(x => x.OptionType == o.GetType()).Select(x => x.OperationType).Single();

                var operation = ActivatorUtilities.CreateInstance(serviceProvider, operationType);

                var executeAsyncMethod = operationType.GetMethod("ExecuteAsync");
                await (Task)executeAsyncMethod.Invoke(operation, new[] { o, source.Token });

                exitCode = 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);
            }
        });

        return exitCode;
    }
}
