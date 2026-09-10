using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace GlobalTranslator
{
    internal static class DiagnosticLog
    {
        private static readonly object Sync = new object();
        internal static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlobalTranslator");
        internal static readonly string FilePath = Path.Combine(FolderPath, "diagnostic.log");

        internal static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(FolderPath);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 256 * 1024)
                        File.WriteAllText(FilePath, "", Encoding.UTF8);
                    File.AppendAllText(
                        FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Diagnostics must never affect translation.
            }
        }

        internal static void WriteStartup()
        {
            Write("Application starting; " + BuildEnvironmentLine());
        }

        internal static void WriteException(string context, Exception error)
        {
            Write((context ?? "Unhandled error") + "; " +
                  (error == null ? "no exception details" : error.ToString()));
        }

        internal static string BuildSupportSummary()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Sharkey 诊断信息");
            builder.AppendLine("版本: " + VersionInfo.SemanticVersion);
            builder.AppendLine("文件版本: " +
                Assembly.GetExecutingAssembly().GetName().Version);
            builder.AppendLine("系统: " + Environment.OSVersion.VersionString);
            builder.AppendLine("进程: " +
                (Environment.Is64BitProcess ? "x64" : "x86"));
            builder.AppendLine("系统架构: " +
                (Environment.Is64BitOperatingSystem ? "x64" : "x86"));
            builder.AppendLine("CLR: " + Environment.Version);
            builder.AppendLine("日志: " + FilePath);
            builder.Append("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return builder.ToString();
        }

        private static string BuildEnvironmentLine()
        {
            return "version=" + VersionInfo.SemanticVersion +
                   "; os=" + Environment.OSVersion.VersionString +
                   "; process=" + (Environment.Is64BitProcess ? "x64" : "x86") +
                   "; osArch=" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") +
                   "; clr=" + Environment.Version;
        }
    }
}
