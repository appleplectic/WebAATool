using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

namespace AATool
{
    public class SseServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly ConcurrentBag<HttpListenerResponse> _clients = new ConcurrentBag<HttpListenerResponse>();
        private string _latestJson;

        public void Start(string urlPrefix)
        {
            _listener.Prefixes.Add(urlPrefix);
            _listener.Start();
            Log("INFO", "SSE Server started and listening on " + urlPrefix);

            ThreadPool.QueueUserWorkItem(_ => ListenLoop());
        }

        private async void ListenLoop()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.Url.AbsolutePath == "/sse")
                    {
                        Log("INFO", $"Client connected: {context.Request.RemoteEndPoint}");

                        var response = context.Response;
                        response.ContentType = "text/event-stream";
                        response.Headers.Add("Cache-Control", "no-cache");
                        response.Headers.Add("Access-Control-Allow-Origin", "*");
                        response.Headers.Add("Access-Control-Allow-Headers", "*");
                        response.SendChunked = true;

                        _clients.Add(response);
                        Log("INFO", $"Total connected clients: {_clients.Count}");

                        if (!string.IsNullOrEmpty(_latestJson))
                        {
                            var initialMessage = $"data: {_latestJson}\n\n";
                            var bytes = Encoding.UTF8.GetBytes(initialMessage);
                            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            await response.OutputStream.FlushAsync();
                            Log("DEBUG", "Sent initial JSON to new client.");
                        }

                        _ = KeepAlivePing(response);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                        Log("WARN", $"Rejected request to unsupported path: {context.Request.Url.AbsolutePath}");
                    }
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

        private async System.Threading.Tasks.Task KeepAlivePing(HttpListenerResponse response)
        {
            try
            {
                while (true)
                {
                    byte[] pingBytes = Encoding.UTF8.GetBytes(": ping\n\n");
                    await response.OutputStream.WriteAsync(pingBytes, 0, pingBytes.Length);
                    await response.OutputStream.FlushAsync();
                    Log("DEBUG", "Sent keep-alive ping.");
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30));
                }
            }
            catch (Exception ex)
            {
                response.Close();
                _clients.TryTake(out _);
                Log("INFO", $"Client disconnected during keep-alive. Reason: {ex.Message}");
                Log("INFO", $"Remaining connected clients: {_clients.Count}");
            }
        }

        public void PushUpdate(string json)
        {
            _latestJson = json;

            var message = $"data: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(message);

            Log("INFO", "Pushing update to clients...");

            foreach (var client in _clients)
            {
                try
                {
                    client.OutputStream.Write(bytes, 0, bytes.Length);
                    client.OutputStream.Flush();
                }
                catch (Exception ex)
                {
                    client.Close();
                    _clients.TryTake(out _);
                    Log("WARN", $"Removed unresponsive client. Reason: {ex.Message}");
                }
            }

            Log("INFO", $"Update pushed to clients. Total remaining: {_clients.Count}");
        }

        private void Log(string level, string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
        }
    }
}
