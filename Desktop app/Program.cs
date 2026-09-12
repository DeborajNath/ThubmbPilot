namespace LocalMouse;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--runtime-check") {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"runtime-check.txt"),System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()+Environment.NewLine+typeof(Form).Assembly.Location);
            return;
        }
        ApplicationConfiguration.Initialize();
        try {
            var user=System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var suffix=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user)))[..24];
            using var instance=new Mutex(true,@"Local\LocalMouse-"+suffix,out var first);
            using var activate=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\LocalMouse-Open-"+suffix);
            if(!first) {if(!args.Contains("--background")) activate.Set();return;}
            try {
                using var form=new MainForm(startHidden:args.Contains("--background"));
                form.WatchActivation(activate);
                Application.Run(form);
            } finally {instance.ReleaseMutex();}
        }
        catch(Exception ex) when(ex is System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) {
            MessageBox.Show("ThumbPilot could not open its secure identity for this Windows account.\n"+ex.Message+"\nUse your normal Windows login. Existing pairings have not been reset.","ThumbPilot",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}