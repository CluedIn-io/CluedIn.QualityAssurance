using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

internal class RabbitMQCompletionChecker : IRabbitMQCompletionChecker
{
    private static readonly TimeSpan DelayBetweenQueuePolls = TimeSpan.FromSeconds(3);
    private const int TotalSampleSizeForPatterns = 5;

    private ILogger<RabbitMQCompletionChecker> Logger { get; }

    private RabbitMQService RabbitMqService { get; }

    private List<string> ObservedQueueRegexes { get; } = new ()
    {
        @".*Messages.*\.(.*Command).*",
        @"clue_(datasource)_process_(.*)",
    };
    private List<string> CriticalQueueRegexes { get; } = new ()
    {
        @".*\.(IProcessingCommand).*",
    };

    private List<string> ForceIncludeQueueRegexes { get; } = new()
    {
        @"(DeadLetterCommands)",
        @"(EasyNetQ_Default_Error_Queue)",
    };

    private List<QueueChecker> AllQueueCheckers { get; set; } = new ();

    private Dictionary<string, QueueChecker> ObservedQueueCheckers { get; set; } = new ();

    private Dictionary<string, QueueChecker> CriticalQueueCheckers { get; set; } = new ();

    private Dictionary<string, QueueHistory> ForceIncludeQueues { get; set; } = new ();

    public RabbitMQCompletionChecker(ILogger<RabbitMQCompletionChecker> logger, RabbitMQService rabbitMqService)
    {
        RabbitMqService = rabbitMqService ?? throw new ArgumentNullException(nameof(rabbitMqService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AllQueueCheckers = new ();
        ObservedQueueCheckers = new ();
        CriticalQueueCheckers = new ();
        ForceIncludeQueues = new ();
        _ = await PopulateQueueInformation(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RabbitMQCompletionResult> PollForCompletionAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        var lastShowProgressTime = startTime;
        Logger.LogInformation("Start polling for completion.");

        var hasStartedProcessing = false;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("Aborting polling because cancellation is requested.");

                // TODO: Return partial history
                return new(startTime, DateTimeOffset.UtcNow, new());
            }

            var results = await PopulateQueueInformation(cancellationToken).ConfigureAwait(false);
            if (results.Any(results => results.CurrentQueueInfo.Messages.Count != 0))
            {
                hasStartedProcessing = true;
            }

            var utcNow = DateTimeOffset.UtcNow;
            var shouldShowLog = (utcNow - lastShowProgressTime).TotalMinutes > 1;
            var criticalQueueInfos = CriticalQueueCheckers.Select(checker => checker.Value.HistoricalQueueInfo);
            if (!hasStartedProcessing && criticalQueueInfos.All(info => info.Last().Published.Count == info.First().Published.Count))
            {
                if (shouldShowLog)
                {
                    Logger.LogInformation("None of the queues have any messages yet. No messages went through processing queue.");
                }
            }
            else if (results.All(results => results.CurrentQueueInfo.Messages.Count == 0 && results.IsComplete))
            {
                if (shouldShowLog)
                {
                    Logger.LogInformation("All queues message count are zero and marked as completed. Processing is completed.");
                }
                foreach (var currentResult in results)
                {
                    Logger.LogDebug("Queue {QueueName} completed at {EndTime}", currentResult.CurrentQueueInfo.QueueName, currentResult.CompletedInfo?.PolledAt);
                }

                var endTime = results.Where(result => result.CompletedInfo != null).Max(result => result.CompletedInfo.PolledAt);
                var pollingHistoryWithActivity = AllQueueCheckers
                    .Where(checker => checker.HistoricalQueueInfo.Select(info => info.Published).Distinct().Count() > 1);

                var queuePollingHistory = pollingHistoryWithActivity
                    .ToDictionary(checker => checker.QueueName, checker => new QueuePollingHistory(checker.QueueName,  checker.ShortQueueName, checker.HistoricalQueueInfo));

                AddForceIncludeQueues(queuePollingHistory);
                return new(startTime, endTime, queuePollingHistory);
            }

            if (shouldShowLog)
            {
                Logger.LogInformation("Some queues message count are NOT zero or not marked as complete. Processing is NOT YET completed.");
                lastShowProgressTime = utcNow;
            }
            await Task.Delay(DelayBetweenQueuePolls).ConfigureAwait(false);
        }
    }

    private void AddForceIncludeQueues(Dictionary<string, QueuePollingHistory> queuePollingHistory)
    {
        foreach (var queue in ForceIncludeQueues)
        {
            if (queuePollingHistory.ContainsKey(queue.Key))
            {
                continue;
            }

            queuePollingHistory.Add(queue.Key, new QueuePollingHistory(queue.Value.QueueName, queue.Value.ShortQueueName, queue.Value.HistoricalQueueInfo));
        }
    }

    private async Task<List<(QueueInfo CurrentQueueInfo, bool IsComplete, QueueInfo CompletedInfo, List<QueueInfo> HistoricalQueueInfo)>> PopulateQueueInformation(CancellationToken cancellationToken)
    {
        var allQueueInfo = await RabbitMqService.GetRabbitAllQueueInfoAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<(QueueInfo CurrentQueueInfo, bool IsComplete, QueueInfo CompletedInfo, List<QueueInfo> HistoricalQueueInfo)>();
        foreach (var currentQueueInfo in allQueueInfo)
        {
            var queueName = currentQueueInfo.QueueName;

            bool isCritical = false;
            foreach (var currentRegex in CriticalQueueRegexes)
            {
                var match = Regex.Match(queueName, currentRegex);
                if (match.Success)
                {
                    isCritical = true;
                    if (!CriticalQueueCheckers.ContainsKey(queueName))
                    {
                        var shortName = string.Join(' ', match.Groups.Values.Skip(1));
                        var checker = new QueueChecker(Logger, queueName, shortName);
                        CriticalQueueCheckers.Add(queueName, checker);
                        AllQueueCheckers.Add(checker);

                    }

                    var currentResult = CriticalQueueCheckers[queueName].AddCurrentInfo(currentQueueInfo);
                    results.Add(currentResult);
                }
            }

            if (!isCritical)
            {
                foreach (var currentRegex in ObservedQueueRegexes)
                {
                    var match = Regex.Match(queueName, currentRegex);
                    if (match.Success)
                    {
                        if (!ObservedQueueCheckers.ContainsKey(queueName))
                        {
                            var shortName = string.Join(' ', match.Groups.Values.Skip(1));
                            var checker = new QueueChecker(Logger, queueName, shortName);
                            ObservedQueueCheckers.Add(queueName, checker);
                            AllQueueCheckers.Add(checker);
                        }

                        var currentResult = ObservedQueueCheckers[queueName].AddCurrentInfo(currentQueueInfo);
                        results.Add(currentResult);
                    }
                }
            }

            foreach (var currentRegex in ForceIncludeQueueRegexes)
            {
                var match = Regex.Match(queueName, currentRegex);
                if (match.Success)
                {
                    if (!ForceIncludeQueues.ContainsKey(queueName))
                    {
                        var shortName = string.Join(' ', match.Groups.Values.Skip(1));
                        var history = new QueueHistory(Logger, queueName, shortName);
                        ForceIncludeQueues.Add(queueName, history);
                    }

                    ForceIncludeQueues[queueName].AddHistoricalQueueInfo(currentQueueInfo);
                }
            }
        }

        return results;
    }


    private class QueueHistory
    {
        public string QueueName { get; }
        public string ShortQueueName { get; }

        public List<QueueInfo> HistoricalQueueInfo { get; } = new List<QueueInfo>();
        public ILogger<RabbitMQCompletionChecker> Logger { get; }

        public QueueHistory(ILogger<RabbitMQCompletionChecker> logger, string queueName, string shortQueueName)
        {
            if (string.IsNullOrWhiteSpace(queueName))
            {
                throw new ArgumentException($"'{nameof(queueName)}' cannot be null or whitespace.", nameof(queueName));
            }

            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            QueueName = queueName;
            ShortQueueName = string.IsNullOrWhiteSpace(shortQueueName) ? queueName : shortQueueName;
        }

        public virtual void AddHistoricalQueueInfo(QueueInfo currentInfo)
        {
            HistoricalQueueInfo.Add(currentInfo);
        }
    }

    private class QueueChecker : QueueHistory
    {
        private const int totalSamples = TotalSampleSizeForPatterns;

        private Queue<QueueInfo> SampledQueueInfo { get; } = new Queue<QueueInfo>();

        public QueueChecker(ILogger<RabbitMQCompletionChecker> logger, string queueName, string shortQueueName)
            : base (logger, queueName, shortQueueName)
        {
        }

        public (QueueInfo CurrentQueueInfo, bool IsComplete, QueueInfo CompletedInfo, List<QueueInfo> HistoricalQueueInfo) AddCurrentInfo(QueueInfo currentInfo)
        {
            AddHistoricalQueueInfo(currentInfo);
            if (SampledQueueInfo.Count >= totalSamples)
            {
                SampledQueueInfo.Dequeue();
            }

            SampledQueueInfo.Enqueue(currentInfo);

            var isComplete = false;

            var queueItems = SampledQueueInfo.ToList();

            if (SampledQueueInfo.Count == totalSamples)
            {
                var totalUniquePublishedCount = SampledQueueInfo.Select(info => info.Published.Count).Distinct().Count();
                bool hasNewMessages = totalUniquePublishedCount != 1;
                isComplete = !hasNewMessages && SampledQueueInfo.All(info => IsIdleQueue(info));
            }

            Logger.LogDebug(
                "Queue Status, Name: {Name} Count: {Count}  IsComplete {IsComplete}",
                ShortQueueName,
                currentInfo.Messages.Count,
                isComplete);


            QueueInfo? completedInfo = null;
            if (isComplete)
            {
                var maxPublished = HistoricalQueueInfo.Max(info => info.Published.Count);
                var firstMaxPublishIndex = HistoricalQueueInfo.FindIndex(info => IsIdleQueue(info) && info.Published.Count == maxPublished);
                if (firstMaxPublishIndex == -1)
                {
                    completedInfo = HistoricalQueueInfo[1];
                }
                else
                {
                    completedInfo = HistoricalQueueInfo[firstMaxPublishIndex];
                }
            }

            return (currentInfo, isComplete, completedInfo, HistoricalQueueInfo);

            static bool IsIdleQueue(QueueInfo info)
            {
                return info.Messages.Count == 0
                    && info.Messages.Rate == 0
                    && info.Published.Rate == 0
                    && info.Acknowledged.Rate == 0
                    && info.Delivered.Rate == 0
                    && info.Redelivered.Rate == 0;
            }
        }
    }
}
