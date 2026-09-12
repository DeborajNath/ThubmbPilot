using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LocalMouse;

var mouse = new RecordingMouse();
var keyboard = new RecordingKeyboard();
var actions = new RecordingActions();
var directory=Path.Combine(Path.GetTempPath(),"LocalMouseTests-"+Guid.NewGuid().ToString("N"));
// Test account cannot use Windows DPAPI; inject authenticated encryption only in tests.
var testKey=RandomNumberGenerator.GetBytes(32);
byte[] TestProtect(byte[] data,bool encrypt) {
    using var aes=new AesGcm(testKey,16);
    if(encrypt) {
        var nonce=RandomNumberGenerator.GetBytes(12); var cipher=new byte[data.Length]; var tag=new byte[16];
        aes.Encrypt(nonce,data,cipher,tag); return nonce.Concat(tag).Concat(cipher).ToArray();
    }
    var clear=new byte[data.Length-28]; aes.Decrypt(data.AsSpan(0,12),data.AsSpan(28),data.AsSpan(12,16),clear);return clear;
}
var isolated=args.Contains("--storage-only");
var flags=isolated ? System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet : System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.UserKeySet;
Func<byte[],bool,byte[]>? protector=isolated ? TestProtect : null;
var security=new SecurityStore(directory,protector:protector,keyStorageFlags:flags);
var credential=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
if(!security.Approve(credential,"Test phone")) throw new Exception("Initial pairing failed");
if(isolated) {
    var restored=new SecurityStore(directory,protector:TestProtect,keyStorageFlags:flags);
    Check(restored.Fingerprint==security.Fingerprint && restored.IsAuthorized(credential),"Encrypted persistence failed");
    Check(!restored.IsAuthorized(new string('0',64)),"Wrong credential accepted");
    security.Revoke(security.Phones.Single().Hash);
    Check(!security.IsAuthorized(credential),"Revoked credential accepted");
    var afterRevoke=new SecurityStore(directory,protector:TestProtect,keyStorageFlags:flags);
    Check(!afterRevoke.IsAuthorized(credential),"Revocation not persisted");
    var now=DateTime.UtcNow;
    var invitations=new PairingInvitations(()=>now);
    using var signing=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicKey=Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo());
    var invitation=invitations.Create(publicKey);
    Check(!invitations.Consume(invitation,"wrong-key"),"Invitation accepted a different phone");
    Check(invitations.Consume(invitation,publicKey) && !invitations.Consume(invitation,publicKey),"Invitation reused");
    invitation=invitations.Create(publicKey);now=now.AddMinutes(2);
    Check(!invitations.Consume(invitation,publicKey),"Expired invitation accepted");
    var signed=Encoding.UTF8.GetBytes("LocalMouse8\n"+security.Fingerprint+"\n"+invitation+"\n"+credential);
    var proof=Convert.ToBase64String(signing.SignData(signed,HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence));
    Check(PairingInvitations.Verify(publicKey,proof,security.Fingerprint,invitation,credential),"Signed pairing proof rejected");
    Check(!PairingInvitations.Verify(publicKey,proof,new string('0',64),invitation,credential),"Proof accepted another PC identity");
    Check(!PairingInvitations.Verify(publicKey,proof,security.Fingerprint,invitation,new string('0',64)),"Proof accepted another credential");
    using var discovery=new DiscoveryServer(security.Fingerprint);
    using var udp=new UdpClient();
    var nonce=Guid.NewGuid().ToString("N");
    var query=Encoding.ASCII.GetBytes("LOCALMOUSE8:"+nonce);
    await udp.SendAsync(query,new IPEndPoint(IPAddress.Loopback,45833));
    using var timeout=new CancellationTokenSource(2000);
    var response=await udp.ReceiveAsync(timeout.Token);
    using var advert=JsonDocument.Parse(response.Buffer);
    Check(advert.RootElement.GetProperty("id").GetString()==security.Fingerprint && advert.RootElement.GetProperty("nonce").GetString()==nonce,"Discovery identity/nonce failed");
    using var phoneSocket=new UdpClient(45834);
    var mockPhone=Task.Run(async ()=> {
        using var wait=new CancellationTokenSource(3000);
        var request=await phoneSocket.ReceiveAsync(wait.Token);
        var queryText=Encoding.ASCII.GetString(request.Buffer);
        Check(queryText.StartsWith("LOCALMOUSEPHONE8:"),"Wrong phone discovery query");
        var reply=JsonSerializer.SerializeToUtf8Bytes(new {type="localmouse-phone",nonce=queryText["LOCALMOUSEPHONE8:".Length..],name="Test phone",publicKey});
        await phoneSocket.SendAsync(reply,request.RemoteEndPoint);
    });
    var phones=await PhoneDiscovery.Scan(CancellationToken.None,[IPAddress.Loopback]);
    await mockPhone;
    Check(phones.Count==1 && phones[0].PublicKey==publicKey,"PC could not discover phone");
    await PhoneDiscovery.Invite(phones[0],security.Fingerprint,"test-invitation");
    using var inviteWait=new CancellationTokenSource(2000);
    var invitePacket=await phoneSocket.ReceiveAsync(inviteWait.Token);
    using var inviteJson=JsonDocument.Parse(invitePacket.Buffer);
    Check(inviteJson.RootElement.GetProperty("invitation").GetString()=="test-invitation","PC invitation delivery failed");
    Console.WriteLine("PASS: PC-to-phone discovery and invitation delivery.");
    Console.WriteLine("PASS: encrypted storage round trip (test AES protector), token validation, persisted revocation, signed identity proofs, invitation expiry and one-use invitations and UDP discovery. Production DPAPI/TLS require the normal Windows account.");
    return;
}
Peer.Pin=security.Fingerprint;
var server = new ConnectionServer(mouse, 0, IPAddress.Loopback, keyboard, actions, security);
server.Start();
server.SetInputEnabled(false);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task<Peer> Connect()
{
    var peer = await Peer.Open(server.BoundPort);
    await peer.Send(JsonSerializer.Serialize(new { type = "hello", version = 8, token = credential }));
    var welcome = await peer.Read();
    Check(welcome?.GetProperty("type").GetString() == "welcome", "Handshake failed");
    return peer;
}
async Task RejectHello(string frame)
{
    using var peer = await Peer.Open(server.BoundPort);
    await peer.Send(frame);
    var reply = await peer.Read();
    Check(reply == null || reply.Value.GetProperty("type").GetString() == "error", "Unauthenticated input accepted");
}
try
{
    await RejectHello("{\"type\":\"move\",\"dx\":1,\"dy\":2}");
    await RejectHello("{\"type\":\"click\",\"button\":\"left\"}");
    await RejectHello("{\"type\":\"hello\",\"version\":\"bad\",\"code\":\"wrong\"}");
    await RejectHello(JsonSerializer.Serialize(new { type = "hello", version = 1, token = credential }));
    await RejectHello("[]");
    await RejectHello("{broken");
    Check(mouse.Events.IsEmpty, "Input before authentication");
    using (var peer = await Connect())
    {
        await peer.Send("{\"type\":\"move\",\"dx\":10,\"dy\":-10}");
        var ack = await peer.Read();
        Check(ack?.GetProperty("inputEnabled").GetBoolean() == false && mouse.Events.IsEmpty, "Input accepted while paused");
        await peer.Send("{\"type\":\"click\",\"button\":\"left\"}");
        await peer.Read();
        Check(mouse.Events.IsEmpty, "Click accepted while paused");
        server.SetInputEnabled(true);
        // Multiple frames in one write test buffering and move-before-click ordering.
        await peer.Raw("{\"type\":\"move\",\"dx\":12,\"dy\":-8}\n{\"type\":\"click\",\"button\":\"right\"}\n{\"type\":\"ping\"}\n");
        Check((await peer.Read())?.GetProperty("type").GetString() == "ack", "Missing move ack");
        Check((await peer.Read())?.GetProperty("type").GetString() == "ack", "Missing click ack");
        Check((await peer.Read())?.GetProperty("type").GetString() == "pong", "Missing heartbeat");
        Check(mouse.Events.ToArray().SequenceEqual(new[] { "move:12,-8", "click:right" }), "Input ordering/mapping incorrect");
        server.SetInputEnabled(false);
        await peer.Send("{\"type\":\"click\",\"button\":\"left\"}");
        Check((await peer.Read())?.GetProperty("inputEnabled").GetBoolean() == false, "Pause not reflected");
        Check(mouse.Events.Count == 2, "Pause failed");
    }
    server.SetInputEnabled(true);
    foreach (var invalid in new[] {
        "{\"type\":\"move\",\"dx\":513,\"dy\":0}",
        "{\"type\":\"move\",\"dx\":-513,\"dy\":0}",
        "{\"type\":\"move\",\"dx\":1.2,\"dy\":0}",
        "{\"type\":\"move\",\"dx\":\"1\",\"dy\":0}",
        "{\"type\":\"move\",\"dx\":1}",
        "{\"type\":\"click\",\"button\":\"middle\"}",
        "{\"type\":\"click\",\"button\":1}",
        "{\"type\":\"shutdown\"}", "[]", new string('x',4097) })
    {
        using var peer = await Connect();
        try { await peer.Send(invalid); Check(await peer.Read() == null, "Invalid frame accepted"); }
        catch (IOException) { /* A reset is also valid rejection. */ }
    }
    Check(mouse.Events.Count == 2, "Malformed input reached mouse handler");
    using (var peer = await Peer.Open(server.BoundPort))
    {
        var hello = JsonSerializer.Serialize(new { type = "hello", version = 8, token = credential }) + "\n";
        await peer.Raw(hello[..10]); await peer.Raw(hello[10..]);
        Check((await peer.Read())?.GetProperty("type").GetString() == "welcome", "Fragmented handshake failed");
        await peer.Send("{\"type\":\"move\",\"dx\":-512,\"dy\":512}");
        await peer.Read();
        await peer.Send("{\"type\":\"click\",\"button\":\"left\"}");
        await peer.Read();
        Check(mouse.Events.Last() == "click:left", "Left click failed");
        server.Stop();
        Check(await peer.Read() == null, "Stop did not close active peer");
    }
    server.Start();
    Check(server.InputEnabled,"Saved phones were not enabled after restart");
    await RejectHello(JsonSerializer.Serialize(new {type="hello",version=8,token="bad"}));
    using (var peer = await Connect()) { }
    server.SetInputEnabled(true);
    using (var peer = await Connect()) {
        await peer.Send("{\"type\":\"scroll\",\"dx\":-120,\"dy\":120}"); await peer.Read();
        Check(mouse.Events.Last() == "scroll:-120,120", "Scroll axes/signs failed");
        await peer.Send("{\"type\":\"button\",\"down\":true}"); await peer.Read();
        Check(mouse.Events.Last() == "down", "Drag down failed");
        var before = mouse.Events.Count;
        await peer.Send("{\"type\":\"button\",\"down\":true}"); await peer.Read();
        Check(mouse.Events.Count == before, "Repeated down should be idempotent");
        server.SetInputEnabled(false);
        Check(mouse.Events.Last() == "up", "Pause did not release held button");
    }
    server.SetInputEnabled(true);
    foreach (var end in new[] { "disconnect", "malformed", "timeout", "stop", "release" }) {
        using var peer = await Connect();
        await peer.Send("{\"type\":\"button\",\"down\":true}"); await peer.Read();
        Check(mouse.Events.Last() == "down", "Missing drag press");
        if (end == "disconnect") peer.Dispose();
        else if (end == "malformed") await peer.Send("{\"type\":\"button\",\"down\":123}");
        else if (end == "stop") server.Stop();
        else if (end == "release") { await peer.Send("{\"type\":\"button\",\"down\":false}"); await peer.Read(); }
        var deadline = DateTime.UtcNow.AddSeconds(7);
        while (mouse.Events.Last() != "up" && DateTime.UtcNow < deadline) await Task.Delay(20);
        Check(mouse.Events.Last() == "up", "Held button not released on " + end);
        if (end == "stop") { server.Start(); server.SetInputEnabled(true); }
    }
    Check(WindowsMouseInput.Wheel(-120, false).Mouse.Flags == 0x0800 &&
        WindowsMouseInput.Wheel(-120, false).Mouse.Data == unchecked((uint)-120), "Vertical wheel mapping failed");
    Check(WindowsMouseInput.Wheel(120, true).Mouse.Flags == 0x1000, "Horizontal wheel mapping failed");
    Check(Marshal.SizeOf<WindowsMouseInput.NativeInput>() == (IntPtr.Size == 8 ? 40 : 28), "Wrong Win32 INPUT size");
    Check(Marshal.OffsetOf<WindowsMouseInput.NativeInput>("Mouse").ToInt32() == (IntPtr.Size == 8 ? 8 : 4), "Wrong Win32 INPUT alignment");
    var move = WindowsMouseInput.Movement(-7, 9);
    Check(move.Type == 0 && move.Mouse.Dx == -7 && move.Mouse.Dy == 9 && move.Mouse.Flags == 1, "Wrong native relative movement");
    foreach (var button in new[] { "left", "right" })
    {
        var pair = WindowsMouseInput.ClickPair(button);
        Check(pair.Length == 2 && pair[0].Mouse.Flags == (button == "left" ? 2 : 8) && pair[1].Mouse.Flags == (button == "left" ? 4 : 16), "Wrong native click pair");
    }
    await RejectHello("{\"type\":\"text\",\"text\":\"unauthenticated\"}");
    server.SetInputEnabled(false);
    using (var peer = await Connect()) {
        await peer.Send("{\"type\":\"text\",\"text\":\"paused\"}");
        Check((await peer.Read())?.GetProperty("inputEnabled").GetBoolean() == false, "Paused typing accepted");
        await peer.Send("{\"type\":\"key\",\"key\":\"C\",\"modifiers\":[\"CTRL\"]}"); await peer.Read();
        Check(keyboard.Events.IsEmpty, "Keyboard input bypassed local gate");
    }
    server.SetInputEnabled(true);
    using (var peer = await Connect()) {
        var sample = "Hello 123! café हिन्दी 😀";
        await peer.Send(JsonSerializer.Serialize(new { type = "text", text = sample })); await peer.Read();
        Check(keyboard.Events.Last() == "text:" + sample, "Unicode text changed in protocol");
        await peer.Send("{\"type\":\"key\",\"key\":\"C\",\"modifiers\":[\"CTRL\"]}"); await peer.Read();
        Check(keyboard.Events.Last() == "key:C:CTRL", "Shortcut dispatch failed");
        await peer.Send("{\"type\":\"button\",\"down\":true}"); await peer.Read();
        await peer.Send("{\"type\":\"text\",\"text\":\"x\"}"); await peer.Read();
        Check(mouse.Events.Last() == "up", "Typing did not release an existing drag");
    }
    var mediaKeys = new Dictionary<string,ushort> {
        ["VOLUME_MUTE"]=0xAD, ["VOLUME_DOWN"]=0xAE, ["VOLUME_UP"]=0xAF,
        ["MEDIA_NEXT"]=0xB0, ["MEDIA_PREVIOUS"]=0xB1, ["MEDIA_PLAY_PAUSE"]=0xB3
    };
    foreach (var (key,vk) in mediaKeys) {
        var pair = WindowsKeyboardInput.KeyEvents(key,[]);
        Check(pair.Length == 2 && pair.All(x => x.Value.Keyboard.Key == vk) &&
            pair[0].Value.Keyboard.Flags == 1 && pair[1].Value.Keyboard.Flags == 3, "Media native mapping: " + key);
        server.SetInputEnabled(false);
        int count = keyboard.Events.Count;
        using (var peer = await Connect()) {
            await peer.Send(JsonSerializer.Serialize(new {type="key",key,modifiers=Array.Empty<string>()})); await peer.Read();
            Check(keyboard.Events.Count == count, "Paused media bypassed gate");
            server.SetInputEnabled(true);
            await peer.Send(JsonSerializer.Serialize(new {type="key",key,modifiers=Array.Empty<string>()})); await peer.Read();
            Check(keyboard.Events.Last() == "key:" + key + ":", "Media dispatch: " + key);
        }
    }
    await RejectHello(JsonSerializer.Serialize(new {type="hello",version=4,token=credential}));
    Console.WriteLine("PASS: all six media mappings, dispatch and pause gate; previous protocol rejected.");
    foreach (var action in ActionProtocol.Allowed) {
        server.SetInputEnabled(false);
        int count = actions.Events.Count;
        using var peer = await Connect();
        var command = JsonSerializer.Serialize(new {type="action",action,confirmed=true});
        await peer.Send(command); await peer.Read();
        Check(actions.Events.Count == count,"PC action bypassed pause gate");
        server.SetInputEnabled(true);
        await peer.Send(command); await peer.Read();
        Check(actions.Events.Last() == action,"PC action dispatch failed");
    }
    foreach (var action in new[] {"sleep","restart","shutdown","cmd.exe"}) {
        int count = actions.Events.Count;
        using var peer = await Connect();
        await peer.Send(JsonSerializer.Serialize(new {type="action",action}));
        Check(await peer.Read() == null && actions.Events.Count == count,"Unconfirmed or unknown action accepted");
    }
    actions.Fail = true;
    using (var peer = await Connect()) {
        await peer.Send("{\"type\":\"action\",\"action\":\"brave\"}");
        Check((await peer.Read())?.GetProperty("error").GetString() == "Test launch failure","Launch failure not returned");
        await peer.Send("{\"type\":\"ping\"}");
        Check((await peer.Read())?.GetProperty("type").GetString() == "pong","Action failure killed connection");
    }
    actions.Fail = false;
    Console.WriteLine("PASS: eight PC action dispatches, pause gate, confirmation validation, unknown action rejection and failure feedback. No power or app actions executed.");
    var keyboardCount = keyboard.Events.Count;
    foreach (var invalid in new[] {
        JsonSerializer.Serialize(new { type="text", text=new string('x',257) }),
        "{\"type\":\"text\",\"text\":\"\"}",
        "{\"type\":\"text\",\"text\":17}",
        "{\"type\":\"text\",\"text\":\"\\u0000\"}",
        "{\"type\":\"text\",\"text\":\"\\uD800\"}",
        "{\"type\":\"key\",\"key\":\"UNKNOWN\",\"modifiers\":[]}",
        "{\"type\":\"key\",\"key\":\"C\",\"modifiers\":[\"META\"]}",
        "{\"type\":\"key\",\"key\":\"C\",\"modifiers\":[\"CTRL\",\"CTRL\"]}",
        "{\"type\":\"key\",\"key\":\"C\",\"modifiers\":\"CTRL\"}",
        "{\"type\":\"key\",\"key\":\"WIN\",\"modifiers\":[\"WIN\"]}" }) {
        using var peer = await Connect();
        await peer.Send(invalid); Check(await peer.Read() == null, "Invalid keyboard frame accepted");
    }
    Check(keyboard.Events.Count == keyboardCount, "Invalid keyboard input reached native sink");
    Check(Marshal.SizeOf<WindowsKeyboardInput.NativeInput>() == (IntPtr.Size == 8 ? 40 : 28), "Keyboard INPUT size wrong");
    Check(Marshal.OffsetOf<WindowsKeyboardInput.NativeInput>("Value").ToInt32() == (IntPtr.Size == 8 ? 8 : 4), "Keyboard union alignment wrong");
    var chord = WindowsKeyboardInput.KeyEvents("C", ["CTRL", "SHIFT"]);
    Check(chord.Select(x => x.Value.Keyboard.Key).SequenceEqual(new ushort[] { 0xA2,0xA0,0x43,0x43,0xA0,0xA2 }), "Chord order wrong");
    Check(chord.Select(x => x.Value.Keyboard.Flags).SequenceEqual(new uint[] {0,0,0,2,2,2}), "Chord release flags wrong");
    var cleanup = WindowsKeyboardInput.CleanupForPrefix(chord,3);
    Check(cleanup.Length == 3 && cleanup.All(x => (x.Value.Keyboard.Flags & 2) != 0) && cleanup.Last().Value.Keyboard.Key == 0xA2, "Partial insertion cleanup wrong");
    Check(WindowsKeyboardInput.CleanupForPrefix(chord,chord.Length).Length == 0, "Completed chord should hold no keys");
    var emoji = WindowsKeyboardInput.TextEvents("😀");
    Check(emoji.Length == 4 && emoji[0].Value.Keyboard.Scan == 0xD83D && emoji[2].Value.Keyboard.Scan == 0xDE00 && emoji.All(x => x.Type == 1), "Surrogate pair Unicode input wrong");
    Check(emoji.Select(x => x.Value.Keyboard.Flags).SequenceEqual(new uint[] {4,6,4,6}), "Unicode key flags wrong");
    var newline = WindowsKeyboardInput.TextEvents("\r\n\t");
    Check(newline.Length == 4 && newline[0].Value.Keyboard.Key == 0x0D && newline[2].Value.Keyboard.Key == 9, "CRLF/tab handling wrong");
    Check(WindowsKeyboardInput.KeyEvents("LEFT",[])[0].Value.Keyboard.Flags == 1, "Arrow key needs extended flag");
    var persisted=new SecurityStore(directory);
    Check(persisted.Fingerprint==security.Fingerprint && persisted.IsAuthorized(credential),"Pairing/identity did not survive reload");
    Check(!persisted.IsAuthorized(new string('0',64)),"Wrong token accepted");
    using var signing=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicKey=Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo());
    var phoneToken=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    string Request(string invitation="") {
        var proof=Convert.ToBase64String(signing.SignData(Encoding.UTF8.GetBytes("LocalMouse8\n"+security.Fingerprint+"\n"+invitation+"\n"+phoneToken),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence));
        return JsonSerializer.Serialize(new {type="hello",version=8,token=phoneToken,pair="request",invitation,publicKey,proof,name="Approval phone"});
    }
    server.RequestApproval=(_,_)=>Task.FromResult(false);
    using(var peer=await Peer.Open(server.BoundPort)) {
        await peer.Send(Request());Check((await peer.Read())?.GetProperty("type").GetString()=="error","Declined pairing accepted");
        Check(!security.IsAuthorized(phoneToken),"Declined phone was saved");
    }
    server.RequestApproval=(_,_)=>Task.FromResult(true);
    using(var peer=await Peer.Open(server.BoundPort)) {
        await peer.Send(Request());Check((await peer.Read())?.GetProperty("type").GetString()=="welcome","Approved pairing failed");
        Check(server.InputEnabled,"Approved pairing did not enable controls");
        server.Revoke(server.Security.Phones.Single(p=>p.Name=="Approval phone").Hash);
        Check(await peer.Read()==null,"Unpair did not disconnect phone");
    }
    Check(!security.IsAuthorized(phoneToken),"Revoked token remained valid");
    server.RequestApproval=(_,_)=>Task.FromResult(false);
    var invitation=server.Invitations.Create(publicKey);
    using(var peer=await Peer.Open(server.BoundPort)) {
        await peer.Send(Request(invitation));Check((await peer.Read())?.GetProperty("type").GetString()=="welcome","PC-initiated invitation failed");
    }
    bool pinRejected=false;
    var correctPin=Peer.Pin; Peer.Pin=new string('0',64);
    try { using var peer=await Peer.Open(server.BoundPort); } catch(AuthenticationException) { pinRejected=true; }
    finally { Peer.Pin=correctPin; }
    Check(pinRejected,"Wrong certificate pin accepted");
    Console.WriteLine("PASS: persisted encrypted test identity and pairing, approval/decline, signed TLS pairing, PC-initiated invitation, immediate unpair and wrong certificate pin rejection.");
    Console.WriteLine("PASS: v8 TLS authentication, movement/click ordering, pause/resume, bounds/schema rejection, fragmented/batched frames, reconnect, active stop, saved approvals and native INPUT layout/mapping. Scroll axes and drag release on pause/disconnect/malformed input/timeout/stop/explicit release passed. Keyboard gate/schema, Unicode, chord order, partial cleanup and native keyboard layouts passed. No real Windows input generated.");
}
finally { server.Stop(); }

sealed class RecordingMouse : IMouseInput
{
    public ConcurrentQueue<string> Events { get; } = new();
    public void Move(int dx, int dy) => Events.Enqueue($"move:{dx},{dy}");
    public void Click(string button) => Events.Enqueue("click:" + button);
    public void Scroll(int dx, int dy) => Events.Enqueue($"scroll:{dx},{dy}");
    public void SetLeft(bool down) => Events.Enqueue(down ? "down" : "up");
}
sealed class Peer : IDisposable
{
    public static string Pin = "";
    private readonly TcpClient socket;
    private readonly SslStream stream;
    private readonly StreamReader reader;
    private Peer(TcpClient socket,SslStream stream) { this.socket=socket; this.stream=stream; reader=new StreamReader(stream); }
    public static async Task<Peer> Open(int port)
    {
        var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, port);
        var stream=new SslStream(socket.GetStream(),false,(_,certificate,_,_) => certificate!=null && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()))==Pin);
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {TargetHost="LocalMouse",EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13});
        return new Peer(socket,stream);
    }
    public Task Send(string frame) => Raw(frame + "\n");
    public async Task Raw(string text) => await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
    public async Task<JsonElement?> Read()
    {
        using var timeout = new CancellationTokenSource(3000);
        string? line;
        try { line=await reader.ReadLineAsync(timeout.Token); } catch(IOException) { return null; }
        if (line == null) return null;
        using var parsed = JsonDocument.Parse(line);
        return parsed.RootElement.Clone();
    }
    public void Dispose() { reader.Dispose(); socket.Dispose(); }
}

sealed class RecordingKeyboard : IKeyboardInput
{
    public ConcurrentQueue<string> Events { get; } = new();
    public void Text(string text) => Events.Enqueue("text:" + text);
    public void Key(string key, IReadOnlyList<string> modifiers) => Events.Enqueue("key:" + key + ":" + string.Join(",",modifiers));
}
sealed class RecordingActions : IPCActionInput {
    public ConcurrentQueue<string> Events { get; } = new();
    public bool Fail;
    public void Execute(string action) { if (Fail) throw new InvalidOperationException("Test launch failure"); Events.Enqueue(action); }
}
