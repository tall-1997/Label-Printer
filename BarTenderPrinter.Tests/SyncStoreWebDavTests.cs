using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class SyncStoreWebDavTests
    {
        [Fact]
        public async Task WebDavUsesRequiredMethodsAndConditionalHeaders()
        {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            using var store = new WebDavObjectStore(new Uri("https://dav.example.test/root/"), client);

            await store.EnsureCollectionAsync("events/device-1");
            await store.HeadAsync("events/device-1/1.evt");
            await store.GetAsync("events/device-1/1.evt");
            await store.PutAsync("events/device-1/1.evt", new byte[] { 1 }, createOnly: true);
            await store.PutAsync("snapshot-pointer.enc", new byte[] { 2 }, ifMatch: "\"etag-1\"");
            await store.ListAsync("events/device-1");

            Assert.Equal(new[] { "MKCOL", "HEAD", "GET", "PUT", "PUT", "PROPFIND" }, handler.Requests.ConvertAll(request => request.Method));
            Assert.Equal("*", handler.Requests[3].IfNoneMatch);
            Assert.Equal("\"etag-1\"", handler.Requests[4].IfMatch);
            Assert.Equal("1", handler.Requests[5].Depth);
        }

        [Fact]
        public async Task PreconditionsAndAuthenticationReturnStableSafeErrors()
        {
            var handler = new RecordingHandler(HttpStatusCode.PreconditionFailed);
            using var client = new HttpClient(handler);
            using var store = new WebDavObjectStore(new Uri("https://dav.example.test/root/"), client);

            var conflict = await Assert.ThrowsAsync<WebDavPreconditionFailedException>(() => store.PutAsync("object.enc", new byte[] { 1 }, createOnly: true));
            Assert.Equal(SyncErrorCodes.WebDavPreconditionFailed, conflict.ErrorCode);

            handler.StatusCode = HttpStatusCode.Unauthorized;
            var authentication = await Assert.ThrowsAsync<WebDavException>(() => store.GetAsync("object.enc"));
            Assert.Equal(SyncErrorCodes.WebDavAuthenticationFailed, authentication.ErrorCode);
            Assert.DoesNotContain("example.test", authentication.Message);
        }

        [Fact]
        public async Task ObjectPathCannotEscapeConfiguredRoot()
        {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            using var store = new WebDavObjectStore(new Uri("https://dav.example.test/root/"), client);

            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("../outside"));
            Assert.Empty(handler.Requests);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.OK) { StatusCode = statusCode; }
            public HttpStatusCode StatusCode { get; set; }
            public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(new RecordedRequest
                {
                    Method = request.Method.Method,
                    IfMatch = Header(request, "If-Match"),
                    IfNoneMatch = Header(request, "If-None-Match"),
                    Depth = Header(request, "Depth")
                });
                var status = StatusCode;
                if (request.Method.Method == "MKCOL" && status == HttpStatusCode.OK) status = HttpStatusCode.Created;
                if (request.Method.Method == "PROPFIND" && status == HttpStatusCode.OK) status = HttpStatusCode.MultiStatus;
                var response = new HttpResponseMessage(status);
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag-new\"");
                response.Content = request.Method.Method == "PROPFIND"
                    ? new StringContent("<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/root/events/device-1/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat></d:response></d:multistatus>", Encoding.UTF8, "application/xml")
                    : new ByteArrayContent(new byte[] { 7 });
                return Task.FromResult(response);
            }

            private static string Header(HttpRequestMessage request, string name)
            {
                return request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
            }
        }

        private sealed class RecordedRequest
        {
            public string Method { get; set; }
            public string IfMatch { get; set; }
            public string IfNoneMatch { get; set; }
            public string Depth { get; set; }
        }
    }
}
