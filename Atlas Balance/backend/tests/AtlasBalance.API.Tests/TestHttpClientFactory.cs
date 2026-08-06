using System.Net;
using System.Text;

namespace AtlasBalance.API.Tests;

// HttpClientFactory minimal para tests. Solo se usa en
// IaAcceptanceTests; las clases mas detalladas viven dentro de
// AtlasAiServiceTests como CapturingHttpClientFactory privada.
internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;
    private readonly Exception? _exception;
    private readonly int? _retryAfterSeconds;

    public TestHttpClientFactory(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? responseBody = null,
        Exception? exception = null,
        int? retryAfterSeconds = null)
    {
        _statusCode = statusCode;
        _responseBody = responseBody ??
            "{\"choices\":[{\"message\":{\"content\":\"OK\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5}}";
        _exception = exception;
        _retryAfterSeconds = retryAfterSeconds;
    }

    public HttpClient CreateClient(string name)
    {
        var handler = new TestHandler(_statusCode, _responseBody, _exception, _retryAfterSeconds);
        return new HttpClient(handler)
        {
            BaseAddress = new Uri(name switch
            {
                "openai" => "https://openai.test/v1/",
                "minimax" => "https://minimax.test/v1/",
                _ => "https://openrouter.test/api/v1/"
            })
        };
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly Exception? _exception;
        private readonly int? _retryAfterSeconds;

        public TestHandler(HttpStatusCode statusCode, string responseBody, Exception? exception, int? retryAfterSeconds)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
            _exception = exception;
            _retryAfterSeconds = retryAfterSeconds;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception is not null) throw _exception;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            if (_retryAfterSeconds is > 0)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(_retryAfterSeconds.Value));
            }
            return Task.FromResult(response);
        }
    }
}
