using System.Reflection;

// Bump the version here; it shows up in the Settings window and in the exe's file properties.
[assembly: AssemblyTitle("FlatClip")]
[assembly: AssemblyProduct("FlatClip")]
[assembly: AssemblyDescription("Clipboard manager")]
[assembly: AssemblyCopyright("Copyright © Jeroen Idserda")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace FlatClip
{
    static class AppInfo
    {
        public static readonly string Version =
            typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AppInfo).Assembly.GetName().Version.ToString(3);
    }
}
