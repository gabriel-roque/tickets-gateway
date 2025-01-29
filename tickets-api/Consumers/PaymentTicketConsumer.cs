using Confluent.Kafka;

namespace TicketsApi.Consumers;
public class PaymentTicketConsumer : BackgroundService
{
    private readonly ILogger<PaymentTicketConsumer> _logger;
    private readonly IConsumer<Ignore, string> _consumer;
    
    public PaymentTicketConsumer(ILogger<PaymentTicketConsumer> logger)
    {
        _logger = logger;
        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092",
            GroupId = "payment-ticket-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        _consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        _consumer.Subscribe("payment-ticket");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentTicketConsumer iniciado.");
        
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(stoppingToken);
                if (consumeResult != null)
                {
                    _logger.LogInformation($"Mensagem recebida: {consumeResult.Value}");
                    // Aqui você pode processar a mensagem
                    _consumer.Commit(consumeResult);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Cancelamento solicitado. Encerrando consumidor.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao consumir mensagem: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
        }
    }

    public override void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
        base.Dispose();
    }
}

