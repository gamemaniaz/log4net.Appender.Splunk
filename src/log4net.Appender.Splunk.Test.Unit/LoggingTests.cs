using System.Reflection;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using NUnit.Framework;

namespace log4net.Appender.Splunk.Test.Unit
{
    public class LoggingTests
    {
        private const string ServerUrl             = "{INSERT_SPLUNK_HOST}";
        private const string Token                 = "{INSERT_SPLUNK_HEC_TOKEN}";
        private const int RetriesOnError           = 0;
        private const string ConversionPattern     = "%message";
        private const bool IgnoreCertificateErrors = true;
        
        private ILog logger;
        
        [SetUp]
        public void Setup()
        {
            Hierarchy hierarchy = (Hierarchy) LogManager.GetRepository(Assembly.GetCallingAssembly());

            var eventCollector = new SplunkHttpEventCollector
            {
                ServerUrl = ServerUrl,
                Token = Token,
                RetriesOnError = RetriesOnError,
                IgnoreCertificateErrors = IgnoreCertificateErrors
            };
            PatternLayout patternLayout = new PatternLayout
            {
                ConversionPattern = ConversionPattern
            };
            patternLayout.ActivateOptions();
            eventCollector.Layout = patternLayout;
            eventCollector.ActivateOptions();
            
            hierarchy.Root.AddAppender(eventCollector);
            hierarchy.Threshold = Level.All;
            hierarchy.Configured = true;

            logger = LogManager.GetLogger(GetType());
        }

        [Test]
        public void SendInfoWithoutException()
        {
            logger.Info("This is an INFO message without exception");
        }

        [Test]
        public void SendErrorWithoutException()
        {
            logger.Error("This is an ERROR message without exception");
        }
    }
}
