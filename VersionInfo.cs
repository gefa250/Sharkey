using System.Reflection;

[assembly: AssemblyTitle("鲨译 Sharkey")]
[assembly: AssemblyDescription("Windows 全局翻译与截图 OCR 助手")]
[assembly: AssemblyCompany("Sharkey")]
[assembly: AssemblyProduct("Sharkey")]
[assembly: AssemblyCopyright("Copyright © Sharkey contributors")]
[assembly: AssemblyVersion(GlobalTranslator.VersionInfo.AssemblyVersion)]
[assembly: AssemblyFileVersion(GlobalTranslator.VersionInfo.FileVersion)]
[assembly: AssemblyInformationalVersion(
    GlobalTranslator.VersionInfo.SemanticVersion)]

namespace GlobalTranslator
{
    internal static class VersionInfo
    {
        public const string SemanticVersion = "0.2.0-dev";
        public const string AssemblyVersion = "0.2.0.0";
        public const string FileVersion = "0.2.0.0";
    }
}
