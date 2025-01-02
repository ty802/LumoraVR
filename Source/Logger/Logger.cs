using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Environment = System.Environment;

namespace Aquamarine.Source.Logging
{
    public static class Logger
    {
        private static readonly string LogDirectory;
        private static readonly string LogFile;
        private static readonly ConcurrentQueue<string> _logQueue = new();
        private static readonly AutoResetEvent _logEvent = new(false);
        private static readonly CancellationTokenSource _cancellationTokenSource = new();

        static Logger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string gameName = "Aquamarine"; // temp solution
            LogDirectory = Path.Combine(appData, gameName, "logs");

            // Ensure the directory exists
            if (!Directory.Exists(LogDirectory))
                Directory.CreateDirectory(LogDirectory);

            LogFile = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");

            // Start the background logging task
            Task.Run(() => ProcessLogQueue(_cancellationTokenSource.Token));
        }

        private static void WriteLog(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logEntry = $"[{timestamp}] [{level}] {message}";
            _logQueue.Enqueue(logEntry);
            _logEvent.Set();
        }

        private static void ProcessLogQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logEvent.WaitOne(TimeSpan.FromSeconds(10)); // Wait for either a timeout or a new log entry
                    FlushLogQueue();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Logger encountered an error: {ex.Message}");
                }
            }
        }

        private static void FlushLogQueue()
        {
            if (_logQueue.IsEmpty) return;

            try
            {
                using var writer = new StreamWriter(LogFile, append: true);
                while (_logQueue.TryDequeue(out var logEntry))
                {
                    writer.WriteLine(logEntry);
                }
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Failed to write logs to file: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            _cancellationTokenSource.Cancel();
            FlushLogQueue(); // Ensure any remaining logs are written
        }

        public static void Log(string message)
        {
            WriteLog("LOG", message);
        }

        public static void Warn(string message)
        {
            WriteLog("WARN", message);
        }

        public static void Error(string message)
        {
            WriteLog("ERROR", message);
        }
    }
}
