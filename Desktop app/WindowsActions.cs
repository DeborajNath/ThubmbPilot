using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LocalMouse;
public sealed class WindowsActions : IPCActionInput {
    public void Execute(string action) {
        if (!ActionProtocol.Allowed.Contains(action)) throw new InvalidDataException("Unknown action");
        switch (action) {
            case "lock": if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error()); return;
            case "sleep":
                if (!Application.SetSuspendState(PowerState.Suspend,false,false)) throw new InvalidOperationException("Windows could not enter sleep.");
                return;
            case "restart": case "shutdown":
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"shutdown.exe")) { UseShellExecute=false,CreateNoWindow=true };
                info.ArgumentList.Add(action == "restart" ? "/r" : "/s");
                info.ArgumentList.Add("/t"); info.ArgumentList.Add("0");
                using (var process = Process.Start(info) ?? throw new InvalidOperationException("Unable to request power action")) {
                    if (process.WaitForExit(2000) && process.ExitCode != 0) throw new InvalidOperationException("Windows rejected the power action.");
                }
                return;
        }
        string path = action switch {
            "calculator" => Path.Combine(Environment.SystemDirectory,"calc.exe"),
            "notepad" => Path.Combine(Environment.SystemDirectory,"notepad.exe"),
            "explorer" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),
            "brave" => FindBrave(),
            _ => throw new InvalidDataException("Unknown application")
        };
        using var launched = Process.Start(new ProcessStartInfo(path) { UseShellExecute=true });
    }
    private static string FindBrave() {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }) {
            var path = Path.Combine(root,"BraveSoftware","Brave-Browser","Application","brave.exe");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Brave was not found in its standard install locations.");
    }
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
