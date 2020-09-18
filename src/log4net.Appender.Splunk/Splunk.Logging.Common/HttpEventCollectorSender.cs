using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Splunk.Logging
{
    public sealed class HttpEventCollectorSender : IDisposable
    {
        public delegate Task<HttpResponseMessage> HttpEventCollectorHandler(
            string token, List<HttpEventCollectorEventInfo> events);

        public delegate Task<HttpResponseMessage> HttpEventCollectorMiddleware(
            string token, List<HttpEventCollectorEventInfo> events, HttpEventCollectorHandler next);

        public delegate dynamic HttpEventCollectorFormatter(HttpEventCollectorEventInfo eventInfo);

        public enum SendMode
        {
            Parallel,
            Sequential
        };

        private const string HttpContentTypeMedia = "application/json";
        private const string HttpEventCollectorPath = "/services/collector/event";
        private const string AuthorizationHeaderScheme = "Splunk";
        private readonly Uri httpEventCollectorEndpointUri;
        private readonly HttpEventCollectorEventInfo.Metadata metadata;
        private readonly string token;

        private readonly int batchSizeBytes;
        private readonly int batchSizeCount;
        private readonly SendMode sendMode;
        private Task activePostTask;
        private readonly object eventsBatchLock = new object();
        private List<HttpEventCollectorEventInfo> eventsBatch = new List<HttpEventCollectorEventInfo>();
        private StringBuilder serializedEventsBatch = new StringBuilder();
        private readonly Timer timer;

        private readonly HttpClient httpClient;
        private readonly HttpEventCollectorMiddleware middleware;
        private readonly HttpEventCollectorFormatter formatter;
        private long activeAsyncTasksCount;

        public event Action<HttpEventCollectorException> OnError = (e) => Console.WriteLine(e.ToString());

        public HttpEventCollectorSender(
            Uri uri,
            string token,
            HttpEventCollectorEventInfo.Metadata metadata,
            SendMode sendMode,
            int batchInterval,
            int batchSizeBytes,
            int batchSizeCount,
            HttpEventCollectorMiddleware middleware,
            HttpEventCollectorFormatter formatter = null,
            bool ignoreCertificateErrors = false
        )
        {
            this.httpEventCollectorEndpointUri = new Uri(uri, HttpEventCollectorPath);
            this.sendMode = sendMode;
            this.batchSizeBytes = batchSizeBytes;
            this.batchSizeCount = batchSizeCount;
            this.metadata = metadata;
            this.token = token;
            this.middleware = middleware;
            this.formatter = formatter;

            if (batchInterval > 0 && this.batchSizeBytes == 0 && this.batchSizeCount == 0)
                this.batchSizeBytes = this.batchSizeCount = int.MaxValue;
            if (this.batchSizeCount == 0 && this.batchSizeBytes > 0)
                this.batchSizeCount = int.MaxValue;
            else if (this.batchSizeBytes == 0 && this.batchSizeCount > 0)
                this.batchSizeBytes = int.MaxValue;
            if (batchInterval != 0)
                timer = new Timer(OnTimer, null, batchInterval, batchInterval);

            if (ignoreCertificateErrors)
            {
                var certificateHandler = new HttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    ServerCertificateCustomValidationCallback =
                        (httpRequestMessage, cert, cetChain, policyErrors) => true
                };
                httpClient = new HttpClient(certificateHandler, true);
            }
            else
                httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                AuthorizationHeaderScheme,
                token
            );
        }

        public void Send(
            DateTime timestamp,
            string id = null,
            string level = null,
            string messageTemplate = null,
            string renderedMessage = null,
            object exception = null,
            object properties = null,
            HttpEventCollectorEventInfo.Metadata metadataOverride = null
        )
        {
            DoSerialization(new HttpEventCollectorEventInfo(
                timestamp,
                id,
                level,
                messageTemplate,
                renderedMessage,
                exception,
                properties,
                metadataOverride ?? metadata
            ));
        }

        private void DoSerialization(HttpEventCollectorEventInfo eventInfo)
        {
            string serializedEventInfo;

            if (formatter == null)
                serializedEventInfo = JsonConvert.SerializeObject(eventInfo);
            else
            {
                var formattedEvent = formatter(eventInfo);
                eventInfo.Event = formattedEvent;
                serializedEventInfo = JsonConvert.SerializeObject(eventInfo);
            }

            lock (eventsBatchLock)
            {
                eventsBatch.Add(eventInfo);
                serializedEventsBatch.Append(serializedEventInfo);
                if (eventsBatch.Count >= batchSizeCount || serializedEventsBatch.Length >= batchSizeBytes)
                    FlushInternal();
            }
        }

        private void FlushInternal()
        {
            if (serializedEventsBatch.Length == 0)
                return;

            switch (sendMode)
            {
                case SendMode.Sequential:
                    FlushInternalSequentialMode(this.eventsBatch, this.serializedEventsBatch.ToString());
                    break;
                case SendMode.Parallel:
                    FlushInternalSingleBatch(this.eventsBatch, this.serializedEventsBatch.ToString());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            this.serializedEventsBatch = new StringBuilder();
            this.eventsBatch = new List<HttpEventCollectorEventInfo>();
        }

        private void FlushInternalSequentialMode(List<HttpEventCollectorEventInfo> events, String serializedEvents)
        {
            if (this.activePostTask == null)
            {
                this.activePostTask = Task.Run(async () => await FlushInternalSingleBatch(events, serializedEvents));
                // this.activePostTask.Wait(); // <-- uncomment this for debugging 
            }
            else
                this.activePostTask = this.activePostTask.ContinueWith(async (_) =>
                    await FlushInternalSingleBatch(events, serializedEvents));
        }

        private Task<HttpStatusCode> FlushInternalSingleBatch(
            List<HttpEventCollectorEventInfo> events,
            String serializedEvents
        )
        {
            Interlocked.Increment(ref activeAsyncTasksCount);
            Task<HttpStatusCode> task = Task.Run(async () => await PostEvents(events, serializedEvents));
            task.ContinueWith((_) => Interlocked.Decrement(ref activeAsyncTasksCount));
            return task;
        }

        private async Task<HttpStatusCode> PostEvents(List<HttpEventCollectorEventInfo> events, String serializedEvents)
        {
            HttpResponseMessage response = null;
            string serverReply = null;
            HttpStatusCode responseCode = HttpStatusCode.OK;
            try
            {
                Task<HttpResponseMessage> Next(string t, List<HttpEventCollectorEventInfo> e)
                {
                    HttpContent content = new StringContent(serializedEvents, Encoding.UTF8, HttpContentTypeMedia);
                    return httpClient.PostAsync(httpEventCollectorEndpointUri, content);
                }

                Task<HttpResponseMessage> Events(string t, List<HttpEventCollectorEventInfo> e)
                {
                    return middleware == null ? Next(t, e) : middleware(t, e, Next);
                }

                response = await Events(token, events);
                responseCode = response.StatusCode;

                if (responseCode != HttpStatusCode.OK && response.Content != null)
                {
                    serverReply = await response.Content.ReadAsStringAsync();
                    OnError(new HttpEventCollectorException(
                        code: responseCode,
                        webException: null,
                        reply: serverReply,
                        response: response,
                        events: events
                    ));
                }
            }
            catch (HttpEventCollectorException e)
            {
                e.Events = events;
                OnError(e);
            }
            catch (Exception e)
            {
                OnError(new HttpEventCollectorException(
                    code: responseCode,
                    webException: e,
                    reply: serverReply,
                    response: response,
                    events: events
                ));
            }

            return responseCode;
        }

        public void FlushSync()
        {
            Flush();
            while (Interlocked.CompareExchange(ref activeAsyncTasksCount, 0, 0) != 0)
            {
                Thread.Sleep(100);
            }
        }

        public Task FlushAsync()
        {
            return new Task(FlushSync);
        }

        private void Flush()
        {
            lock (eventsBatchLock)
                FlushInternal();
        }

        private void OnTimer(object state)
        {
            Flush();
        }

        #region HttpClientHandler.IDispose

        private bool disposed;

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                timer?.Dispose();
                httpClient.Dispose();
            }

            disposed = true;
        }

        ~HttpEventCollectorSender()
        {
            Dispose(false);
        }

        #endregion
    }
}
