using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Splunk.Logging
{
    public class HttpEventCollectorException : Exception 
    {
        public HttpStatusCode StatusCode { get; private set; }
        public Exception WebException { get; private set; }
        public string ServerReply { get; private set; }
        public HttpResponseMessage Response { get; private set; }
        public List<HttpEventCollectorEventInfo> Events { get; set; }
        
        public HttpEventCollectorException(
            HttpStatusCode code, 
            Exception webException = null, 
            string reply = null, 
            HttpResponseMessage response = null,
            List<HttpEventCollectorEventInfo> events = null)
        {
            this.StatusCode = code;
            this.WebException = webException;
            this.ServerReply = reply;
            this.Response = response;
            this.Events = events;
        }

        public override string ToString()
        {
            return "StatusCode : " + StatusCode + "\n" +
                   "ServerReply : " + ServerReply + "\n" +
                   "Response : " + Response + "\n" +
                   "Events : " + string.Join(", ", Events) + "\n" +
                   "WebException : " + WebException.StackTrace;
        }
    }
}
