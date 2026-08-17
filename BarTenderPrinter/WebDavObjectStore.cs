using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BarTenderPrinter
{
    public sealed class WebDavObjectStore : ICloudObjectStore
    {
        private static readonly HttpMethod MkColMethod = new HttpMethod("MKCOL");
        private static readonly HttpMethod PropFindMethod = new HttpMethod("PROPFIND");
        private readonly Uri _baseUri;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsClient;

        public WebDavObjectStore(Uri baseUri, string userName, string applicationPassword, TimeSpan? timeout = null)
        {
            ValidateBaseUri(baseUri);
            if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("WebDAV 账号不能为空。", nameof(userName));
            if (string.IsNullOrWhiteSpace(applicationPassword)) throw new ArgumentException("WebDAV 应用密码不能为空。", nameof(applicationPassword));
            _baseUri = EnsureTrailingSlash(baseUri);
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            _httpClient = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
            var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes(userName + ":" + applicationPassword));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
            _ownsClient = true;
        }

        internal WebDavObjectStore(Uri baseUri, HttpClient httpClient)
        {
            ValidateBaseUri(baseUri);
            _baseUri = EnsureTrailingSlash(baseUri);
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task EnsureCollectionAsync(string path, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(MkColMethod, BuildUri(path, true));
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.MethodNotAllowed) return;
            await EnsureSuccessAsync(response).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CloudObjectMetadata>> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(PropFindMethod, BuildUri(path, true));
            request.Headers.TryAddWithoutValidation("Depth", "1");
            request.Content = new StringContent("<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getetag/><d:getcontentlength/><d:getlastmodified/><d:resourcetype/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, HttpStatusCode.MultiStatus).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = XDocument.Load(stream, LoadOptions.None);
            XNamespace dav = "DAV:";
            var requestedUri = BuildUri(path, true);
            return document.Descendants(dav + "response")
                .Select(element => ParseMetadata(element, dav))
                .Where(metadata => metadata != null && !UrisIdentifySameObject(BuildUri(metadata.Path, metadata.IsCollection), requestedUri))
                .ToList();
        }

        public async Task<CloudObject> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, false));
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return new CloudObject
            {
                Content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
                Metadata = MetadataFromHeaders(path, response)
            };
        }

        public async Task<CloudObjectMetadata> HeadAsync(string path, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, BuildUri(path, false));
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return MetadataFromHeaders(path, response);
        }

        public async Task<CloudObjectMetadata> PutAsync(string path, byte[] content, string ifMatch = null, bool createOnly = false, CancellationToken cancellationToken = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (createOnly && !string.IsNullOrEmpty(ifMatch)) throw new ArgumentException("创建条件与 ETag 更新条件不能同时使用。", nameof(ifMatch));
            using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(path, false));
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            if (createOnly) request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            if (!string.IsNullOrWhiteSpace(ifMatch)) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            return MetadataFromHeaders(path, response);
        }

        public void Dispose()
        {
            if (_ownsClient) _httpClient.Dispose();
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new WebDavException(SyncErrorCodes.NetworkUnavailable, "WebDAV 请求超时，本地同步队列已保留。");
            }
            catch (HttpRequestException ex)
            {
                throw new WebDavException(SyncErrorCodes.NetworkUnavailable, "当前无法连接 WebDAV，本地同步队列已保留。", innerException: ex);
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, HttpStatusCode additionalSuccess = 0)
        {
            if (response.IsSuccessStatusCode || response.StatusCode == additionalSuccess) return;
            if (response.StatusCode == HttpStatusCode.PreconditionFailed) throw new WebDavPreconditionFailedException(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new WebDavNotFoundException(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                throw new WebDavException(SyncErrorCodes.WebDavAuthenticationFailed, "WebDAV 身份验证失败，请重新检查或导入连接配置。", response.StatusCode);
            if ((int)response.StatusCode == 429)
                throw new WebDavException(SyncErrorCodes.WebDavRateLimited, "WebDAV 请求频率受限，请稍后重试。", response.StatusCode, GetRetryAfter(response));
            if (response.StatusCode == HttpStatusCode.InsufficientStorage || response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                throw new WebDavException(SyncErrorCodes.StorageQuota, "WebDAV 存储空间或单文件额度不足。", response.StatusCode);
            await Task.CompletedTask.ConfigureAwait(false);
            throw new WebDavException(SyncErrorCodes.NetworkUnavailable, "WebDAV 请求失败，本地同步队列已保留。", response.StatusCode);
        }

        private Uri BuildUri(string path, bool collection)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var normalized = path.Replace('\\', '/').Trim('/');
            if (normalized.Length == 0) return _baseUri;
            var segments = normalized.Split('/');
            if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".." || segment.IndexOfAny(new[] { '\r', '\n', '\0', '?', '#' }) >= 0))
                throw new ArgumentException("WebDAV 对象路径无效。", nameof(path));
            var escaped = string.Join("/", segments.Select(Uri.EscapeDataString));
            if (collection) escaped += "/";
            return new Uri(_baseUri, escaped);
        }

        private CloudObjectMetadata ParseMetadata(XElement response, XNamespace dav)
        {
            var href = response.Element(dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(_baseUri, href, out var absoluteUri) || !IsUnderBaseUri(absoluteUri)) return null;
            var property = response.Descendants(dav + "prop").FirstOrDefault();
            if (property == null) return null;
            var relative = Uri.UnescapeDataString(_baseUri.MakeRelativeUri(absoluteUri).ToString()).TrimEnd('/');
            var lengthText = property.Element(dav + "getcontentlength")?.Value;
            var modifiedText = property.Element(dav + "getlastmodified")?.Value;
            return new CloudObjectMetadata
            {
                Path = relative,
                ETag = property.Element(dav + "getetag")?.Value ?? "",
                ContentLength = long.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length) ? length : 0,
                IsCollection = property.Element(dav + "resourcetype")?.Element(dav + "collection") != null,
                LastModifiedUtc = DateTimeOffset.TryParse(modifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var modified) ? modified.ToUniversalTime() : null
            };
        }

        private static CloudObjectMetadata MetadataFromHeaders(string path, HttpResponseMessage response)
        {
            return new CloudObjectMetadata
            {
                Path = path.Replace('\\', '/').Trim('/'),
                ETag = response.Headers.ETag?.ToString() ?? "",
                ContentLength = response.Content.Headers.ContentLength ?? 0,
                LastModifiedUtc = response.Content.Headers.LastModified ?? response.Headers.Date
            };
        }

        private bool IsUnderBaseUri(Uri uri)
        {
            return string.Equals(uri.Scheme, _baseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(uri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
                   uri.Port == _baseUri.Port &&
                   uri.AbsolutePath.StartsWith(_baseUri.AbsolutePath, StringComparison.Ordinal);
        }

        private static bool UrisIdentifySameObject(Uri left, Uri right)
        {
            return string.Equals(left.GetLeftPart(UriPartial.Path).TrimEnd('/'), right.GetLeftPart(UriPartial.Path).TrimEnd('/'), StringComparison.Ordinal);
        }

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta != null) return retryAfter.Delta;
            if (retryAfter?.Date != null)
            {
                var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
            return null;
        }

        private static void ValidateBaseUri(Uri baseUri)
        {
            if (baseUri == null) throw new ArgumentNullException(nameof(baseUri));
            if (!baseUri.IsAbsoluteUri || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
                throw new ArgumentException("WebDAV 地址必须为不含凭据、查询参数和片段的 HTTPS 地址。", nameof(baseUri));
        }

        private static Uri EnsureTrailingSlash(Uri uri)
        {
            return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/");
        }
    }
}
