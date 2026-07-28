using System;
using System.IO;
using System.Text;

namespace GlobalTranslator
{
    internal static class DiagnosticLog
    {
        private static readonly object Sync = new object();
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlobalTranslator");
        internal static readonly string FilePath = Path.Combine(Folder, "diagnostic.log");

        internal static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Folder);
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
    }
}
