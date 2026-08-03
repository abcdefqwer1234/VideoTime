using System;
using System.IO;
using System.Text;

namespace VideoTime
{
    public enum LogLevel
    {
        Off = -1,
        Error = 0,
        Warning = 1,
        Info = 2
    }

    public static class Log
    {
        private static readonly object _logLock = new object();
        private static LogLevel _cachedLevel = LogLevel.Info;
        private static bool _cacheValid;

        public static string LogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt"); }
        }

        public static void Append(string line, LogLevel level = LogLevel.Info)
        {
            if (!IsLevelEnabled(level)) return;
            try
            {
                string tag = level == LogLevel.Error ? "错误" : (level == LogLevel.Warning ? "警告" : "信息");
                lock (_logLock)
                {
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + tag + "] " + line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void InvalidateLevelCache()
        {
            lock (_logLock)
            {
                _cacheValid = false;
            }
        }

        public static bool IsLevelEnabled(LogLevel level)
        {
            LogLevel cfg;
            lock (_logLock)
            {
                if (!_cacheValid)
                {
                    try
                    {
                        _cachedLevel = Properties.Settings.Default.LogOutputLevel;
                    }
                    catch { _cachedLevel = LogLevel.Info; }
                    _cacheValid = true;
                }
                cfg = _cachedLevel;
            }
            if (cfg == LogLevel.Off) return false;
            return (int)level <= (int)cfg;
        }
    }
}
