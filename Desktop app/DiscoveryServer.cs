using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace LocalMouse;
public sealed class DiscoveryServer : IDisposable {
    private readonly UdpClient socket = new(45833);
    private readonly CancellationTokenSource stop = new();
    public DiscoveryServer(string id) { _ = Run(id); }
    private async Task Run(string id) {
        try {
            while(!stop.IsCancellationRequested) {
                var packet=await socket.ReceiveAsync(stop.Token);
                if (packet.Buffer.Length>128) continue;
                var query=Encoding.ASCII.GetString(packet.Buffer);
                if (!query.StartsWith("LOCALMOUSE8:")) continue;
                var nonce=query[12..];
                if (nonce.Length!=32 || !nonce.All(Uri.IsHexDigit)) continue;
                var reply=JsonSerializer.SerializeToUtf8Bytes(new {type="localmouse",version=8,id,name=Environment.MachineName,nonce});
                await socket.SendAsync(reply,packet.RemoteEndPoint,stop.Token);
            }
        } catch(Exception ex) when(ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public void Dispose() { stop.Cancel(); socket.Dispose(); stop.Dispose(); }
}
