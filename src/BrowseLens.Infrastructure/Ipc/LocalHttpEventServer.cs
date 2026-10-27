using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BrowseLens.Core.Models;

namespace BrowseLens.Infrastructure.Ipc
{
    public class LocalHttpEventServer
    {
        private readonly int _port;
        private readonly string _authToken;
        private TcpListener? _listener;
        private Thread? _listenerThread;
        private volatile bool _isRunning;
        private readonly HashSet<string> _allowedEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Hello",
            "Heartbeat",
            "TabActivated",
            "TabUpdated",
            "NavigationCommitted",
            "TabRemoved",
            "WindowFocusChanged",
            "BrowserStateChanged",
            "TrackingStateChanged"
        };

        public int Port { get { return _port; } }
        public string AuthToken { get { return _authToken; } }
        public bool IsRunning { get { return _isRunning; } }

        public event Action<BrowserEventEnvelope>? EventReceived;
        public event Action<string>? ServerLog;

        public LocalHttpEventServer(int port = 58432, string? customToken = null)
        {
            _port = port;
            if (!string.IsNullOrEmpty(customToken))
            {
                _authToken = customToken;
            }
            else
            {
                byte[] randomBytes = new byte[32];
                RandomNumberGenerator.Fill(randomBytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in randomBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                _authToken = sb.ToString();
            }
        }

        public void Start()
        {
            if (_isRunning) return;

            // Strict loopback binding: 127.0.0.1 only
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _isRunning = true;

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "BrowseLens_HttpListener"
            };
            _listenerThread.Start();

            Log(string.Format("Local HTTP Event Server started strictly on 127.0.0.1:{0}", _port));
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                }
            }
            catch { }

            Log("Local HTTP Event Server stopped.");
        }

        private void ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    if (_listener == null) break;
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        if (state is TcpClient tcpClient)
                        {
                            HandleClient(tcpClient);
                        }
                    }, client);
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    stream.ReadTimeout = 4000;
                    stream.WriteTimeout = 4000;

                    // Read HTTP request header
                    byte[] buffer = new byte[4096];
                    using (var ms = new MemoryStream())
                    {
                        int bytesRead;
                        int headerEndPos = -1;

                        while (ms.Length < 65536 && (bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ms.Write(buffer, 0, bytesRead);
                            byte[] data = ms.GetBuffer();
                            int len = (int)ms.Length;

                            for (int i = 0; i <= len - 4; i++)
                            {
                                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                                {
                                    headerEndPos = i;
                                    break;
                                }
                            }
                            if (headerEndPos >= 0) break;
                        }

                        if (headerEndPos < 0) return;

                        byte[] allBytes = ms.ToArray();
                        string headerText = Encoding.ASCII.GetString(allBytes, 0, headerEndPos);
                        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                        if (lines.Length == 0) return;

                        string[] reqParts = lines[0].Split(' ');
                        if (reqParts.Length < 3) return;

                        string method = reqParts[0].ToUpperInvariant();
                        string path = reqParts[1];

                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 1; i < lines.Length; i++)
                        {
                            int colon = lines[i].IndexOf(':');
                            if (colon > 0)
                            {
                                headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                            }
                        }

                        if (method == "OPTIONS")
                        {
                            SendCorsResponse(stream, 204, "No Content");
                            return;
                        }

                        if (method != "POST" || !path.Equals("/api/v1/events", StringComparison.OrdinalIgnoreCase))
                        {
                            SendJsonResponse(stream, 404, "Not Found", "{\"error\":\"Not Found\"}");
                            return;
                        }

                        string incomingToken = "";
                        if (headers.ContainsKey("X-BrowseLens-Token"))
                        {
                            incomingToken = headers["X-BrowseLens-Token"];
                        }
                        else if (headers.ContainsKey("Authorization") && headers["Authorization"].StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            incomingToken = headers["Authorization"].Substring(7).Trim();
                        }

                        if (!SecureEquals(incomingToken, _authToken))
                        {
                            Log("Rejected unauthenticated event request (invalid or missing token)");
                            SendJsonResponse(stream, 401, "Unauthorized", "{\"schemaVersion\":1,\"accepted\":false,\"errorCode\":\"UNAUTHENTICATED\"}");
                            return;
                        }

                        int contentLength = 0;
                        if (headers.ContainsKey("Content-Length"))
                        {
                            int.TryParse(headers["Content-Length"], out contentLength);
                        }

                        if (contentLength <= 0 || contentLength > 65536)
                        {
                            Log(string.Format("Rejected payload with invalid size: {0} bytes", contentLength));
                            SendJsonResponse(stream, 413, "Payload Too Large", "{\"schemaVersion\":1,\"accepted\":false,\"errorCode\":\"PAYLOAD_TOO_LARGE\"}");
                            return;
                        }

                        // The body bytes already in memory after \r\n\r\n
                        int bodyBytesAlreadyRead = allBytes.Length - (headerEndPos + 4);
                        byte[] bodyBytes = new byte[contentLength];
                        if (bodyBytesAlreadyRead > 0)
                        {
                            int toCopy = Math.Min(bodyBytesAlreadyRead, contentLength);
                            Array.Copy(allBytes, headerEndPos + 4, bodyBytes, 0, toCopy);
                        }

                        int totalBodyRead = Math.Min(bodyBytesAlreadyRead, contentLength);
                        while (totalBodyRead < contentLength)
                        {
                            int r = stream.Read(bodyBytes, totalBodyRead, contentLength - totalBodyRead);
                            if (r <= 0) break;
                            totalBodyRead += r;
                        }

                        string body = Encoding.UTF8.GetString(bodyBytes, 0, totalBodyRead);

                        string errorReason;
                        BrowserEventEnvelope envelope = ParseAndValidateJson(body, out errorReason);
                        if (envelope == null)
                        {
                            Log(string.Format("Rejected malformed event payload: {0}", errorReason));
                            SendJsonResponse(stream, 400, "Bad Request", string.Format("{{\"schemaVersion\":1,\"accepted\":false,\"errorCode\":\"INVALID_PAYLOAD\",\"detail\":\"{0}\"}}", errorReason));
                            return;
                        }

                        // Dispatch to event handler
                        if (EventReceived != null)
                        {
                            EventReceived(envelope);
                        }

                        SendJsonResponse(stream, 200, "OK", string.Format("{{\"schemaVersion\":1,\"eventId\":\"{0}\",\"accepted\":true}}", envelope.EventId));
                    }
                }
                catch (Exception ex)
                {
                    Log(string.Format("Error handling client request: {0}", ex.Message));
                }
            }
        }

        public BrowserEventEnvelope? ParseAndValidateJson(string json, out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "Empty body";
                return null;
            }

            try
            {
                var envelope = new BrowserEventEnvelope();

                // Simple robust key-value / token extraction without external dependencies
                int schemaVer = ExtractInt(json, "schemaVersion", 0);
                if (schemaVer != 1)
                {
                    error = "Unsupported schemaVersion (must be 1)";
                    return null;
                }
                envelope.SchemaVersion = schemaVer;

                string eventId = ExtractString(json, "eventId");
                if (string.IsNullOrEmpty(eventId))
                {
                    error = "Missing eventId";
                    return null;
                }
                envelope.EventId = eventId;

                envelope.Sequence = ExtractLong(json, "sequence", 0);
                envelope.Browser = ExtractString(json, "browser") ?? "Chrome";

                string eventType = ExtractString(json, "eventType");
                if (string.IsNullOrEmpty(eventType) || !_allowedEventTypes.Contains(eventType))
                {
                    error = "Unsupported or missing eventType";
                    return null;
                }
                envelope.EventType = eventType;

                string timeStr = ExtractString(json, "occurredAtUtc");
                DateTime dt;
                if (!string.IsNullOrEmpty(timeStr) && DateTime.TryParse(timeStr, out dt))
                {
                    envelope.OccurredAtUtc = dt.ToUniversalTime();
                }
                else
                {
                    envelope.OccurredAtUtc = DateTime.UtcNow;
                }

                envelope.WindowKey = ExtractString(json, "windowKey") ?? "w-0";
                envelope.TabKey = ExtractString(json, "tabKey") ?? "t-0";

                // Payload sub-object
                string payloadSection = ExtractObject(json, "payload");
                var payload = new TabEventPayload();
                if (!string.IsNullOrEmpty(payloadSection))
                {
                    payload.Url = ExtractString(payloadSection, "url") ?? string.Empty;
                    payload.Title = ExtractString(payloadSection, "title") ?? string.Empty;
                    payload.IsWindowFocused = ExtractBool(payloadSection, "isWindowFocused", true);
                    payload.IsIncognito = ExtractBool(payloadSection, "isIncognito", false);
                }
                envelope.Payload = payload;

                return envelope;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= (a[i] ^ b[i]);
            }
            return result == 0;
        }

        private void SendJsonResponse(NetworkStream stream, int statusCode, string statusText, string jsonBody)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            string responseHeader = string.Format(
                "HTTP/1.1 {0} {1}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                "Content-Length: {2}\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Headers: Content-Type, X-BrowseLens-Token, Authorization\r\n" +
                "Access-Control-Allow-Methods: POST, OPTIONS\r\n" +
                "Connection: close\r\n\r\n",
                statusCode, statusText, bodyBytes.Length);

            byte[] headerBytes = Encoding.ASCII.GetBytes(responseHeader);
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }
            stream.Flush();
        }

        private void SendCorsResponse(NetworkStream stream, int statusCode, string statusText)
        {
            string responseHeader = string.Format(
                "HTTP/1.1 {0} {1}\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Headers: Content-Type, X-BrowseLens-Token, Authorization\r\n" +
                "Access-Control-Allow-Methods: POST, OPTIONS\r\n" +
                "Content-Length: 0\r\n" +
                "Connection: close\r\n\r\n",
                statusCode, statusText);

            byte[] headerBytes = Encoding.ASCII.GetBytes(responseHeader);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Flush();
        }

        private void Log(string message)
        {
            if (ServerLog != null)
            {
                ServerLog(message);
            }
        }

        public static string ExtractString(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            int colonIdx = json.IndexOf(':', idx + pattern.Length);
            if (colonIdx < 0) return null;

            int firstQuote = json.IndexOf('"', colonIdx + 1);
            if (firstQuote < 0) return null;

            int nextQuote = json.IndexOf('"', firstQuote + 1);
            while (nextQuote > 0 && json[nextQuote - 1] == '\\')
            {
                nextQuote = json.IndexOf('"', nextQuote + 1);
            }
            if (nextQuote < 0) return null;

            return json.Substring(firstQuote + 1, nextQuote - firstQuote - 1);
        }

        public static int ExtractInt(string json, string key, int defaultValue)
        {
            string s = ExtractPrimitive(json, key);
            int val;
            if (int.TryParse(s, out val)) return val;
            return defaultValue;
        }

        public static long ExtractLong(string json, string key, long defaultValue)
        {
            string s = ExtractPrimitive(json, key);
            long val;
            if (long.TryParse(s, out val)) return val;
            return defaultValue;
        }

        public static bool ExtractBool(string json, string key, bool defaultValue)
        {
            string s = ExtractPrimitive(json, key);
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return defaultValue;
        }

        public static string ExtractObject(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            int openBrace = json.IndexOf('{', idx + pattern.Length);
            if (openBrace < 0) return null;

            int depth = 1;
            for (int i = openBrace + 1; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(openBrace, i - openBrace + 1);
                    }
                }
            }
            return null;
        }

        private static string ExtractPrimitive(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            int colonIdx = json.IndexOf(':', idx + pattern.Length);
            if (colonIdx < 0) return null;

            int i = colonIdx + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;

            int start = i;
            while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && !char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            if (start < json.Length)
            {
                return json.Substring(start, i - start).Trim().Trim('"');
            }
            return null;
        }
    }
}

