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
    private readonly object _lock = new();
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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"[PaymentTicketConsumer] Starting Consumer...");

        // Rodar em Threads Diferentes
        Task.Run(() => ConsumeKafka(stoppingToken), stoppingToken);
        Task.Run(() => ProcessBufferPeriodically(stoppingToken), stoppingToken);

        return Task.CompletedTask;
    }

    private void ConsumeKafka(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(stoppingToken);
                if (consumeResult != null)
                {
                    lock (_lock)
                    {
                        _buffer.Add(consumeResult);
                    }

                    if (_buffer.Count >= BULK_SIZE)
                    {
                        Task.Run(() => ProcessBuffer());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[KAFKA] Consumer is cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KAFKA_ERROR] {ex.Message}");
            }
        }
    }

    private async Task ProcessBuffer()
    {
        if (!await _semaphore.WaitAsync(0)) return; // Evita múltiplos processamentos concorrentes
        try
        {
            List<ConsumeResult<Ignore, string>> messagesToProcess;
            lock (_lock)
            {
                // Evita que multiplas Threads manipule o mesmo Buffer
                if (_buffer.Count == 0) return;
                messagesToProcess = new List<ConsumeResult<Ignore, string>>(_buffer);
                _buffer.Clear();
            }

            using var scope = _serviceProvider.CreateScope();
            IGatewayApiService? gatewayApiService = scope.ServiceProvider.GetService<IGatewayApiService>();

                var tickets = messagesToProcess
                    .Select(m => JsonSerializer.Deserialize<Ticket>(m.Message.Value))
                    .ToList();


                Parallel.ForEach(tickets, new ParallelOptions()
                {
                    MaxDegreeOfParallelism = 4
                }, async void (ticket) =>
                {
                    try
                    {
                        await gatewayApiService.CreateTransactionPix(ticket);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError($"[GATEWAY_API_ERROR] {e.Message}");
                    }
                });

                _consumer.Commit(messagesToProcess.Select(m => m.TopicPartitionOffset));
        }
        catch (Exception ex)
        {
            _logger.LogError($"[PAYMENT_TICKET_ERROR] {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task ProcessBufferPeriodically(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TIME_LIMIT, stoppingToken);
            await ProcessBuffer();
        }
    }
}