using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace AATool
{
    public class SseServer
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
        private string _latestJson;

        public void Start(string urlPrefix)
        {
            _listener.Prefixes.Add(urlPrefix);
            _listener.Start();
            Log("INFO", "SSE Server started and listening on " + urlPrefix);

            _ = Task.Run(ListenLoop);
            _ = Task.Run(PingAndLogLoop);
        }

        private async Task ListenLoop()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    var response = context.Response;
                    response.ContentType = "text/event-stream";
                    response.Headers.Add("Cache-Control", "no-cache");
                    response.Headers.Add("Access-Control-Allow-Origin", "*");
                    response.Headers.Add("Access-Control-Allow-Headers", "*");
                    response.Headers.Add("Connection", "keep-alive");
                    response.SendChunked = true;

                    var clientId = Guid.NewGuid();
                    var connection = new ClientConnection(response, DateTime.UtcNow);
                    _clients.TryAdd(clientId, connection);
                    Log("INFO", $"Client connected: {context.Request.RemoteEndPoint}");

                    if (!string.IsNullOrEmpty(_latestJson))
                        await SendMessageAsync(response, _latestJson);
                }
                catch (HttpListenerException ex)
                {
                    Log("ERROR", $"HttpListenerException: {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Log("ERROR", $"Unexpected error in ListenLoop: {ex}");
                }
            }
        }

        private async Task PingAndLogLoop()
        {
            while (true)
            {
                foreach (var kvp in _clients)
                {
                    var id = kvp.Key;
                    var client = kvp.Value;

                    try
                    {
                        byte[] pingBytes = Encoding.UTF8.GetBytes(": ping\n\n");
                        await client.Response.OutputStream.WriteAsync(pingBytes, 0, pingBytes.Length);
                        await client.Response.OutputStream.FlushAsync();
                        client.LastActive = DateTime.UtcNow;
                    }
                    catch
                    {
                        client.Response.Close();
                        _clients.TryRemove(id, out _);
                        Log("INFO", "Client disconnected (ping failure).");
                    }
                }

                Log("INFO", $"Currently connected clients: {_clients.Count}");
                await Task.Delay(TimeSpan.FromSeconds(20));
            }
        }

        public void PushUpdate(string json)
        {
            _latestJson = json;
            var message = $"data: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(message);

            foreach (var kvp in _clients)
            {
                var id = kvp.Key;   
                var client = kvp.Value;

                try
                {
                    client.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    client.Response.OutputStream.Flush();
                }
                catch
                {
                    client.Response.Close();
                    _clients.TryRemove(id, out _);
                    Log("WARN", "Removed unresponsive client.");
                }
            }

            Log("INFO", $"Update pushed to {_clients.Count} clients.");
        }

        private async Task SendMessageAsync(HttpListenerResponse response, string json)
        {
            var data = $"data: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(data);
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            await response.OutputStream.FlushAsync();
        }

        private void Log(string level, string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
        }

        private class ClientConnection
        {
            public HttpListenerResponse Response { get; }
            public DateTime LastActive { get; set; }

            public ClientConnection(HttpListenerResponse response, DateTime connectedAt)
            {
                Response = response;
                LastActive = connectedAt;
            }
        }
    }
}
