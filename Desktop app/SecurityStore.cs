using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
namespace LocalMouse;

public sealed record PairedPhone(string Name, string Hash);
public sealed class SecurityStore {
    private sealed record State(string Certificate, List<PairedPhone> Phones);
    private readonly object gate = new();
    private readonly string path;
    private readonly Func<byte[],bool,byte[]> protect;
    private readonly Func<DateTime> clock;
    private State state;
    public X509Certificate2 Certificate { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Certificate.RawData));
    public IReadOnlyList<PairedPhone> Phones { get { lock(gate) return state.Phones.ToArray(); } }
    public SecurityStore(string? directory = null, Func<DateTime>? clock = null, Func<byte[],bool,byte[]>? protector = null, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.UserKeySet) {
        protect=protector ?? Protect;
        this.clock=clock ?? (()=>DateTime.UtcNow);
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalMouse");
        Directory.CreateDirectory(directory); path=Path.Combine(directory,"identity.dat");
        if (File.Exists(path)) {
            state=JsonSerializer.Deserialize<State>(protect(File.ReadAllBytes(path),false)) ?? throw new InvalidDataException("Invalid identity file");
            Certificate=X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(state.Certificate),null,keyStorageFlags);
        } else {
            using var rsa=RSA.Create(3072);
            var request=new CertificateRequest("CN=Local Mouse",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,true));
            using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(20));
            var pfx=cert.Export(X509ContentType.Pfx);
            Certificate=X509CertificateLoader.LoadPkcs12(pfx,null,keyStorageFlags);
            state=new(Convert.ToBase64String(pfx),[]); Save();
        }
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public bool IsAuthorized(string token) {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return false;
        var hash=Convert.FromHexString(Hash(token));
        lock(gate) return state.Phones.Any(p => CryptographicOperations.FixedTimeEquals(hash,Convert.FromHexString(p.Hash)));
    }
    // Called only after local approval or a verified invitation initiated locally.
    public bool Approve(string token,string name) {
        lock(gate) {
            if(IsAuthorized(token)) return true;
            if(token.Length!=64 || !token.All(Uri.IsHexDigit) || state.Phones.Count>=16) return false;
            var phone=new PairedPhone(new string(name.Where(c=>!char.IsControl(c)).Take(48).ToArray()),Hash(token));
            state.Phones.Add(phone);
            try { Save(); } catch { state.Phones.Remove(phone); throw; }
            return true;
        }
    }
    public void Revoke(string hash) { lock(gate) { var removed=state.Phones.Where(p=>p.Hash==hash).ToList(); state.Phones.RemoveAll(p=>p.Hash==hash); try { Save(); } catch { state.Phones.AddRange(removed); throw; } } }
    private void Save() {
        var bytes=protect(JsonSerializer.SerializeToUtf8Bytes(state),true);
        File.WriteAllBytes(path+".tmp",bytes); File.Move(path+".tmp",path,true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    private static byte[] Protect(byte[] bytes,bool encrypt) {
        var input=new Blob { Size=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length) }; Blob output=default;
        try {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            bool ok=encrypt ? CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output) : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result=new byte[output.Size]; Marshal.Copy(output.Data,result,0,result.Length); return result;
        } finally { Marshal.FreeHGlobal(input.Data); if(output.Data!=IntPtr.Zero) LocalFree(output.Data); }
    }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
}

