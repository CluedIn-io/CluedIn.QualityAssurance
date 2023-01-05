using CluedIn.QualityAssurance.Cli.Models.Operations;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace CluedIn.QualityAssurance.Cli.Services.ResultWriters;

internal class ConsoleResultWriter : IResultWriter
{
    public ConsoleResultWriter(ILogger<ConsoleResultWriter> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<ConsoleResultWriter> Logger { get; }

    public virtual Task ProcessAsync(string outputDirectoryPath, MultiIterationOperationResult result, CancellationToken cancellationToken)
    {
        try
        {
            var results = result.IterationResults.ToList();
            var table = new Table();

            table.AddColumn("No.");
            table.AddColumn(new TableColumn("Start Time").RightAligned());
            table.AddColumn(new TableColumn("End Time").RightAligned());
            table.AddColumn(new TableColumn("Timed Out").Centered());
            table.AddColumn(new TableColumn("Time (s)").RightAligned());
            table.AddColumn(new TableColumn("Avail Memory (MB)").RightAligned());

            var visibleQueueNames = results
                .SelectMany(result => result.QueuePollingHistory.Select(history => history.Value.QueueName))
                .Distinct()
                .Where(queueName => results.All(result => result.QueuePollingHistory.ContainsKey(queueName)))
                .ToList();

            var isAllQueueColumnsVisible = results.All(result => result.QueuePollingHistory.Keys.All(key => visibleQueueNames.Contains(key)));

            foreach (var currentQueue in visibleQueueNames)
            {
                var shortQueueName = results.First().QueuePollingHistory[currentQueue].QueueShortName;
                table.AddColumn(new TableColumn($"# {shortQueueName}").RightAligned());
            }

            var visibleOutputKeys = results
                .SelectMany(result => result.Output.Select(output => output.Key))
                .Distinct()
                .Where(key => results.All(result => result.Output.ContainsKey(key) && result.Output[key].GetType().IsSimpleType()))
                .ToList();

            foreach (var currentCustomColumn in visibleOutputKeys)
            {
                table.AddColumn(new TableColumn(currentCustomColumn).RightAligned());
            }

            var isAllOutputColumnsVisible = results.All(result => result.Output.Keys.All(key => visibleOutputKeys.Contains(key)));

            var isAllColumnsVisible = isAllQueueColumnsVisible && isAllOutputColumnsVisible;

            if (!isAllColumnsVisible)
            {
                table.AddColumn(new TableColumn("Hidden").RightAligned());
            }

            var rowNumber = 1;
            foreach (var currentResult in results)
            {
                var totalAllTimeInSeconds = currentResult.EndTime == null ? 0 : (currentResult.EndTime.Value - currentResult.StartTime).TotalSeconds;

                var columns = new List<string>
                {
                    rowNumber.ToString(),
                    TimeOnly.FromDateTime(currentResult.StartTime.LocalDateTime).ToString("HH:mm:ss"),
                    currentResult.EndTime.HasValue ? TimeOnly.FromDateTime(currentResult.EndTime.Value.LocalDateTime).ToString("HH:mm:ss") : string.Empty,
                    currentResult.HasTimedOut.ToString(),
                    totalAllTimeInSeconds.ToString("0.00"),
                    currentResult.MemoryStatistics.After.ToString() + " (" + currentResult.MemoryStatistics.Difference.ToString("+#;-#;0") + ")",
                };

                foreach (var currentQueue in visibleQueueNames)
                {
                    var value = currentResult.TotalMessages.ContainsKey(currentQueue) ? currentResult.TotalMessages[currentQueue] : null;
                    var published = value == null ? 0 : value.Published.Difference;
                    var delivered = value == null ? 0 : value.Delivered.Difference;

                    columns.Add($"{published} ({delivered})");
                }

                foreach (var currentOutputKey in visibleOutputKeys)
                {
                    columns.Add(currentResult.Output[currentOutputKey].ToString());
                }

                if (!isAllColumnsVisible)
                {
                    var totalQueuesHidden = currentResult.QueuePollingHistory.Count - visibleQueueNames.Count;
                    var totalOutputHidden = currentResult.Output.Count - visibleOutputKeys.Count;
                    var totalHidden = totalQueuesHidden + totalOutputHidden;
                    columns.Add(totalHidden.ToString());
                }

                table.AddRow(columns.ToArray());
                rowNumber++;
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error trying to print result table.");
        }

        return Task.CompletedTask;
    }
}
