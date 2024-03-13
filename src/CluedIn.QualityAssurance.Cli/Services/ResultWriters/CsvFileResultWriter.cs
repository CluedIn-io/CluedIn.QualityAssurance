using System.Globalization;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CsvHelper;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Services.ResultWriters;

internal class CsvFileResultWriter : IResultWriter
{
    public CsvFileResultWriter(ILogger<CsvFileResultWriter> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<CsvFileResultWriter> Logger { get; }

    public virtual async Task ProcessAsync(string outputDirectoryPath, MultiIterationOperationResult result, CancellationToken cancellationToken)
    {
        try
        {
            var results = result.IterationResults.ToList();
            var records = results.Select((current, i) =>
            {
                var totalAllTimeInSeconds = current.EndTime == null ? 0 : (current.EndTime.Value - current.StartTime).TotalSeconds;
                var dictionary = new Dictionary<string, object>
                {
                    ["No"] = i + 1,
                    ["StartTime"] = current.StartTime,
                    ["EndTimeAll"] = current.EndTime,
                    ["HasTimedOut"] = current.HasTimedOut,
                    ["TotalSecondsAll"] = totalAllTimeInSeconds,
                    ["MemoryBeforeInMegabytes"] = current.MemoryStatistics.Before,
                    ["MemoryAfterInMegabytes"] = current.MemoryStatistics.After,
                };

                foreach (var currentCustomColumn in current.Output)
                {
                    if (currentCustomColumn.Value.GetType().IsSimpleType())
                    {
                        dictionary.Add(currentCustomColumn.Key, currentCustomColumn.Value);
                    }
                }

                foreach (var currentQueue in current.QueuePollingHistory)
                {
                    var currentQueueName = currentQueue.Key;
                    var value = current.TotalMessages.ContainsKey(currentQueueName) ? current.TotalMessages[currentQueueName] : null;
                    var published = value == null ? 0 : value.Published.Difference;
                    var delivered = value == null ? 0 : value.Delivered.Difference;
                    dictionary.Add(currentQueue.Value.QueueShortName + "-Published", published);
                    dictionary.Add(currentQueue.Value.QueueShortName + "-Delivered", delivered);
                }
                return dictionary;
            });

            var allKeys = records.SelectMany(record => record.Keys).ToHashSet();
            var outputFilePath = Path.Combine(outputDirectoryPath, "results.csv");
            using var writer = new StreamWriter(outputFilePath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            foreach (var currentHeader in allKeys)
            {
                csv.WriteField(currentHeader);
            }

            await csv.NextRecordAsync().ConfigureAwait(false);

            foreach (var currentRecord in records)
            {
                foreach (var currentKey in allKeys)
                {
                    var value = currentRecord.ContainsKey(currentKey) ? currentRecord[currentKey] : string.Empty;
                    csv.WriteField(value);
                }
                await csv.NextRecordAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error trying to save csv result.");
        }

    }
}
