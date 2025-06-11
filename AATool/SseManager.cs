using System;

namespace AATool
{
    public static class SseManager
    {
        public static SseServer Instance { get; } = new SseServer();

        public static void Start()
        {
            Instance.Start("http://127.0.0.1:5974/sse/");
        }

        public static void Broadcast(string json)
        {
            Instance.PushUpdate(json);
        }
    }

}