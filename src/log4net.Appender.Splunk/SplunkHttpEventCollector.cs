using log4net.Core;
using Splunk.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace log4net.Appender.Splunk
{
    public class SplunkHttpEventCollector : AppenderSkeleton
    {
        private HttpEventCollectorSender _hecSender;
        public string ServerUrl { get; set; }
        public string Token { get; set; }
        public string Index { get; set; }
        public string Host { get; set; }
        public int RetriesOnError { get; set; }
        public int BatchIntevalMs { get; set; }
        public int BatchSizeCount { get; set; }
        HttpEventCollectorSender.SendMode SendMode { get; set; } = HttpEventCollectorSender.SendMode.Sequential;
        public bool IgnoreCertificateErrors { get; set; }

        protected override bool RequiresLayout => true;

        public override void ActivateOptions()
        {
            _hecSender = new HttpEventCollectorSender(
                new Uri(ServerUrl),
                Token,
                new HttpEventCollectorEventInfo.Metadata(Index, null, "_json", GetMachineName()),
                SendMode,
                0,
                0,
                0,
                new HttpEventCollectorResendMiddleware(RetriesOnError).Plugin,
                null,
                IgnoreCertificateErrors
            );
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (loggingEvent == null)
                throw new ArgumentNullException(nameof(loggingEvent));

            if (_hecSender == null)
                throw new Exception("SplunkHttpEventCollector Append() called before ActivateOptions()");

            var metaData = new HttpEventCollectorEventInfo.Metadata(
                Index,
                loggingEvent.LoggerName,
                "_json",
                GetMachineName()
            );

            var properties = new Dictionary<String, object>
            {
                {"Source", loggingEvent.LoggerName}, {"Host", GetMachineName()}
            };

            if (loggingEvent.Properties != null && loggingEvent.Properties.Count > 0)
                foreach (var key in loggingEvent.Properties.GetKeys())
                    properties.Add(key, loggingEvent.Properties[key]);

            _hecSender.Send(
                loggingEvent.TimeStampUtc,
                null,
                loggingEvent.Level.Name,
                null,
                loggingEvent.RenderedMessage,
                loggingEvent.ExceptionObject,
                properties,
                metaData
            );

            _hecSender.FlushSync();
        }

        private string GetMachineName()
        {
            if (!string.IsNullOrEmpty(Host))
                return Host;
            var computerName = Environment.GetEnvironmentVariable("COMPUTERNAME");
            return !string.IsNullOrEmpty(computerName) ? computerName : System.Net.Dns.GetHostName();
        }

        public override bool Flush(int millisecondsTimeout)
        {
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(millisecondsTimeout));
            Task.Run(async () => await _hecSender.FlushAsync(), cancellationTokenSource.Token).Wait();
            return true;
        }
    }
}
