using System.Text.Json;
using Confluent.Kafka;
using TicketsApi.Enums;
using TicketsApi.Interfaces.Repositories;
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

    private const int BULK_SIZE = 200;
    private static readonly TimeSpan TIME_LIMIT = TimeSpan.FromSeconds(10);

    public PaymentTicketConsumer(IServiceProvider serviceProvider, ILogger<PaymentTicketConsumer> logger, IConfiguration config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var kafkaUrl = config.GetValue<string>("Kafka:Url");

        var kafkaConfig = new ConsumerConfig
        {
            BootstrapServers = kafkaUrl,
            GroupId = $"{KafkaTopicsEnum.PaymentTicket}-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            MaxInFlight = 10,
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
                if (_buffer.Count == 0) return;
                messagesToProcess = new List<ConsumeResult<Ignore, string>>(_buffer);
                _buffer.Clear();
            }

            using var scope = _serviceProvider.CreateScope();
            ITicketRepository? ticketRepository = scope.ServiceProvider.GetService<ITicketRepository>();

            if (ticketRepository is not null)
            {
                var tickets = messagesToProcess.Select(m => JsonSerializer.Deserialize<Ticket>(m.Message.Value)).ToList();
                await ticketRepository.BulkCreate(tickets);
                _logger.LogInformation($"[BULK_INSERT] {tickets.Count} tickets inserted!");

                _consumer.Commit(messagesToProcess.Select(m => m.TopicPartitionOffset));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[BULK_INSERT_ERROR] {ex.Message}");
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
