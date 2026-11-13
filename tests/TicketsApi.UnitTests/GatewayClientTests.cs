using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketsApi.AppConfig;
using TicketsApi.Interfaces.Services;
using TicketsApi.Services;
using Xunit;

namespace TicketsApi.UnitTests;

public sealed class GatewayClientTests
{
    [Fact]
    public void Client_is_created_by_http_client_factory_with_bounded_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GatewayApi:Url"] = "http://legacy-gateway:3000",
            ["GatewayApi:XApiKey"] = "test-key"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplicationServicesDI();
        using var provider = services.BuildServiceProvider();

        var first = Assert.IsType<GatewayApiService>(provider.GetRequiredService<IGatewayApiService>());
        var second = Assert.IsType<GatewayApiService>(provider.GetRequiredService<IGatewayApiService>());

        Assert.NotSame(first.ConfiguredClient, second.ConfiguredClient);
        Assert.Equal(new Uri("http://legacy-gateway:3000"), first.ConfiguredClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(10), first.ConfiguredClient.Timeout);
        Assert.Equal("test-key", first.ConfiguredClient.DefaultRequestHeaders.GetValues("x-api-key").Single());
    }
}
