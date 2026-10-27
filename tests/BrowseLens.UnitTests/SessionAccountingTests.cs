using System;
using BrowseLens.Core.Models;
using BrowseLens.Core.TimeAccounting;

namespace BrowseLens.UnitTests
{
    public class SessionAccountingTests
    {
        public static void RunAll()
        {
            Test_TabActivation_StartsForegroundSession();
            Test_TabSwitch_ClosesPriorSessionWithoutOverlap();
            Test_WindowFocusLost_ClosesActiveSession();
            Test_WindowFocusRegained_ResumesSession();
            Test_DuplicateEvent_IgnoredWithoutDoubleCounting();
            Test_OutOfOrderEvent_Rejected();
            Test_PauseResume_ExcludesPausedPeriod();
            Test_Disconnection_ClosesSession();
            Test_MultiTabUserBrowsingJourney();
        }

        private static void Test_TabActivation_StartsForegroundSession()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);

            var env = CreateEnvelope("evt-1", 1, "TabActivated", t0, "w-1", "t-1", "https://github.com", "GitHub", true);
            string rejection;
            bool ok = sm.ProcessEvent(env, out rejection);

            Assert(ok, "Event should be processed");
            Assert(sm.ActiveSession != null, "Active session should be created");
            Assert(sm.ActiveSession.Url == "https://github.com", "Url should match");
            Assert(sm.ActiveSession.NormalizedDomain == "github.com", "Domain should be normalized");
            Assert(sm.ActiveSession.StartedAtUtc == t0, "Start time should match");
            Assert(sm.ActiveSession.IsActive, "Session should be active");
            Console.WriteLine("  [PASS] TC-004: First tab activation starts foreground session");
        }

        private static void Test_TabSwitch_ClosesPriorSessionWithoutOverlap()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(15); // 15 seconds browsing on Tab 1

            string dummy;
            sm.ProcessEvent(CreateEnvelope("evt-1", 1, "TabActivated", t0, "w-1", "t-1", "https://site-a.com", "Site A", true), out dummy);
            sm.ProcessEvent(CreateEnvelope("evt-2", 2, "TabActivated", t1, "w-1", "t-2", "https://site-b.com", "Site B", true), out dummy);

            Assert(sm.CompletedSessions.Count == 1, "Tab 1 session should be completed");
            var prev = sm.CompletedSessions[0];
            Assert(prev.Url == "https://site-a.com", "Completed session was site-a");
            Assert(prev.ForegroundDurationMs == 15000, "Duration should be exactly 15,000 ms");
            Assert(prev.EndedAtUtc == t1, "EndedAtUtc should equal t1");
            Assert(prev.EndReason == "Switch", "End reason should be Switch");

            Assert(sm.ActiveSession != null, "Tab 2 session should be active");
            Assert(sm.ActiveSession.Url == "https://site-b.com", "Active session is site-b");
            Assert(sm.ActiveSession.StartedAtUtc == t1, "Tab 2 starts at t1 (no overlap or gap)");

            Console.WriteLine("  [PASS] TC-005: Switch between tabs closes prior interval and starts new one without overlap");
        }

        private static void Test_WindowFocusLost_ClosesActiveSession()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(10);

            string dummy;
            sm.ProcessEvent(CreateEnvelope("evt-1", 1, "TabActivated", t0, "w-1", "t-1", "https://news.com", "News", true), out dummy);
            sm.ProcessEvent(CreateEnvelope("evt-2", 2, "WindowFocusChanged", t1, "w-1", "t-1", "https://news.com", "News", false), out dummy);

            Assert(sm.ActiveSession == null, "Active session should be closed when window loses focus");
            Assert(sm.CompletedSessions.Count == 1, "Completed session should exist");
            Assert(sm.CompletedSessions[0].ForegroundDurationMs == 10000, "Duration should be 10,000 ms");
            Assert(sm.CompletedSessions[0].EndReason == "FocusLost", "End reason should be FocusLost");

            Console.WriteLine("  [PASS] TC-007: Window focus loss pauses/closes active foreground session");
        }

        private static void Test_WindowFocusRegained_ResumesSession()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(10); // lost focus
            DateTime t2 = t0.AddSeconds(60); // regained focus 50 seconds later

            string dummy;
            sm.ProcessEvent(CreateEnvelope("evt-1", 1, "TabActivated", t0, "w-1", "t-1", "https://news.com", "News", true), out dummy);
            sm.ProcessEvent(CreateEnvelope("evt-2", 2, "WindowFocusChanged", t1, "w-1", "t-1", "", "", false), out dummy);
            sm.ProcessEvent(CreateEnvelope("evt-3", 3, "WindowFocusChanged", t2, "w-1", "t-1", "https://news.com", "News", true), out dummy);

            Assert(sm.CompletedSessions.Count == 1, "First session was 10s");
            Assert(sm.CompletedSessions[0].ForegroundDurationMs == 10000, "10s on first interval");
            Assert(sm.ActiveSession != null, "New active session opened upon focus regain");
            Assert(sm.ActiveSession.StartedAtUtc == t2, "New session starts at t2 (the 50s unfocused gap is excluded)");

            Console.WriteLine("  [PASS] TC-008: Window focus regain resumes tracking without counting unfocused gap");
        }

        private static void Test_DuplicateEvent_IgnoredWithoutDoubleCounting()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);

            var env = CreateEnvelope("evt-dup-1", 1, "TabActivated", t0, "w-1", "t-1", "https://example.com", "Example", true);
            string rej1, rej2;
            bool ok1 = sm.ProcessEvent(env, out rej1);
            bool ok2 = sm.ProcessEvent(env, out rej2);

            Assert(ok1, "First event should be accepted");
            Assert(!ok2, "Second duplicate event must be rejected/ignored");
            Assert(rej2.Contains("Duplicate"), "Rejection must state duplicate");
            Assert(sm.CompletedSessions.Count == 0, "No duplicate interval created");

            Console.WriteLine("  [PASS] TC-010: Duplicate event rejected and does not double-count");
        }

        private static void Test_OutOfOrderEvent_Rejected()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);

            string dummy;
            sm.ProcessEvent(CreateEnvelope("evt-10", 10, "TabActivated", t0, "w-1", "t-1", "https://a.com", "A", true), out dummy);
            string rej;
            bool ok = sm.ProcessEvent(CreateEnvelope("evt-5", 5, "TabActivated", t0.AddSeconds(1), "w-1", "t-2", "https://b.com", "B", true), out rej);

            Assert(!ok, "Out-of-order event (sequence 5 after 10) must be rejected");
            Assert(rej.Contains("Out-of-order"), "Rejection reason must mention sequence");
            Console.WriteLine("  [PASS] TC-011: Out-of-order event sequence rejected");
        }

        private static void Test_PauseResume_ExcludesPausedPeriod()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(20); // user clicks pause
            DateTime t2 = t0.AddSeconds(80); // user clicks resume (60 seconds paused)

            string dummy;
            sm.ProcessEvent(CreateEnvelope("evt-1", 1, "TabActivated", t0, "w-1", "t-1", "https://site.com", "Site", true), out dummy);
            sm.SetTrackingPaused(true, t1);

            Assert(sm.IsPaused, "StateMachine is paused");
            Assert(sm.ActiveSession == null, "Active session stopped on pause");
            Assert(sm.CompletedSessions.Count == 1, "Session closed");
            Assert(sm.CompletedSessions[0].ForegroundDurationMs == 20000, "Duration was 20,000 ms");
            Assert(sm.CompletedSessions[0].EndReason == "Paused", "End reason was Paused");

            // Event arriving while paused must not start new sessions
            sm.ProcessEvent(CreateEnvelope("evt-2", 2, "TabActivated", t0.AddSeconds(40), "w-1", "t-2", "https://private.com", "Priv", true), out dummy);
            Assert(sm.ActiveSession == null, "No session started while paused");

            // Resume
            sm.SetTrackingPaused(false, t2);
            sm.ProcessEvent(CreateEnvelope("evt-3", 3, "TabActivated", t2, "w-1", "t-1", "https://site.com", "Site", true), out dummy);

            Assert(sm.ActiveSession != null, "Session resumed at t2");
            Assert(sm.ActiveSession.StartedAtUtc == t2, "Starts at t2; paused 60 seconds completely excluded");

            Console.WriteLine("  [PASS] TC-017 & TC-018: Pause and resume properly excludes paused periods");
        }

        private static void Test_Disconnection_ClosesSession()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);
            DateTime t1 = t0.AddSeconds(30);

            string dummy;
            sm.ProcessEvent(CreateEnvelope("evt-1", 1, "TabActivated", t0, "w-1", "t-1", "https://docs.microsoft.com", "Docs", true), out dummy);
            sm.HandleDisconnect(t1);

            Assert(sm.ActiveSession == null, "Active session closed on disconnect");
            Assert(sm.CompletedSessions.Count == 1, "Session preserved in history");
            Assert(sm.CompletedSessions[0].ForegroundDurationMs == 30000, "Duration is 30s");
            Assert(sm.CompletedSessions[0].EndReason == "Disconnected", "End reason is Disconnected");

            Console.WriteLine("  [PASS] TC-Disconnect: Clean closure upon extension or companion disconnection");
        }

        private static void Test_MultiTabUserBrowsingJourney()
        {
            var sm = new SessionStateMachine();
            DateTime t0 = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);
            string rej;
            long seq = 0;

            // Step 1 & 2: Open URL in Tab 1 (GitHub)
            bool ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0, "w-1", "t-1", "https://github.com/microsoft/dotnet", "GitHub — dotnet", true), out rej);
            Assert(ok, "Step 1: Open Tab 1 should be accepted");
            Assert(sm.ActiveSession != null && sm.ActiveSession.NormalizedDomain == "github.com", "Step 2: Tab 1 recorded as active");

            // Step 3: Open URL in Tab 2 (Stack Overflow) at t0 + 10s
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(10), "w-1", "t-2", "https://stackoverflow.com/questions", "Stack Overflow — Questions", true), out rej);
            Assert(ok, "Step 3: Open Tab 2 should be accepted");
            Assert(sm.CompletedSessions.Count == 1, "Tab 1 session finalized");
            Assert(sm.CompletedSessions[0].ForegroundDurationMs == 10000, "Tab 1 duration is 10s");
            Assert(sm.ActiveSession?.NormalizedDomain == "stackoverflow.com", "Tab 2 active");

            // Step 4: Open 3 more tabs (Tabs 3, 4, 5)
            // Tab 3: Wikipedia at t0 + 25s (15s on Tab 2)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(25), "w-1", "t-3", "https://wikipedia.org/wiki/Special:Search", "Wikipedia — Search", true), out rej);
            Assert(ok && sm.CompletedSessions.Count == 2 && sm.CompletedSessions[1].ForegroundDurationMs == 15000, "Tab 2 duration is 15s");

            // Tab 4: MDN at t0 + 40s (15s on Tab 3)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(40), "w-1", "t-4", "https://developer.mozilla.org/en-US/docs/Web", "MDN Web Docs", true), out rej);
            Assert(ok && sm.CompletedSessions.Count == 3 && sm.CompletedSessions[2].ForegroundDurationMs == 15000, "Tab 3 duration is 15s");

            // Tab 5: Hacker News at t0 + 60s (20s on Tab 4)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(60), "w-1", "t-5", "https://news.ycombinator.com/news", "Hacker News", true), out rej);
            Assert(ok && sm.CompletedSessions.Count == 4 && sm.CompletedSessions[3].ForegroundDurationMs == 20000, "Tab 4 duration is 20s");
            Assert(sm.ActiveSession?.NormalizedDomain == "news.ycombinator.com", "Tab 5 active");

            // Step 5: Switch through the tabs one by one and check recording
            // Switch to Tab 1 (GitHub) at t0 + 70s (10s on Tab 5)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(70), "w-1", "t-1", "https://github.com/microsoft/dotnet", "GitHub — dotnet", true), out rej);
            Assert(ok && sm.ActiveSession?.NormalizedDomain == "github.com", "Switch to Tab 1 recorded");

            // Switch to Tab 2 (StackOverflow) at t0 + 85s (15s on Tab 1)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(85), "w-1", "t-2", "https://stackoverflow.com/questions", "Stack Overflow — Questions", true), out rej);
            Assert(ok && sm.ActiveSession?.NormalizedDomain == "stackoverflow.com", "Switch to Tab 2 recorded");

            // Switch to Tab 3 (Wikipedia) at t0 + 95s (10s on Tab 2)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(95), "w-1", "t-3", "https://wikipedia.org/wiki/Special:Search", "Wikipedia — Search", true), out rej);
            Assert(ok && sm.ActiveSession?.NormalizedDomain == "wikipedia.org", "Switch to Tab 3 recorded");

            // Switch to Tab 4 (MDN) at t0 + 110s (15s on Tab 3)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(110), "w-1", "t-4", "https://developer.mozilla.org/en-US/docs/Web", "MDN Web Docs", true), out rej);
            Assert(ok && sm.ActiveSession?.NormalizedDomain == "developer.mozilla.org", "Switch to Tab 4 recorded");

            // Switch to Tab 5 (Hacker News) at t0 + 120s (10s on Tab 4)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(120), "w-1", "t-5", "https://news.ycombinator.com/news", "Hacker News", true), out rej);
            Assert(ok && sm.ActiveSession?.NormalizedDomain == "news.ycombinator.com", "Switch to Tab 5 recorded");

            // Step 6: Close one of them (Tab 3: Wikipedia) at t0 + 130s (10s on Tab 5)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabRemoved", t0.AddSeconds(130), "w-1", "t-3", "", "", true), out rej);
            Assert(ok, "Step 6: Tab 3 closed without disrupting active Tab 5");
            Assert(sm.ActiveSession?.NormalizedDomain == "news.ycombinator.com", "Tab 5 remains active after Tab 3 closed");

            // Step 7: Go back to previous tab (Tab 2) and do activity on the page
            // Switch to Tab 2 at t0 + 140s (10s on Tab 5)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(140), "w-1", "t-2", "https://stackoverflow.com/questions", "Stack Overflow — Questions", true), out rej);
            Assert(ok && sm.ActiveSession?.NormalizedDomain == "stackoverflow.com", "Step 7a: Return to Tab 2");

            // In-page navigation / search activity on Tab 2 at t0 + 150s (10s on questions page)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabUpdated", t0.AddSeconds(150), "w-1", "t-2", "https://stackoverflow.com/questions/98765/tcp-sockets-in-csharp", "TCP Sockets in C# — Stack Overflow", true), out rej);
            Assert(ok, "Step 7b: Activity/Navigation on Tab 2 recorded");
            Assert(sm.ActiveSession?.Url == "https://stackoverflow.com/questions/98765/tcp-sockets-in-csharp", "New page URL recorded");

            // Step 8: Open a new tab (Tab 6) with a new URL at t0 + 170s (20s on question page)
            ok = sm.ProcessEvent(CreateEnvelope($"evt-{++seq}", seq, "TabActivated", t0.AddSeconds(170), "w-1", "t-6", "https://dotnet.microsoft.com/en-us/", ".NET Platform", true), out rej);
            Assert(ok, "Step 8: Open Tab 6 should be accepted");
            Assert(sm.ActiveSession?.NormalizedDomain == "dotnet.microsoft.com", "Tab 6 active");

            // Close session at t0 + 200s (30s on Tab 6)
            sm.CloseActiveSession(t0.AddSeconds(200), "CleanShutdown");

            // Global Validation Assertions
            Assert(sm.CompletedSessions.Count == 13, string.Format("Expected exactly 13 completed sessions, got {0}", sm.CompletedSessions.Count));
            long totalDurationMs = 0;
            foreach (var s in sm.CompletedSessions)
            {
                Assert(s.ForegroundDurationMs > 0, "Duration must be positive");
                totalDurationMs += s.ForegroundDurationMs;
            }
            Assert(totalDurationMs == 200000, string.Format("Total foreground duration must equal exact 200s (200000ms), got {0}", totalDurationMs));

            Console.WriteLine("  [PASS] TC-MultiTab: Realistic 5-tab browsing lifecycle, tab switches, closing, in-page activity, and new tab addition verified.");
        }

        private static BrowserEventEnvelope CreateEnvelope(
            string eventId,
            long seq,
            string eventType,
            DateTime occurredAtUtc,
            string winKey,
            string tabKey,
            string url,
            string title,
            bool isFocused)
        {
            return new BrowserEventEnvelope
            {
                SchemaVersion = 1,
                EventId = eventId,
                Sequence = seq,
                Browser = "Chrome",
                EventType = eventType,
                OccurredAtUtc = occurredAtUtc,
                WindowKey = winKey,
                TabKey = tabKey,
                Payload = new TabEventPayload
                {
                    Url = url,
                    Title = title,
                    IsWindowFocused = isFocused,
                    IsIncognito = false
                }
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }
    }
}
