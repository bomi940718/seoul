using System;
using System.IO;
using System.Text;

namespace WorkReport.AddIn.Services
{
    /// <summary>%LOCALAPPDATA%\WorkReport\logs\yyyyMMdd.log — 애드인 전 과정 기록.</summary>
    public static class Logger
    {
        private static readonly object Sync = new object();

        public static string LogDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkReport", "logs");

        public static string TodayLogPath =>
            Path.Combine(LogDir, DateTime.Now.ToString("yyyyMMdd") + ".log");

        public static void Info(string message) => Write("INFO ", message);
        public static void Warn(string message) => Write("WARN ", message);

        public static void Error(string message, Exception ex = null)
            => Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDir);
                    // BOM 포함: 메모장 등에서 한글 로그가 CP949로 오인되지 않도록 (신규 파일 생성 시에만 기록됨)
                    File.AppendAllText(TodayLogPath,
                        $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                        new UTF8Encoding(true));
                }
            }
            catch
            {
                // 로그 실패가 기능을 막아서는 안 됨
            }
        }
    }
}
