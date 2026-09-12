using LocalMouse;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

internal static class Program {
    [STAThread] static int Main() {
        ApplicationConfiguration.Initialize();
        var directory=Path.Combine(AppContext.BaseDirectory,"test-identity");
        var key=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("isolated tray lifecycle test only"));
        byte[] Protect(byte[] data,bool encrypt) {
            using var aes=new AesGcm(key,16);
            if(encrypt) {var nonce=RandomNumberGenerator.GetBytes(12);var cipher=new byte[data.Length];var tag=new byte[16];aes.Encrypt(nonce,data,cipher,tag);return nonce.Concat(tag).Concat(cipher).ToArray();}
            var clear=new byte[data.Length-28];aes.Decrypt(data.AsSpan(0,12),data.AsSpan(28),data.AsSpan(12,16),clear);return clear;
        }
        var security=new SecurityStore(directory,protector:Protect,keyStorageFlags:X509KeyStorageFlags.EphemeralKeySet);
        var server=new ConnectionServer(new NoMouse(),port:0,bindAddress:IPAddress.Loopback,security:security);
        using var form=new MainForm(server,startHidden:true,enableDiscovery:false);
        using var timer=new System.Windows.Forms.Timer {Interval=200};
        Exception? failure=null;int step=0;
        void Check(bool condition,string message) {if(!condition) throw new Exception(message);}
        timer.Tick += (_,_)=> {
            try {
                if(step++==0) {
                    Check(!form.Visible && server.BoundPort>0,"Background startup must hide the form and keep listening");
                    form.Opacity=0;form.OpenWindow();
                    Check(form.Visible,"Open must restore the existing form");
                    form.Close();
                    Check(!form.IsDisposed && !form.Visible && server.BoundPort>0,"Closing the window must preserve listener and form");
                    form.OpenWindow();
                    Check(form.Visible && server.BoundPort>0,"Reopen after close must preserve server");
                } else {timer.Stop();form.ExitCompanion();}
            } catch(Exception ex) {failure=ex;timer.Stop();form.ExitCompanion();}
        };
        timer.Start();Application.Run(form);
        if(failure!=null) {Console.Error.WriteLine(failure);return 1;}
        if(server.BoundPort!=0 || !form.IsDisposed) {Console.Error.WriteLine("Exit failed to stop server/dispose form");return 1;}
        Console.WriteLine("PASS: hidden startup, reopen, close-to-tray preserves listener, explicit Exit stops server.");return 0;
    }
    private sealed class NoMouse:IMouseInput {
        public void Move(int x,int y) => throw new Exception("Unexpected input");
        public void Click(string button) => throw new Exception("Unexpected input");
        public void Scroll(int x,int y) => throw new Exception("Unexpected input");
        public void SetLeft(bool down) => throw new Exception("Unexpected input");
    }
}
