using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Splunk.Logging
{
    public class HttpEventCollectorResendMiddleware
    {
        private static readonly HttpStatusCode[] HttpEventCollectorApplicationErrors =
        {
            HttpStatusCode.Forbidden, HttpStatusCode.MethodNotAllowed, HttpStatusCode.BadRequest
        };

        private const int RetryDelayCeiling = 60 * 1000;
        private readonly int retriesOnError = 0;

        public HttpEventCollectorResendMiddleware(int retriesOnError)
        {
            this.retriesOnError = retriesOnError;
        }

        public async Task<HttpResponseMessage> Plugin(
            string token,
            List<HttpEventCollectorEventInfo> events,
            HttpEventCollectorSender.HttpEventCollectorHandler next
        )
        {
            HttpResponseMessage response = null;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            Exception webException = null;
            string serverReply = null;
            int retryDelay = 1000;
            for (int retriesCount = 0; retriesCount <= retriesOnError; retriesCount++)
            {
                try
                {
                    response = await next(token, events);
                    statusCode = response.StatusCode;
                    if (statusCode == HttpStatusCode.OK)
                    {
                        webException = null;
                        break;
                    }
                    if (Array.IndexOf(HttpEventCollectorApplicationErrors, statusCode) >= 0)
                    {
                        if (response.Content != null)
                        {
                            serverReply = await response.Content.ReadAsStringAsync();
                        }
                        break;
                    }
                }
                catch (Exception e)
                {
                    webException = e;
                }

                await Task.Delay(retryDelay);
                retryDelay = Math.Min(RetryDelayCeiling, retryDelay * 2);
            }

            if (statusCode != HttpStatusCode.OK || webException != null)
            {
                throw new HttpEventCollectorException(
                    code: statusCode,
                    webException: webException,
                    reply: serverReply,
                    response: response
                );
            }

            return response;
        }
    }
}
