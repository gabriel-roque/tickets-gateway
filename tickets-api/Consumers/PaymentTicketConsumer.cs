using System.Text.Json;
using Confluent.Kafka;
using TicketsApi.Enums;
using TicketsApi.Interfaces.Repositories;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;

namespace TicketsApi.Consumers;

public class PaymentTicketConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentTicketConsumer> _logger;
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly List<ConsumeResult<Ignore, string>> _buffer = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const int BULK_SIZE = 1;
    private static readonly TimeSpan TIME_LIMIT = TimeSpan.FromSeconds(2);

    public PaymentTicketConsumer(IServiceProvider serviceProvider, ILogger<PaymentTicketConsumer> logger,
        IConfiguration config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var kafkaUrl = config.GetValue<string>("Kafka:Url") ??
                       Environment.GetEnvironmentVariable("Kafka__Url");

        _logger.LogInformation("PaymentTicketConsumer - Kafka URL: {Url}", kafkaUrl);

        var kafkaConfig = new ConsumerConfig
        {
            BootstrapServers = kafkaUrl,
            GroupId = $"{KafkaTopicsEnum.PaymentTicket}-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            MaxInFlight = BULK_SIZE,
        };

        _consumer = new ConsumerBuilder<Ignore, string>(kafkaConfig).Build();
        _consumer.Subscribe(KafkaTopicsEnum.PaymentTicket);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[PaymentTicketConsumer] Starting Consumer...");
        var nextFlush = DateTimeOffset.UtcNow.Add(TIME_LIMIT);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_buffer.Count >= BULK_SIZE || (_buffer.Count > 0 && DateTimeOffset.UtcNow >= nextFlush))
                {
                    await ProcessBuffer();
                    nextFlush = DateTimeOffset.UtcNow.Add(TIME_LIMIT);
                    continue;
                }


                var consumeResult = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (consumeResult != null)
                    _buffer.Add(consumeResult);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[KAFKA] Consumer is cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KAFKA_ERROR] Payment batch will be retried without advancing offsets.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task ProcessBuffer()
    {
        if (!await _semaphore.WaitAsync(0)) return; // Evita múltiplos processamentos concorrentes
        try
        {
            List<ConsumeResult<Ignore, string>> messagesToProcess;
            if (_buffer.Count == 0) return;
            messagesToProcess = new List<ConsumeResult<Ignore, string>>(_buffer);

            using var scope = _serviceProvider.CreateScope();
            var gatewayApiService = scope.ServiceProvider.GetRequiredService<IGatewayApiService>();

            var tickets = messagesToProcess
                .Select(m => JsonSerializer.Deserialize<Ticket>(m.Message.Value)
                    ?? throw new InvalidOperationException("Payment ticket payload is empty."))
                .GroupBy(ticket => ticket.Id)
                .Select(group => group.Last())
                .ToList();

            var results = await Task.WhenAll(tickets.Select(gatewayApiService.CreateTransactionPix));
            if (results.Any(success => !success))
                throw new InvalidOperationException("At least one payment transaction failed; Kafka offsets were not committed.");

            _consumer.Commit(CommitOffsets(messagesToProcess));
            _buffer.RemoveRange(0, messagesToProcess.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PAYMENT_TICKET_ERROR] Batch retained for retry.");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static IEnumerable<TopicPartitionOffset> CommitOffsets(
        IEnumerable<ConsumeResult<Ignore, string>> messages) => messages
        .GroupBy(message => message.TopicPartition)
        .Select(group => new TopicPartitionOffset(group.Key, group.Max(message => message.Offset) + 1));

}
