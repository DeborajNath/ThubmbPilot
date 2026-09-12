using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace LocalMouse;
public sealed record NearbyPhone(string Name,IPAddress Address,string PublicKey);
public static class PhoneDiscovery {
    public static async Task<IReadOnlyList<NearbyPhone>> Scan(CancellationToken token,IReadOnlyList<IPAddress>? targets=null) {
        using var udp=new UdpClient {EnableBroadcast=true};
        var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var bytes=Encoding.ASCII.GetBytes("LOCALMOUSEPHONE8:"+nonce);
        var addresses=new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach(var network in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up)) {
            foreach(var address in network.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork)) {
                var ip=address.Address.GetAddressBytes();var mask=address.IPv4Mask.GetAddressBytes();
                addresses.Add(new IPAddress(ip.Select((b,i)=>(byte)(b|~mask[i])).ToArray()));
            }
        }
        if(targets!=null) addresses=new HashSet<IPAddress>(targets);
        foreach(var address in addresses) { try { await udp.SendAsync(bytes,new IPEndPoint(address,45834),token); } catch(SocketException) {} }
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(2000);
        var found=new Dictionary<string,NearbyPhone>();
        try {
            while(!timeout.IsCancellationRequested) {
                var response=await udp.ReceiveAsync(timeout.Token);
                if(response.Buffer.Length>2048) continue;
                try {
                    using var document=JsonDocument.Parse(response.Buffer);var root=document.RootElement;
                    if(root.GetProperty("type").GetString()!="localmouse-phone" || root.GetProperty("nonce").GetString()!=nonce) continue;
                    var key=root.GetProperty("publicKey").GetString()!;
                    using var verifier=ECDsa.Create();verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key),out _);
                    found[key]=new NearbyPhone(root.GetProperty("name").GetString()![..Math.Min(48,root.GetProperty("name").GetString()!.Length)],response.RemoteEndPoint.Address,key);
                } catch(Exception ex) when(ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or CryptographicException or ArgumentException) {}
            }
        } catch(OperationCanceledException) when(!token.IsCancellationRequested) {}
        return found.Values.ToArray();
    }
    public static async Task Invite(NearbyPhone phone,string fingerprint,string invitation) {
        using var udp=new UdpClient();
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new {type="localmouse-invite",version=8,name=Environment.MachineName,id=fingerprint,invitation});
        for(int i=0;i<3;i++) { await udp.SendAsync(bytes,new IPEndPoint(phone.Address,45834)); if(i<2) await Task.Delay(200); }
    }
}
