using System;
using BrowseLens.Core.Models;
using BrowseLens.Infrastructure.Ipc;

namespace BrowseLens.UnitTests
{
    public class EventValidationTests
    {
        public static void RunAll()
        {
            Test_ValidEnvelope_Parsing();
            Test_InvalidSchemaVersion_Rejection();
            Test_MissingEventId_Rejection();
            Test_UnsupportedEventType_Rejection();
            Test_SubObject_PayloadExtraction();
        }

        private static void Test_ValidEnvelope_Parsing()
        {
            var server = new LocalHttpEventServer(59000, "test-secret");
            string validJson = "{" +
                "\"schemaVersion\": 1," +
                "\"eventId\": \"evt-100\"," +
                "\"sequence\": 5," +
                "\"browser\": \"Chrome\"," +
                "\"eventType\": \"TabActivated\"," +
                "\"occurredAtUtc\": \"2026-10-26T12:00:00Z\"," +
                "\"windowKey\": \"w-1\"," +
                "\"tabKey\": \"t-42\"," +
                "\"payload\": {" +
                    "\"url\": \"https://example.com/docs\"," +
                    "\"title\": \"Example Documentation\"," +
                    "\"isWindowFocused\": true," +
                    "\"isIncognito\": false" +
                "}" +
            "}";

            string error;
            BrowserEventEnvelope envelope = server.ParseAndValidateJson(validJson, out error);

            Assert(envelope != null, "Valid JSON should parse successfully");
            Assert(string.IsNullOrEmpty(error), "Error should be null");
            Assert(envelope.SchemaVersion == 1, "SchemaVersion should be 1");
            Assert(envelope.EventId == "evt-100", "EventId should be evt-100");
            Assert(envelope.Sequence == 5, "Sequence should be 5");
            Assert(envelope.EventType == "TabActivated", "EventType should be TabActivated");
            Assert(envelope.Payload != null, "Payload should not be null");
            Assert(envelope.Payload.Url == "https://example.com/docs", "URL should match");
            Assert(envelope.Payload.Title == "Example Documentation", "Title should match");
            Assert(envelope.Payload.IsWindowFocused == true, "IsWindowFocused should be true");
            Assert(envelope.Payload.IsIncognito == false, "IsIncognito should be false");

            Console.WriteLine("  [PASS] EventValidation: Valid envelope parsed with full field extraction");
        }

        private static void Test_InvalidSchemaVersion_Rejection()
        {
            var server = new LocalHttpEventServer(59000, "test-secret");
            string json = "{\"schemaVersion\": 2, \"eventId\": \"evt-2\", \"eventType\": \"TabActivated\"}";

            string error;
            BrowserEventEnvelope envelope = server.ParseAndValidateJson(json, out error);

            Assert(envelope == null, "Unsupported schemaVersion must be rejected");
            Assert(error.Contains("schemaVersion"), "Error must mention schemaVersion");
            Console.WriteLine("  [PASS] EventValidation: Unsupported schema version rejected");
        }

        private static void Test_MissingEventId_Rejection()
        {
            var server = new LocalHttpEventServer(59000, "test-secret");
            string json = "{\"schemaVersion\": 1, \"eventType\": \"TabActivated\"}";

            string error;
            BrowserEventEnvelope envelope = server.ParseAndValidateJson(json, out error);

            Assert(envelope == null, "Missing eventId must be rejected");
            Assert(error.Contains("eventId"), "Error must mention eventId");
            Console.WriteLine("  [PASS] EventValidation: Missing eventId rejected");
        }

        private static void Test_UnsupportedEventType_Rejection()
        {
            var server = new LocalHttpEventServer(59000, "test-secret");
            string json = "{\"schemaVersion\": 1, \"eventId\": \"evt-3\", \"eventType\": \"MaliciousExecute\"}";

            string error;
            BrowserEventEnvelope envelope = server.ParseAndValidateJson(json, out error);

            Assert(envelope == null, "Unsupported eventType must be rejected");
            Assert(error.Contains("eventType"), "Error must mention eventType");
            Console.WriteLine("  [PASS] EventValidation: Disallowed event type rejected");
        }

        private static void Test_SubObject_PayloadExtraction()
        {
            var server = new LocalHttpEventServer(59000, "test-secret");
            string json = "{\"schemaVersion\": 1, \"eventId\": \"evt-4\", \"eventType\": \"TabUpdated\", \"payload\": {\"url\": \"https://news.ycombinator.com\", \"title\": \"Hacker News\", \"isWindowFocused\": true}}";

            string error;
            BrowserEventEnvelope envelope = server.ParseAndValidateJson(json, out error);

            Assert(envelope != null, "Should parse payload object");
            Assert(envelope.Payload.Url == "https://news.ycombinator.com", "Url should match");
            Assert(envelope.Payload.Title == "Hacker News", "Title should match");
            Console.WriteLine("  [PASS] EventValidation: Sub-object extraction verified");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}

