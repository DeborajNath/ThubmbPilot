using System.Security.Cryptography;
using System.Text;
namespace LocalMouse;
public sealed record PairRequest(string Name,string Address,string PublicKey);
public sealed class PairingInvitations {
    private readonly object gate=new();
    private readonly Dictionary<string,(string Key,DateTime Expires)> pending=new();
    private readonly Func<DateTime> clock;
    public PairingInvitations(Func<DateTime>? clock=null) { this.clock=clock ?? (()=>DateTime.UtcNow); }
    public string Create(string publicKey) {
        lock(gate) {
            pending.Clear();
            var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            pending[nonce]=(publicKey,clock().AddSeconds(60)); return nonce;
        }
    }
    public bool Consume(string nonce,string publicKey) {
        lock(gate) {
            if(!pending.TryGetValue(nonce,out var value) || value.Expires<=clock() || value.Key!=publicKey) return false;
            pending.Remove(nonce);return true;
        }
    }
    public void Clear() { lock(gate) pending.Clear(); }
    public static bool Verify(string publicKey,string signature,string fingerprint,string nonce,string token) {
        try {
            if(publicKey.Length>512 || signature.Length>256 || nonce.Length>64) return false;
            using var key=ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey),out _);
            return key.VerifyData(Encoding.UTF8.GetBytes("LocalMouse8\n"+fingerprint+"\n"+nonce+"\n"+token),Convert.FromBase64String(signature),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence);
        } catch(Exception ex) when(ex is CryptographicException or FormatException or ArgumentException) { return false; }
    }
}
