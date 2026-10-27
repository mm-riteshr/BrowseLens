using System;
using System.Collections.Generic;
using BrowseLens.Core.Models;
using BrowseLens.Core.Privacy;
using BrowseLens.Core.TimeAccounting;

namespace BrowseLens.UnitTests
{
    public class PrivacyPolicyTests
    {
        public static void RunAll()
        {
            Test_UrlQueryAndFragmentRedaction();
            Test_DomainExtraction();
            Test_RestrictedUrl_Detection();
            Test_Incognito_Rejection();
            Test_DomainExclusion();
        }

        private static void Test_UrlQueryAndFragmentRedaction()
        {
            string sensitiveUrl = "https://example.com/checkout/pay?session_id=secret123&user=john#confirmation";
            string redacted = UrlRedactor.RedactUrl(sensitiveUrl, true);
            string unredacted = UrlRedactor.RedactUrl(sensitiveUrl, false);

            Assert(redacted == "https://example.com/checkout/pay", "Query and fragment must be removed when redaction is enabled");
            Assert(!redacted.Contains("secret123"), "Redacted URL must not contain query parameters");
            Assert(!redacted.Contains("confirmation"), "Redacted URL must not contain fragment");
            Assert(unredacted.Contains("session_id=secret123"), "Unredacted retains query string when option is false");

            Console.WriteLine("  [PASS] TC-016: URL query-string and fragment redaction verified");
        }

        private static void Test_DomainExtraction()
        {
            string url1 = "https://SubDomain.Example.COM:8080/path/to/page";
            string url2 = "http://internal-portal.corp/home";

            string d1 = UrlRedactor.ExtractDomain(url1);
            string d2 = UrlRedactor.ExtractDomain(url2);

            Assert(d1 == "subdomain.example.com", "Domain must be lowercase without port or path");
            Assert(d2 == "internal-portal.corp", "Domain without port must extract cleanly");

            Console.WriteLine("  [PASS] TC-Domain: Domain normalization to lowercase hostname verified");
        }

        private static void Test_RestrictedUrl_Detection()
        {
            Assert(ExclusionPolicy.IsRestrictedUrl("chrome://settings"), "chrome:// must be restricted");
            Assert(ExclusionPolicy.IsRestrictedUrl("chrome://extensions"), "chrome://extensions must be restricted");
            Assert(ExclusionPolicy.IsRestrictedUrl("edge://settings"), "edge:// must be restricted");
            Assert(ExclusionPolicy.IsRestrictedUrl("about:blank"), "about:blank must be restricted");
            Assert(ExclusionPolicy.IsRestrictedUrl("file:///C:/passwords.txt"), "file:// must be restricted");
            Assert(ExclusionPolicy.IsRestrictedUrl("chrome-extension://abcdef/popup.html"), "chrome-extension:// must be restricted");
            Assert(!ExclusionPolicy.IsRestrictedUrl("https://en.wikipedia.org"), "https:// must be permitted");
            Assert(!ExclusionPolicy.IsRestrictedUrl("http://localhost:3000"), "http:// must be permitted");

            Console.WriteLine("  [PASS] TC-022: Restricted browser internal pages (chrome://, edge://, about:) detected");
        }

        private static void Test_Incognito_Rejection()
        {
            var sm = new SessionStateMachine();
            var env = new BrowserEventEnvelope
            {
                SchemaVersion = 1,
                EventId = "evt-incog-1",
                Sequence = 1,
                EventType = "TabActivated",
                OccurredAtUtc = DateTime.UtcNow,
                Payload = new TabEventPayload
                {
                    Url = "https://private-site.com",
                    Title = "Private",
                    IsWindowFocused = true,
                    IsIncognito = true // Marked incognito
                }
            };

            string rejection;
            bool accepted = sm.ProcessEvent(env, out rejection);

            Assert(!accepted, "Incognito events must be rejected by state machine");
            Assert(rejection.Contains("Incognito"), "Rejection must state incognito reason");
            Assert(sm.ActiveSession == null, "No session created for incognito tab");

            Console.WriteLine("  [PASS] TC-Incognito: Incognito activity rejected by default (BRL-009)");
        }

        private static void Test_DomainExclusion()
        {
            var exclusions = new List<string> { "bank.com", "*.internal.corp", "health-portal.org" };

            Assert(ExclusionPolicy.IsDomainExcluded("https://bank.com/account", exclusions), "Exact domain match excluded");
            Assert(ExclusionPolicy.IsDomainExcluded("https://my.bank.com/dashboard", exclusions), "Subdomain of exact pattern excluded");
            Assert(ExclusionPolicy.IsDomainExcluded("https://dev.internal.corp/code", exclusions), "Wildcard subdomain pattern excluded");
            Assert(!ExclusionPolicy.IsDomainExcluded("https://github.com", exclusions), "Unexcluded domain allowed");

            Console.WriteLine("  [PASS] TC-015: Excluded domains and wildcard patterns correctly matched");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}

