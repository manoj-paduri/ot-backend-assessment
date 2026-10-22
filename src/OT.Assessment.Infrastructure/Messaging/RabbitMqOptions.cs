namespace OT.Assessment.Infrastructure.Messaging;

/// <summary>Connection and topology settings for RabbitMQ (section <c>RabbitMq</c>).</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Name shown in the RabbitMQ management UI for this process' connection.</summary>
    public string ClientName { get; set; } = "ot-assessment";

    public string QueueName { get; set; } = "ot.assessment.wagers";
    public string DeadLetterExchangeName { get; set; } = "ot.assessment.wagers.dlx";
    public string DeadLetterQueueName { get; set; } = "ot.assessment.wagers.dead";

    /// <summary>Maximum number of wagers the API publishes concurrently (one pooled channel each).</summary>
    public int PublisherChannelPoolSize { get; set; } = 16;
}
