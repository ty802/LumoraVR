using System;
using System.IO;

namespace Aquamarine.Source.Logging
{
    public static class Logger
    {
        private static readonly string LogDirectory;
        private static readonly string LogFile;

        static Logger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string gameName = "Aquamarine";//temp solution
            LogDirectory = Path.Combine(appData, gameName, "logs");

            // Ensure the directory exists pls
            if (!Directory.Exists(LogDirectory))
                Directory.CreateDirectory(LogDirectory);

            // Define log file with a timestamp
            LogFile = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logEntry = $"[{timestamp}] [{level}] {message}";
                File.AppendAllText(LogFile, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                //this is not really needed but i decided we might need a fallback
                Console.Error.WriteLine($"Failed to write log: {ex.Message}");
            }
        }

        /*
        Example Usage:
        Logger.Log("This is a log message.");
        Logger.Warn("This is a warning message.");
        Logger.Error("This is an error message.");
        */
        //logging functions
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
