using System;
using System.Reflection;
using Microsoft.Win32;

namespace GlobalTranslator
{
    internal static class StartupManager
    {
        private const string RunKey =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Sharkey";
        private const string LegacyValueName = "SharkTranslate";

        public static string CurrentCommand
        {
            get
            {
                string path =
                    Assembly.GetExecutingAssembly().Location;
                return "\"" + path.Replace("\"", "") + "\" --startup";
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key =
                    Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    string value = key.GetValue(ValueName) as string;
                    return string.Equals(
                        value,
                        CurrentCommand,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key =
                Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null)
                    throw new InvalidOperationException(
                        "无法访问当前用户的开机启动设置。");
                if (enabled)
                    key.SetValue(
                        ValueName,
                        CurrentCommand,
                        RegistryValueKind.String);
                else
                    key.DeleteValue(ValueName, false);
                key.DeleteValue(LegacyValueName, false);
            }
        }
    }
}
