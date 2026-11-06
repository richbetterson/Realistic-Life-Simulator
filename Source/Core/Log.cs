// Realistic Life Simulator - lightweight buffered log file.

using System;
using System.IO;

namespace RealisticLifeSimulator.Core
{
    public static class Log
    {
        private const long MaxBytes = 1024 * 1024; // rotate at 1 MB
        private static readonly object Sync = new object();
        private static StreamWriter writer;
        private static bool debugEnabled;

        public static bool DebugEnabled
        {
            get { return debugEnabled; }
            set { debugEnabled = value; }
        }

        public static void Init(string file)
        {
            lock (Sync)
            {
                CloseInternal();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    FileInfo fi = new FileInfo(file);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = file + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                    }
                    writer = new StreamWriter(file, true, FileUtil.Utf8NoBom);
                    writer.AutoFlush = false;
                }
                catch (Exception)
                {
                    writer = null; // logging must never break the mod
                }
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Debug(string message) { if (debugEnabled) Write("DEBUG", message); }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", ex == null ? message : message + " :: " + ex);
        }

        private static void Write(string level, string message)
        {
            lock (Sync)
            {
                if (writer == null) return;
                try
                {
                    writer.WriteLine("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}", DateTime.Now, level, message);
                    if (level != "INFO" && level != "DEBUG") writer.Flush();
                }
                catch (Exception) { }
            }
        }

        public static void Flush()
        {
            lock (Sync)
            {
                if (writer == null) return;
                try { writer.Flush(); } catch (Exception) { }
            }
        }

        public static void Close()
        {
            lock (Sync) { CloseInternal(); }
        }

        private static void CloseInternal()
        {
            if (writer == null) return;
            try { writer.Flush(); writer.Dispose(); } catch (Exception) { }
            writer = null;
        }
    }
}
