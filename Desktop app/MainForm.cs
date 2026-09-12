using System.Net.Sockets;
namespace LocalMouse;
public sealed class MainForm : Form {
    private readonly ConnectionServer server;
    private readonly NotifyIcon tray;
    private readonly Icon appIcon=new(typeof(MainForm).Assembly.GetManifestResourceStream("ThumbPilot.AppIcon")!);
    private readonly ToolStripMenuItem trayStatus=new("Starting…") {Enabled=false};
    private readonly ToolStripMenuItem trayControl=new("Allow control") {CheckOnClick=true};
    private readonly System.Windows.Forms.Timer activationTimer=new() {Interval=250};
    private bool exiting;
    private bool trayTipShown;
    public void OpenWindow() { Show(); WindowState=FormWindowState.Normal; ShowInTaskbar=true; Activate(); }
    public void ExitCompanion() { exiting=true; Close(); }
    internal void WatchActivation(EventWaitHandle signal) {
        activationTimer.Tick += (_,_) => { if(signal.WaitOne(0)) OpenWindow(); };
        activationTimer.Start();
    }
    private void HideToTray() {
        Hide();
        if(!trayTipShown) { trayTipShown=true; tray.ShowBalloonTip(2500,"ThumbPilot is still running","Open or exit ThumbPilot from its icon near the clock.",ToolTipIcon.Info); }
    }
    private readonly Label status=new() {AutoSize=true,Text="Starting…",MaximumSize=new Size(590,0)};
    private readonly CheckBox allow=new() {Text="Allow control from paired phones",AutoSize=true};
    private DiscoveryServer? discovery;
    private readonly CancellationTokenSource closing=new();
    public MainForm(ConnectionServer? serverOverride=null, bool startHidden=false, bool enableDiscovery=true) {
        server=serverOverride ?? new(new WindowsMouseInput(),keyboard:new WindowsKeyboardInput(),actions:new WindowsActions());
        var menu=new ContextMenuStrip();
        menu.Items.Add("Open ThumbPilot",null,(_,_)=>OpenWindow());
        menu.Items.Add(trayStatus);menu.Items.Add(new ToolStripSeparator());menu.Items.Add(trayControl);
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Exit",null,(_,_)=>ExitCompanion());
        tray=new NotifyIcon {Icon=appIcon,Text="ThumbPilot · Starting",ContextMenuStrip=menu,Visible=true};
        tray.DoubleClick += (_,_)=>OpenWindow();
        tray.MouseClick += (_,e)=> {if(e.Button==MouseButtons.Left) OpenWindow();};
        tray.BalloonTipClicked += (_,_)=>OpenWindow();
        trayControl.CheckedChanged += (_,_)=>allow.Checked=trayControl.Checked;
        if(startHidden) {Opacity=0;ShowInTaskbar=false;}
        Icon=appIcon;Text="ThumbPilot";ClientSize=new Size(650,460);MinimumSize=new Size(540,400);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",11);
        var layout=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(24),AutoScroll=true};
        layout.Controls.Add(new Label {Text="ThumbPilot",AutoSize=true,Font=new Font(Font.FontFamily,22,FontStyle.Bold)});
        layout.Controls.Add(new Label {Text="Open ThumbPilot on your phone. Pair from either device.",AutoSize=true});
        layout.Controls.Add(status);
        var find=new Button {Text="Find phones",AutoSize=true};
        find.Click += async (_,_) => {
            find.Enabled=false;status.Text="Finding phones…";
            try {
                var phones=await PhoneDiscovery.Scan(closing.Token);
                if(IsDisposed) return;
                if(phones.Count==0) {status.Text="No phones found. Keep the mobile app open on the same Wi-Fi.";return;}
                using var dialog=new Form {Text="Choose a phone to pair",Width=440,Height=300,StartPosition=FormStartPosition.CenterParent};
                var list=new ListBox {Dock=DockStyle.Fill}; foreach(var phone in phones) list.Items.Add(phone.Name);
                var pair=new Button {Text="Pair",Dock=DockStyle.Bottom,Height=40};
                NearbyPhone? selected=null;
                pair.Click += (_,_) => {if(list.SelectedIndex>=0) {selected=phones[list.SelectedIndex];dialog.Close();}};
                dialog.Controls.Add(list);dialog.Controls.Add(pair);dialog.ShowDialog(this);
                if(selected!=null) {await PhoneDiscovery.Invite(selected,server.Security.Fingerprint,server.Invitations.Create(selected.PublicKey));status.Text="Waiting for approval on "+selected.Name+"…";}
            } catch(OperationCanceledException) {} catch(Exception ex) {if(!IsDisposed) status.Text="Discovery failed: "+ex.Message;}
            finally {if(!IsDisposed) find.Enabled=true;}
        };
        layout.Controls.Add(find);
        var devices=new Button {Text="Paired phones / Unpair",AutoSize=true};
        devices.Click += (_,_) => {
            using var dialog=new Form {Text="Paired phones",Width=470,Height=300,StartPosition=FormStartPosition.CenterParent};
            var list=new ListBox {Dock=DockStyle.Fill};var phones=server.Security.Phones;
            foreach(var phone in phones) list.Items.Add(phone.Name);
            var remove=new Button {Text="Unpair selected phone",Dock=DockStyle.Bottom,Height=40};
            remove.Click += (_,_) => {if(list.SelectedIndex>=0) {try {server.Revoke(phones[list.SelectedIndex].Hash);dialog.Close();} catch(Exception ex) {MessageBox.Show(dialog,ex.Message,"Could not unpair");}}};
            dialog.Controls.Add(list);dialog.Controls.Add(remove);dialog.ShowDialog(this);
        };
        layout.Controls.Add(devices);layout.Controls.Add(allow);
        allow.CheckedChanged += (_,_)=> { server.SetInputEnabled(allow.Checked); trayControl.Checked=allow.Checked; };
        var startup=new CheckBox {Text="Start with Windows (in the system tray)",AutoSize=true};
        try {startup.Checked=StartupSettings.Enabled;} catch(Exception ex) when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) {status.Text="Startup setting unavailable: "+ex.Message;}
        startup.CheckedChanged += (_,_)=> {
            try {StartupSettings.Set(startup.Checked);}
            catch(Exception ex) when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) {
                MessageBox.Show(this,"Could not save the startup setting: "+ex.Message,"Startup setting");
                // Restore the displayed state without retrying a failed registry write.
                startup.Enabled=false;
            }
        };
        layout.Controls.Add(startup);
        layout.Controls.Add(new Label {Text="Closing this window keeps ThumbPilot running in the system tray.\nRight-click its tray icon and choose Exit to stop it.",AutoSize=true,MaximumSize=new Size(580,0)});
        layout.Controls.Add(new Label {Text="Developed by Deboraj · 1.0.1",AutoSize=true,ForeColor=Color.DimGray});
        layout.Controls.Add(new Label {Text="Saved phones reconnect automatically. No IP address or code needed.",MaximumSize=new Size(580,0),AutoSize=true});
        Controls.Add(layout);
        server.RequestApproval=Approve;
        server.StatusChanged += message => {
            if(exiting || IsDisposed || !IsHandleCreated) return;
            try {BeginInvoke((Action)(()=> {
                if(exiting || IsDisposed) return;
                status.Text=message;allow.Checked=server.InputEnabled;trayControl.Checked=allow.Checked;
                trayStatus.Text=message;
                tray.Text="ThumbPilot · "+(message.StartsWith("Connected") ? "Connected" : "Ready");
            }));} catch(InvalidOperationException) { }
        };
        Shown += (_,_) => {try {server.Start();allow.Checked=server.InputEnabled;try {if(enableDiscovery) discovery=new DiscoveryServer(server.Security.Fingerprint);} catch(SocketException) {status.Text="Discovery unavailable. Close any older companion and reopen this app.";}} catch(SocketException ex) {status.Text="Could not start. Close the older companion. "+ex.Message;}};
        Shown += (_,_)=> {if(startHidden) {Hide();Opacity=1;}};
        Resize += (_,_)=> {if(WindowState==FormWindowState.Minimized) HideToTray();};
        FormClosing += (_,e)=> {
            if(e.CloseReason==CloseReason.UserClosing && !exiting) { e.Cancel=true;HideToTray();return; }
            exiting=true;activationTimer.Stop();closing.Cancel();discovery?.Dispose();server.Stop();tray.Visible=false;
        };
        FormClosed += (_,_)=> {activationTimer.Dispose();tray.Dispose();menu.Dispose();appIcon.Dispose();closing.Dispose();};
    }
    private Task<bool> Approve(PairRequest request,CancellationToken token) {
        var result=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(IsDisposed || token.IsCancellationRequested) return Task.FromResult(false);
        BeginInvoke((Action)(()=> {
            if(exiting || IsDisposed || token.IsCancellationRequested) {result.TrySetResult(false);return;}
            OpenWindow();
            var dialog=new Form {Text="Pairing request",Width=460,Height=220,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
            var message=new Label {Text=request.Name+" wants to pair and control this PC.\nApprove only a request you expect.",Dock=DockStyle.Fill,Padding=new Padding(20)};
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=50};
            var approve=new Button {Text="Allow",AutoSize=true};var deny=new Button {Text="Decline",AutoSize=true};
            approve.Click += (_,_)=> {result.TrySetResult(!token.IsCancellationRequested);dialog.Close();};
            deny.Click += (_,_)=>dialog.Close();
            buttons.Controls.Add(approve);buttons.Controls.Add(deny);dialog.Controls.Add(message);dialog.Controls.Add(buttons);
            _ = dialog.Handle;
            var registration=token.Register(()=> {result.TrySetResult(false);if(!dialog.IsDisposed && dialog.IsHandleCreated) {try {dialog.BeginInvoke((Action)(()=>dialog.Close()));} catch(InvalidOperationException) {}}});
            dialog.FormClosed += (_,_)=> {registration.Dispose();result.TrySetResult(false);dialog.Dispose();};
            dialog.Show(this);dialog.Activate();
        }));
        return result.Task;
    }
}
