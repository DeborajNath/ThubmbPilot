using Microsoft.Win32;
namespace LocalMouse;

internal static class StartupSettings {
    private const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName="LocalMouse";
    internal static string Command => "\""+Environment.ProcessPath+"\" --background";
    internal static bool Enabled {
        get { using var key=Registry.CurrentUser.OpenSubKey(KeyPath); return string.Equals(key?.GetValue(ValueName) as string,Command,StringComparison.OrdinalIgnoreCase); }
    }
    internal static void Set(bool enabled) {
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath,true);
        if(enabled) key.SetValue(ValueName,Command,RegistryValueKind.String);
        else key.DeleteValue(ValueName,false);
    }
}
