using Newtonsoft.Json;
using System;
using System.Globalization;

namespace Splunk.Logging
{
    public class HttpEventCollectorEventInfo
    {
        private const string MetadataTimeTag = "time";
        private const string MetadataIndexTag = "index";
        private const string MetadataSourceTag = "source";
        private const string MetadataSourceTypeTag = "sourcetype";
        private const string MetadataHostTag = "host";
        private const string EventIdTag = "id";
        private const string EventLevelTag = "level";
        private const string EventMessageTemplateTag = "messageTemplate";
        private const string EventRenderedMessageTag = "renderedMessage";
        private const string EventExceptionTag = "exception";
        private const string EventPropertiesTag = "properties";
        
        public class Metadata
        {
            public string Index { get; }
            public string Source { get; }
            public string SourceType { get; }
            public string Host { get; }

            public Metadata(
                string index = null,
                string source = null,
                string sourceType = null,
                string host = null
            )
            {
                this.Index = index;
                this.Source = source;
                this.SourceType = sourceType;
                this.Host = host;
            }
        }

        private readonly Metadata metadata;

        public struct LoggerEvent
        {
            [JsonProperty(PropertyName = EventIdTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string Id { get; private set; }

            [JsonProperty(PropertyName = EventLevelTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string Level { get; private set; }

            [JsonProperty(PropertyName = EventMessageTemplateTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string MessageTemplate { get; private set; }

            [JsonProperty(PropertyName = EventRenderedMessageTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string RenderedMessage { get; private set; }

            [JsonProperty(PropertyName = EventExceptionTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public object Exception { get; private set; }

            [JsonProperty(PropertyName = EventPropertiesTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public object Properties { get; private set; }

            internal LoggerEvent(
                string id,
                string level,
                string messageTemplate,
                string renderedMessage,
                object exception,
                object properties
            ) : this()
            {
                this.Id = id;
                this.Level = level;
                this.MessageTemplate = messageTemplate;
                this.RenderedMessage = renderedMessage;
                this.Exception = exception;
                this.Properties = properties;
            }
        }

        [JsonProperty(PropertyName = MetadataTimeTag)]
        public string Timestamp { get; private set; }

        [JsonProperty(PropertyName = MetadataIndexTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Index { get { return metadata.Index; } }

        [JsonProperty(PropertyName = MetadataSourceTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Source { get { return metadata.Source; } }

        [JsonProperty(PropertyName = MetadataSourceTypeTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SourceType { get { return metadata.SourceType; } }

        [JsonProperty(PropertyName = MetadataHostTag, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Host { get { return metadata.Host; } }
        
        [JsonProperty(PropertyName = "event", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public dynamic Event { get; set; }

        public HttpEventCollectorEventInfo(
            DateTime datetime,
            string id,
            string level,
            string messageTemplate,
            string renderedMessage,
            object exception,
            object properties,
            Metadata metadata
        )
        {
            double epochTime = (datetime - new DateTime(1970, 1, 1)).TotalSeconds;
            this.Timestamp = epochTime.ToString("#.000", CultureInfo.InvariantCulture);
            this.metadata = metadata ?? new Metadata();
            this.Event = new LoggerEvent(id, level, messageTemplate, renderedMessage, exception, properties);
        }

        public HttpEventCollectorEventInfo(
            string id,
            string level,
            string messageTemplate,
            string renderedMessage,
            object exception,
            object properties,
            Metadata metadata
        ) : this(DateTime.UtcNow, id, level, messageTemplate, renderedMessage, exception, properties, metadata)
        {
        }
    }
}
