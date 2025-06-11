using System;

namespace AATool
{
    public static class SseManager
    {
        public static SseServer Instance { get; } = new SseServer();

        public static void Start()
        {
            Instance.Start("http://localhost:5974/sse/");
            Console.WriteLine("SSE Server started.");
        }

        public static void Broadcast(string json)
        {
            Console.WriteLine("Broadcasting...");
            Instance.PushUpdate(json);
        }
    }

}