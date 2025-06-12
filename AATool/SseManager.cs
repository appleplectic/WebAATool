using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace AATool
{
    public static class SseManager
    {
        public static SseServer Instance { get; } = new SseServer();

        private static void startLocaltunnel(string port)
        {
            Console.WriteLine("Starting localtunnel process at port " + port);
            string resourceName = "AATool.lt.exe";
            string tempPath = Path.Combine(Path.GetTempPath(), "lt.exe");

            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            using (FileStream file = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                resource.CopyTo(file);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "--port " + port,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Verb = "runas"
            };

            Process process = new Process { StartInfo = psi };
            
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Console.WriteLine(e.Data);
                    var match = Regex.Match(e.Data, @"https:\/\/[^\s]+\.loca\.lt");
                    if (match.Success)
                        Process.Start("https://appleplectic.github.io/WebAATool-website/?server=" + match.Value);
                }
            };

            process.ErrorDataReceived += (s, e) => Console.Error.WriteLine(e.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            new Thread(() =>
                {
                    process.WaitForExit();
                    Console.WriteLine("Localtunnel process exited.");
                }
            )
            {
                IsBackground = true
            }.Start();
        }

        public static void Start()
        {
            string port = "5974";
            Instance.Start("http://127.0.0.1:" + port + "/"); 
            System.Threading.Thread.Sleep(1000);
            startLocaltunnel(port);
        }

        public static void Broadcast(string json)
        {
            Instance.PushUpdate(json);
        }
    }

}