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
            _listener.Prefixes.Add(urlPrefix); // e.g., "http://localhost:8080/sse/"
            _listener.Start();
            Console.WriteLine("SSE Server listening...");

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
                        Console.WriteLine("Client connected.");

                        var response = context.Response;
                        response.ContentType = "text/event-stream";
                        response.Headers.Add("Cache-Control", "no-cache");
                        response.Headers.Add("Access-Control-Allow-Origin", "*");
                        response.SendChunked = true;

                        _clients.Add(response);
                        
                        if (!string.IsNullOrEmpty(_latestJson))
                        {
                            var initialMessage = $"data: {_latestJson}\n\n";
                            var bytes = Encoding.UTF8.GetBytes(initialMessage);
                            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            await response.OutputStream.FlushAsync();
                        }

                        // Keep the connection open
                        _ = KeepAlivePing(response);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException) { break; }
            }
        }

        private async System.Threading.Tasks.Task KeepAlivePing(HttpListenerResponse response)
        {
            try
            {
                while (true)
                {
                    await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(": ping\n\n"), 0, 9);
                    await response.OutputStream.FlushAsync();
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30));
                }
            }
            catch
            {
                response.Close();
                _clients.TryTake(out _); // remove client on disconnect
            }
        }

        public void PushUpdate(string json)
        {
            _latestJson = json;
            
            var message = $"data: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(message);

            foreach (var client in _clients)
            {
                try
                {
                    client.OutputStream.Write(bytes, 0, bytes.Length);
                    client.OutputStream.Flush();
                }
                catch
                {
                    client.Close();
                    _clients.TryTake(out _); // remove disconnected clients
                }
            }
        }
    }
}
