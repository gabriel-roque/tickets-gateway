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
    private readonly List<Ticket> _buffer = new();
    private readonly object _lock = new();

    private const int BULK_SIZE = 100;
    private static readonly TimeSpan TIME_LIMIT = TimeSpan.FromSeconds(10);

    public PaymentTicketConsumer(
        IServiceProvider serviceProvider,
        ILogger<PaymentTicketConsumer> logger, 
        IConfiguration config
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var kafkaUrl = config.GetValue<string>("Kafka:Url");
        
        var kafkaConfig = new ConsumerConfig
        {
            BootstrapServers = kafkaUrl,
            GroupId = $"{KafkaTopicsEnum.PaymentTicket}-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            MaxInFlight = 10,
        };
        _consumer = new ConsumerBuilder<Ignore, string>(kafkaConfig).Build();
        _consumer.Subscribe(KafkaTopicsEnum.PaymentTicket);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerName = "[PaymentTicketConsumer]";
        _logger.LogInformation($"Starting Consumer {consumerName}");

        _ = ProcessBufferPeriodically(stoppingToken); // Inicia a tarefa para processar buffer periodicamente

        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); // Pequeno delay para garantir inicialização correta

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(stoppingToken);
                if (consumeResult != null)
                {
                    var ticket = JsonSerializer.Deserialize<Ticket>(consumeResult.Message.Value);
                    _logger.LogInformation($"{consumerName} - [CONSUMER]: {ticket}");

                    if (ticket is not null)
                    {
                        lock (_lock)
                        {
                            _buffer.Add(ticket);
                        }

                        if (_buffer.Count >= BULK_SIZE)
                        {
                            await ProcessBuffer();
                        }
                    }

                    _consumer.Commit(consumeResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{consumerName} - [ERROR_MESSAGE]: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1), stoppingToken);
        }
    }

    private async Task ProcessBuffer()
    {
        List<Ticket> ticketsToInsert;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;

            ticketsToInsert = new List<Ticket>(_buffer);
            _buffer.Clear();
        }

        using var scope = _serviceProvider.CreateScope();
        ITicketRepository? ticketRepository = scope.ServiceProvider.GetService<ITicketRepository>();

        if (ticketRepository is not null)
        {
            try
            {
                await ticketRepository.BulkCreate(ticketsToInsert);
                _logger.LogInformation($"[BULK INSERT] {ticketsToInsert.Count} tickets inseridos no banco!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[BULK INSERT ERROR] {ex.Message}");
            }
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
