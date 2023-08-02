using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Environments;

namespace CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

internal class RabbitMQService
{
    public RabbitMQService(ILogger<RabbitMQService> logger, IHttpClientFactory httpClientFactory, IEnvironment testEnvironment)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        Environment = testEnvironment ?? throw new ArgumentNullException(nameof(testEnvironment));
    }

    private ILogger<RabbitMQService> Logger { get; }
    private IHttpClientFactory HttpClientFactory { get; }
    private IEnvironment Environment { get; }

    private Task<RabbitMQConnectionInfo> GetRabbitMqConnectionInfoAsync(CancellationToken cancellationToken)
    {
        return Environment.GetRabbitMqConnectionInfoAsync(cancellationToken);
    }

    public async Task PurgeQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        var connectionInfo = await GetRabbitMqConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("Purging queue '{QueueName}'.", queueName);
        var uri = new Uri(connectionInfo.ManagementUri, $"api/queues/%2F/{queueName}/contents");
        var payload = new
        {
            vhost = "/",
            name = queueName,
            mode = "purge",
        };
        var request = new HttpRequestMessage(HttpMethod.Delete, uri)
        {
            Content = JsonContent.Create(payload),
        };

        var authenticationString = $"{connectionInfo.UserName}:{connectionInfo.Password}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authenticationString));
        request.Headers.Add("Authorization", "Basic " + base64EncodedAuthenticationString);

        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<QueueInfo> GetRabbitQueueInfoAsync(string queueName, CancellationToken cancellationToken)
    {
        var connectionInfo = await GetRabbitMqConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        var uri = new Uri(connectionInfo.ManagementUri, $"api/queues/%2F/{queueName}");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var authenticationString = $"{connectionInfo.UserName}:{connectionInfo.Password}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authenticationString));
        request.Headers.Add("Authorization", "Basic " + base64EncodedAuthenticationString);

        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.DeserializeToAnonymousTypeAsync(new
        {
            message_stats = new
            {
                ack = (uint?)null,
                ack_details = new
                {
                    rate = (double?)null,
                },
                deliver_no_ack = (uint?)null,
                deliver_no_ack_details = new
                {
                    rate = (double?)null,
                },
                get = (uint?)null,
                get_details = new
                {
                    rate = (double?)null,
                },
                get_empty = (uint?)null,
                get_empty_details = new
                {
                    rate = (double?)null,
                },
                get_no_ack = (uint?)null,
                get_no_ack_details = new
                {
                    rate = (double?)null,
                },
                deliver = (uint?)null,
                deliver_details = new
                {
                    rate = (double?)null,
                },
                deliver_get = (uint?)null,
                deliver_get_details = new
                {
                    rate = (double?)null,
                },
                publish = (uint?)null,
                publish_details = new
                {
                    rate = (double?)null,
                },
                redeliver = (uint?)null,
                redeliver_details = new
                {
                    rate = (double?)null,
                },

            },
            messages = (uint?)null,
            messages_details = new
            {
                rate = (double?)null,
            },
        }).ConfigureAwait(false);

        // message_stats will be null when there is no activity
        uint ackCount = result?.message_stats?.ack ?? 0;
        double ackRate = result?.message_stats?.ack_details?.rate ?? 0.0f;
        uint deliverCount = result?.message_stats?.deliver ?? 0;
        double deliverRate = result?.message_stats?.deliver_details?.rate ?? 0.0f;
        uint deliverGetCount = result?.message_stats?.deliver_get ?? 0;
        double deliverGetRate = result?.message_stats?.deliver_get_details?.rate ?? 0.0f;
        uint deliverNoAckCount = result?.message_stats?.deliver_no_ack ?? 0;
        double deliverNoAckRate = result?.message_stats?.deliver_no_ack_details?.rate ?? 0.0f;

        uint getCount = result?.message_stats?.get ?? 0;
        double getRate = result?.message_stats?.get_details?.rate ?? 0.0f;
        uint getEmptyCount = result?.message_stats?.get_empty ?? 0;
        double getEmptyRate = result?.message_stats?.get_empty_details?.rate ?? 0.0f;
        uint getNoAckCount = result?.message_stats?.get_no_ack ?? 0;
        double getNoAckRate = result?.message_stats?.get_no_ack_details?.rate ?? 0.0f;

        uint reDeliverGetCount = result?.message_stats?.redeliver ?? 0;
        double reDeliverGetRate = result?.message_stats?.redeliver_details?.rate ?? 0.0f;
        uint publishCount = result?.message_stats?.publish ?? 0;
        double publishRate = result?.message_stats?.publish_details?.rate ?? 0.0f;
        uint messageCount = result?.messages ?? 0;
        double messageCountRate = result?.message_stats?.publish_details?.rate ?? 0.0f;
        return new(
            QueueName: queueName,
            Acknowledged: new(ackCount, ackRate),
            Delivered: new(deliverCount, deliverRate),
            DeliveredOrGet: new(deliverGetCount, deliverGetRate),
            DeliveredNoAck: new(deliverNoAckCount, deliverNoAckRate),
            Get: new(getCount, getRate),
            GetEmpty: new(getEmptyCount, getEmptyRate),
            GetNoAck: new(getNoAckCount, getNoAckRate),
            Redelivered: new(reDeliverGetCount, reDeliverGetRate),
            Published: new(publishCount, publishRate),
            Messages: new(messageCount, messageCountRate),
            DateTimeOffset.UtcNow); ;
    }

    public async Task<ICollection<QueueInfo>> GetRabbitAllQueueInfoAsync(CancellationToken cancellationToken)
    {
        var connectionInfo = await GetRabbitMqConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        var uri = new Uri(connectionInfo.ManagementUri, $"api/queues/%2F");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var authenticationString = $"{connectionInfo.UserName}:{connectionInfo.Password}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authenticationString));
        request.Headers.Add("Authorization", "Basic " + base64EncodedAuthenticationString);

        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.DeserializeToAnonymousTypeAsync(new[]
        {
            new
            {
                name = string.Empty,
                message_stats = new
                {
                    ack = (uint?)null,
                    ack_details = new
                    {
                        rate = (double?)null,
                    },
                    deliver_no_ack = (uint?)null,
                    deliver_no_ack_details = new
                    {
                        rate = (double?)null,
                    },
                    get = (uint?)null,
                    get_details = new
                    {
                        rate = (double?)null,
                    },
                    get_empty = (uint?)null,
                    get_empty_details = new
                    {
                        rate = (double?)null,
                    },
                    get_no_ack = (uint?)null,
                    get_no_ack_details = new
                    {
                        rate = (double?)null,
                    },
                    deliver = (uint?)null,
                    deliver_details = new
                    {
                        rate = (double?)null,
                    },
                    deliver_get = (uint?)null,
                    deliver_get_details = new
                    {
                        rate = (double?)null,
                    },
                    publish = (uint?)null,
                    publish_details = new
                    {
                        rate = (double?)null,
                    },
                    redeliver = (uint?)null,
                    redeliver_details = new
                    {
                        rate = (double?)null,
                    },

                },
                messages = (uint?)null,
                messages_details = new
                {
                    rate = (double?)null,
                },
            }
        }).ConfigureAwait(false);

        return result.Select(current =>
        {
            // message_stats will be null when there is no activity
            uint ackCount = current?.message_stats?.ack ?? 0;
            double ackRate = current?.message_stats?.ack_details?.rate ?? 0.0f;
            uint deliverCount = current?.message_stats?.deliver ?? 0;
            double deliverRate = current?.message_stats?.deliver_details?.rate ?? 0.0f;
            uint deliverGetCount = current?.message_stats?.deliver_get ?? 0;
            double deliverGetRate = current?.message_stats?.deliver_get_details?.rate ?? 0.0f;
            uint deliverNoAckCount = current?.message_stats?.deliver_no_ack ?? 0;
            double deliverNoAckRate = current?.message_stats?.deliver_no_ack_details?.rate ?? 0.0f;

            uint getCount = current?.message_stats?.get ?? 0;
            double getRate = current?.message_stats?.get_details?.rate ?? 0.0f;
            uint getEmptyCount = current?.message_stats?.get_empty ?? 0;
            double getEmptyRate = current?.message_stats?.get_empty_details?.rate ?? 0.0f;
            uint getNoAckCount = current?.message_stats?.get_no_ack ?? 0;
            double getNoAckRate = current?.message_stats?.get_no_ack_details?.rate ?? 0.0f;

            uint reDeliverGetCount = current?.message_stats?.redeliver ?? 0;
            double reDeliverGetRate = current?.message_stats?.redeliver_details?.rate ?? 0.0f;
            uint publishCount = current?.message_stats?.publish ?? 0;
            double publishRate = current?.message_stats?.publish_details?.rate ?? 0.0f;
            uint messageCount = current?.messages ?? 0;
            double messageCountRate = current?.messages_details?.rate ?? 0.0f;
            return new QueueInfo(
                QueueName: current.name,
                Acknowledged: new(ackCount, ackRate),
                Delivered: new(deliverCount, deliverRate),
                DeliveredOrGet: new(deliverGetCount, deliverGetRate),
                DeliveredNoAck: new(deliverNoAckCount, deliverNoAckRate),
                Get: new(getCount, getRate),
                GetEmpty: new(getEmptyCount, getEmptyRate),
                GetNoAck: new(getNoAckCount, getNoAckRate),
                Redelivered: new(reDeliverGetCount, reDeliverGetRate),
                Published: new(publishCount, publishRate),
                Messages: new(messageCount, messageCountRate),
                DateTimeOffset.UtcNow);
        })
        .ToList();
    }
}
