using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    public sealed class MesApiClient : IMesApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
        private readonly HttpClient _httpClient;
        private readonly IMesClientLog _log;
        private readonly object _configurationGate = new object();
        private MesConnectionOptions _options;
        private string _accessToken = "";

        public MesConnectionOptions Options
        {
            get { lock (_configurationGate) return _options.Snapshot(); }
        }

        public MesApiClient(MesConnectionOptions options, string accessToken, IMesClientLog log = null,
            HttpMessageHandler handler = null)
        {
            _httpClient = handler == null ? new HttpClient() : new HttpClient(handler, true);
            _log = log;
            try
            {
                Configure(options, accessToken);
            }
            catch
            {
                _httpClient.Dispose();
                throw;
            }
        }

        public void Configure(MesConnectionOptions options, string accessToken)
        {
            var snapshot = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
            var token = accessToken?.Trim() ?? "";
            var uri = new Uri(snapshot.BaseUrl, UriKind.Absolute);
            if (!string.IsNullOrWhiteSpace(token) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
                throw new ArgumentException("远程 MES Bearer 连接必须使用 HTTPS。", nameof(options));
            lock (_configurationGate)
            {
                _options = snapshot;
                _accessToken = token;
            }
        }

        public Task<MesResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
            SendAsync<T>(HttpMethod.Get, path, null, "", cancellationToken);

        public Task<MesResult<T>> PostAsync<T>(string path, object request, string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            SendAsync<T>(HttpMethod.Post, path, request, idempotencyKey, cancellationToken);

        private async Task<MesResult<T>> SendAsync<T>(HttpMethod method, string path, object request,
            string idempotencyKey, CancellationToken cancellationToken)
        {
            MesConnectionOptions options;
            string accessToken;
            lock (_configurationGate)
            {
                options = _options.Snapshot();
                accessToken = _accessToken;
            }
            var correlationId = Guid.NewGuid().ToString("N");
            var attempts = Math.Max(1, options.MaxRetries + 1);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                try
                {
                    using var message = new HttpRequestMessage(method,
                        new Uri(new Uri(options.BaseUrl + "/", UriKind.Absolute), NormalizePath(path)));
                    message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
                    if (!string.IsNullOrWhiteSpace(accessToken))
                        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    if (!string.IsNullOrWhiteSpace(idempotencyKey))
                        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                    if (request != null)
                        message.Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");

                    _log?.Info($"MES {method.Method} {SafePath(path)} correlationId={correlationId} attempt={attempt}");
                    using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                        .ConfigureAwait(false);
                    var responseCorrelation = response.Headers.TryGetValues("X-Correlation-ID", out var values)
                        ? string.Join("", values) : correlationId;
                    var body = response.StatusCode == HttpStatusCode.NoContent
                        ? "" : await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var value = string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);
                        return MesResult<T>.Success(value, responseCorrelation, (int)response.StatusCode);
                    }

                    var error = ParseError(body, responseCorrelation, response.StatusCode);
                    _log?.Warn($"MES {method.Method} {SafePath(path)} status={(int)response.StatusCode} code={error.Code} correlationId={responseCorrelation}");
                    if (attempt < attempts && CanRetry(method, idempotencyKey) && IsTransient(response.StatusCode))
                    {
                        await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return MesResult<T>.Failure(error, (int)response.StatusCode);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt < attempts && CanRetry(method, idempotencyKey))
                    {
                        await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return ConnectionFailure<T>("MES_TIMEOUT", "MES 请求超时。", correlationId);
                }
                catch (HttpRequestException)
                {
                    if (attempt < attempts && CanRetry(method, idempotencyKey))
                    {
                        await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return ConnectionFailure<T>("MES_UNAVAILABLE", "MES 服务当前不可达。", correlationId);
                }
            }
            return ConnectionFailure<T>("MES_UNAVAILABLE", "MES 服务当前不可达。", correlationId);
        }

        private static string NormalizePath(string path) => (path ?? "").TrimStart('/');

        internal static string SafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "/";
            var query = path.IndexOf('?');
            return query < 0 ? path : path.Substring(0, query) + "?<redacted>";
        }

        private static bool IsTransient(HttpStatusCode statusCode) => statusCode == HttpStatusCode.RequestTimeout ||
            statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

        private static bool CanRetry(HttpMethod method, string idempotencyKey) =>
            method == HttpMethod.Get || !string.IsNullOrWhiteSpace(idempotencyKey);

        private static Task DelayAsync(int attempt, CancellationToken cancellationToken) =>
            Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);

        private static MesApiError ParseError(string body, string correlationId, HttpStatusCode statusCode)
        {
            try
            {
                var error = JsonSerializer.Deserialize<MesApiError>(body, JsonOptions);
                if (error != null)
                {
                    if (string.IsNullOrWhiteSpace(error.CorrelationId)) error.CorrelationId = correlationId;
                    return error;
                }
            }
            catch (JsonException) { }
            return new MesApiError
            {
                Code = "MES_HTTP_ERROR",
                Message = $"MES 服务返回 HTTP {(int)statusCode}。",
                CorrelationId = correlationId,
                Retryable = IsTransient(statusCode)
            };
        }

        private static MesResult<T> ConnectionFailure<T>(string code, string message, string correlationId) =>
            MesResult<T>.Failure(new MesApiError
            {
                Code = code,
                Message = message,
                CorrelationId = correlationId,
                Retryable = true
            });

        public void Dispose() => _httpClient.Dispose();
    }
}
