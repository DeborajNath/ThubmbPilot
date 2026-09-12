using System.Net.Security;
using System.Security.Authentication;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalMouse;

public sealed class ConnectionServer(IMouseInput mouse, int port = ConnectionServer.Port, IPAddress? bindAddress = null, IKeyboardInput? keyboard = null, IPCActionInput? actions = null, SecurityStore? security = null)
{
    public const int Port = 45832;
    public const int ProtocolVersion = 8;
    public SecurityStore Security { get; } = security ?? new SecurityStore();
    public PairingInvitations Invitations { get; } = new();
    public Func<PairRequest,CancellationToken,Task<bool>>? RequestApproval { get; set; }
    private TcpClient? activeClient;
    public void Revoke(string hash) { lock(inputLock) { Security.Revoke(hash); ReleaseHeld(); activeClient?.Close(); } }
    public int BoundPort { get; private set; }
    public event Action<string>? StatusChanged;
    private readonly object inputLock = new();
    private bool inputEnabled;
    private long nextSession, heldOwner;
    private bool leftHeld;
    private CancellationTokenSource? lifetime;
    private TcpListener? listener;

    public bool InputEnabled { get { lock (inputLock) return inputEnabled; } }
    public void SetInputEnabled(bool value)
    {
        lock (inputLock)
        {
            inputEnabled = value && lifetime != null;
            if (!inputEnabled) ReleaseHeld();
        }
    }
    private void ReleaseHeld()
    {
        if (!leftHeld) return;
        try { mouse.SetLeft(false); }
        catch (Win32Exception ex) { StatusChanged?.Invoke("Button release failed: " + ex.Message); }
        finally { leftHeld = false; heldOwner = 0; }
    }

    public void Start()
    {
        if (lifetime != null) return;
        var next = new TcpListener(bindAddress ?? IPAddress.Any, port);
        next.Start(8);
        listener = next;
        BoundPort = ((IPEndPoint)next.LocalEndpoint).Port;
        lifetime = new CancellationTokenSource();
        SetInputEnabled(Security.Phones.Count>0);
        Invitations.Clear();
        StatusChanged?.Invoke("Listening — waiting for phone");
        _ = AcceptLoop(next, lifetime.Token);
    }

    public void Stop()
    {
        SetInputEnabled(false);
        Invitations.Clear();
        activeClient?.Close();
        lifetime?.Cancel();
        listener?.Stop();
        lifetime?.Dispose();
        lifetime = null;
        listener = null;
        BoundPort = 0;
        StatusChanged?.Invoke("Stopped");
    }

    private async Task AcceptLoop(TcpListener socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await socket.AcceptTcpClientAsync(token);
                client.NoDelay = true;
                string nextStatus = "Listening — phone disconnected";
                activeClient=client;
                try { await Handle(client, token); }
                catch (Win32Exception ex) { nextStatus = "Input stopped: " + ex.Message; }
                catch (Exception ex) when (ex is AuthenticationException or IOException or JsonException or OperationCanceledException or SocketException or InvalidDataException or DecoderFallbackException or InvalidOperationException)
                { /* Drop invalid, disconnected or timed-out peers. */ }
                if (!token.IsCancellationRequested) StatusChanged?.Invoke(nextStatus);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested) StatusChanged?.Invoke("Listener stopped: " + ex.Message);
        }
    }

    private async Task Handle(TcpClient client, CancellationToken token)
    {
        using var stream = new SslStream(client.GetStream(),false);
        using(var handshake = CancellationTokenSource.CreateLinkedTokenSource(token)) {
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                ServerCertificate=Security.Certificate, EnabledSslProtocols=SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateRequired=false, CertificateRevocationCheckMode=System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
            },handshake.Token);
        }
        var reader = new FrameReader(stream);
        using var hello = JsonDocument.Parse(await reader.Read(token));
        var root = hello.RootElement;
        static string Field(JsonElement frame,string key) => frame.ValueKind==JsonValueKind.Object && frame.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String ? v.GetString()! : "";
        var credential=Field(root,"token");
        var valid=root.ValueKind==JsonValueKind.Object && Field(root,"type")=="hello" && root.TryGetProperty("version",out var version) && version.TryGetInt32(out var v) && v==ProtocolVersion;
        var authorized=valid && Security.IsAuthorized(credential);
        if(valid && !authorized && credential.Length==64 && credential.All(Uri.IsHexDigit) && Field(root,"pair")=="request") {
            var publicKey=Field(root,"publicKey"); var nonce=Field(root,"invitation");
            if(PairingInvitations.Verify(publicKey,Field(root,"proof"),Security.Fingerprint,nonce,credential)) {
                using var approvalTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);
                approvalTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                bool approved=false;
                if(nonce.Length>0) approved=Invitations.Consume(nonce,publicKey);
                else if(RequestApproval!=null) {
                    var name=new string(Field(root,"name").Where(c=>!char.IsControl(c)).Take(48).ToArray());
                    approved=await RequestApproval(new PairRequest(name,client.Client.RemoteEndPoint?.ToString() ?? "",publicKey),approvalTimeout.Token).WaitAsync(approvalTimeout.Token);
                }
                lock(inputLock) {
                    if(approved && !approvalTimeout.IsCancellationRequested) {
                        authorized=Security.Approve(credential,Field(root,"name"));
                        if(authorized) inputEnabled=true;
                    }
                }
            }
        }
        if (!authorized) {
            await Send(stream,new {type="error",message="Pairing declined or expired. Send a new pairing request from either device."},token);
            return;
        }
        await Send(stream,new {type="welcome",version=ProtocolVersion,name=Environment.MachineName,id=Security.Fingerprint,inputEnabled=InputEnabled},token);
        StatusChanged?.Invoke("Connected: " + client.Client.RemoteEndPoint);
        var session = Interlocked.Increment(ref nextSession);
        try {
        while (!token.IsCancellationRequested)
        {
            using var message = JsonDocument.Parse(await reader.Read(token));
            if (!Security.IsAuthorized(credential)) throw new InvalidDataException("Phone unpaired");
            var frame = message.RootElement;
            if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Missing message type");
            switch (kind.GetString())
            {
                case "ping":
                    await Send(stream, new { type = "pong", inputEnabled = InputEnabled }, token);
                    break;
                case "action":
                    var action = ActionProtocol.Read(frame);
                    string? actionError = null;
                    bool actionAllowed;
                    lock (inputLock) {
                        actionAllowed = inputEnabled && !token.IsCancellationRequested && Security.IsAuthorized(credential);
                        if (actionAllowed) {
                            ReleaseHeld();
                            try { (actions ?? throw new InvalidOperationException("PC actions unavailable")).Execute(action); }
                            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException) { actionError = ex.Message; }
                        }
                    }
                    await Send(stream,new {type="ack",inputEnabled=actionAllowed,error=actionError},token);
                    break;
                case "text":
                    if (!frame.TryGetProperty("text", out var content) || content.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Invalid text command");
                    var text = content.GetString()!;
                    KeyboardProtocol.ValidateText(text);
                    bool typed;
                    lock (inputLock) {
                        typed = inputEnabled && !token.IsCancellationRequested && Security.IsAuthorized(credential);
                        if (typed) { ReleaseHeld(); (keyboard ?? throw new InvalidDataException("Keyboard unavailable")).Text(text); }
                    }
                    await Send(stream, new { type = "ack", inputEnabled = typed }, token);
                    break;
                case "key":
                    var shortcut = KeyboardProtocol.ReadKey(frame);
                    bool keyed;
                    lock (inputLock) {
                        keyed = inputEnabled && !token.IsCancellationRequested && Security.IsAuthorized(credential);
                        if (keyed) { ReleaseHeld(); (keyboard ?? throw new InvalidDataException("Keyboard unavailable")).Key(shortcut.Key, shortcut.Modifiers); }
                    }
                    await Send(stream, new { type = "ack", inputEnabled = keyed }, token);
                    break;
                case "move":
                    var dx = Delta(frame, "dx");
                    var dy = Delta(frame, "dy");
                    bool moved;
                    lock (inputLock)
                    {
                        moved = inputEnabled && !token.IsCancellationRequested && Security.IsAuthorized(credential);
                        if (moved && (dx != 0 || dy != 0)) mouse.Move(dx, dy);
                    }
                    await Send(stream, new { type = "ack", inputEnabled = moved }, token);
                    break;
                case "scroll":
                    var sx = Delta(frame, "dx"); var sy = Delta(frame, "dy");
                    bool scrolled;
                    lock (inputLock) {
                        scrolled = inputEnabled && !token.IsCancellationRequested && Security.IsAuthorized(credential);
                        if (scrolled) mouse.Scroll(sx, sy);
                    }
                    await Send(stream, new { type = "ack", inputEnabled = scrolled }, token);
                    break;
                case "button":
                    if (!frame.TryGetProperty("down", out var down) || down.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("Invalid button state");
                    bool allowed;
                    lock (inputLock) {
                        allowed = inputEnabled && !token.IsCancellationRequested && Security.IsAuthorized(credential);
                        if (!down.GetBoolean()) {
                            if (heldOwner == session) ReleaseHeld();
                        } else if (allowed && !leftHeld) {
                            // Set ownership before insertion so cleanup also runs on native failures.
                            leftHeld = true; heldOwner = session;
                            mouse.SetLeft(true);
                        }
                    }
                    await Send(stream, new { type = "ack", inputEnabled = allowed }, token);
                    break;
                case "click":
                    if (!frame.TryGetProperty("button", out var button) || button.ValueKind != JsonValueKind.String ||
                        button.GetString() is not ("left" or "right")) throw new InvalidDataException("Invalid button");
                    bool clicked;
                    lock (inputLock)
                    {
                        clicked = inputEnabled && !token.IsCancellationRequested && Security.IsAuthorized(credential);
                        if (clicked) { ReleaseHeld(); mouse.Click(button.GetString()!); }
                    }
                    await Send(stream, new { type = "ack", inputEnabled = clicked }, token);
                    break;
                default: throw new InvalidDataException("Unsupported message");
            }
        }
        } finally { lock (inputLock) { if (heldOwner == session) ReleaseHeld(); } }
    }

    private static int Delta(JsonElement frame, string name)
    {
        if (!frame.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Number ||
            !field.TryGetInt32(out var value) || value is < -512 or > 512)
            throw new InvalidDataException("Invalid movement delta");
        return value;
    }

    // Buffer socket reads, while keeping the per-frame size and whole-frame deadline.
    private sealed class FrameReader(Stream stream)
    {
        private readonly byte[] buffer = new byte[4096];
        private int offset, available;
        public async Task<string> Read(CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var bytes = new List<byte>();
            while (bytes.Count < 4096)
            {
                if (offset == available)
                {
                    available = await stream.ReadAsync(buffer, timeout.Token);
                    offset = 0;
                    if (available == 0) throw new IOException("Peer closed");
                }
                var b = buffer[offset++];
                if (b == 10) return new UTF8Encoding(false, true).GetString(bytes.ToArray());
                bytes.Add(b);
            }
            throw new InvalidDataException("Message too large");
        }
    }

    private static async Task Send(Stream stream, object message, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + "\n"), timeout.Token);
    }
}