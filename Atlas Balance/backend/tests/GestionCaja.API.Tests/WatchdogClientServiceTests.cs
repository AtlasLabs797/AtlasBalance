using System.Net;
using System.Text;
using FluentAssertions;
using GestionCaja.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionCaja.API.Tests;

public class WatchdogClientServiceTests
{
    [Fact]
    public async Task GetEstadoAsync_Should_Parse_CamelCase_Response_When_StateFile_Is_Missing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WatchdogSettings:BaseUrl"] = "http://localhost:5001",
                ["WatchdogSettings:SharedSecret"] = "secret-test",
                ["WatchdogSettings:StateFilePath"] = Path.Combine(Path.GetTempPath(), $"watchdog-state-missing-{Guid.NewGuid():N}.json")
            })
            .Build();

        var client = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"estado\":\"RUNNING\",\"operacion\":\"RESTORE_BACKUP\",\"mensaje\":\"Restaurando\",\"updatedAt\":\"2026-04-15T18:55:00Z\"}",
                    Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("http://localhost:5001")
        };

        var service = new WatchdogClientService(
            new StaticHttpClientFactory(client),
            configuration,
            NullLogger<WatchdogClientService>.Instance);

        var result = await service.GetEstadoAsync(CancellationToken.None);

        result.Estado.Should().Be("RUNNING");
        result.Operacion.Should().Be("RESTORE_BACKUP");
        result.Mensaje.Should().Be("Restaurando");
        result.UpdatedAt.Should().Be(DateTime.Parse("2026-04-15T18:55:00Z").ToUniversalTime());
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
