using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace Forterm.Core;

public interface ISecretVault
{
    Task<byte[]?> ReadAsync(string key, CancellationToken ct);
    Task WriteAsync(string key, byte[] secret, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
public sealed class UnavailableVault : ISecretVault
{
    public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => Task.FromException<byte[]?>(new IOException("系统凭据存储不可用，请使用仅本次输入"));
    public Task WriteAsync(string key, byte[] secret, CancellationToken ct) => Task.FromException(new IOException("系统凭据存储不可用；没有明文回退"));
    public Task DeleteAsync(string key, CancellationToken ct) => Task.FromException(new IOException("系统凭据存储不可用，无法确认移除"));
}
/// <summary>OS credential storage only. No files, command-line secrets, shell
/// utilities or fallback encryption keys. Native calls run off the UI thread.</summary>
public sealed class OsCredentialVault(string scope) : ISecretVault
{
    static readonly SemaphoreSlim gate = new(1, 1);
    readonly string prefix = "Forterm.Avalonia/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..24] + "/";
    async Task<T> Run<T>(Func<T> operation, CancellationToken ct) { await gate.WaitAsync(ct); try { ct.ThrowIfCancellationRequested(); return await Task.Run(operation, CancellationToken.None); } finally { gate.Release(); } }
    public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => Run(() => OperatingSystem.IsWindows() ? Win.Read(prefix + key) : OperatingSystem.IsMacOS() ? Mac.Read(prefix + key) : throw new PlatformNotSupportedException("仅支持Windows Credential Manager和macOS Keychain"), ct);
    public async Task WriteAsync(string key, byte[] secret, CancellationToken ct)
    { if (secret.Length > 2400) throw new IOException("密码超过跨平台系统保管库2400字节边界；可仅用于本次连接"); await Run(() => { if (OperatingSystem.IsWindows()) Win.Write(prefix + key, secret); else if (OperatingSystem.IsMacOS()) Mac.Write(prefix + key, secret); else throw new PlatformNotSupportedException(); return true; }, ct); }
    public async Task DeleteAsync(string key, CancellationToken ct) => await Run(() => { if (OperatingSystem.IsWindows()) Win.Delete(prefix + key); else if (OperatingSystem.IsMacOS()) Mac.Delete(prefix + key); else throw new PlatformNotSupportedException(); return true; }, ct);
    public static object ProbeNativeSymbols() => OperatingSystem.IsMacOS() ? Mac.Probe() : new { Platform = OperatingSystem.IsWindows() ? "Windows" : "unsupported", CredentialsAccessed = false };
    static class Win
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Credential
        { public uint Flags, Type; [MarshalAs(UnmanagedType.LPWStr)] public string Target; public IntPtr Comment; public long Written; public uint Size; public IntPtr Blob; public uint Persist, Count; public IntPtr Attributes, Alias, Username; }
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ReadNative(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WriteNative(ref Credential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DeleteNative(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] static extern void CredFree(IntPtr credential);
        static IOException Error() => new("Windows凭据管理器操作失败；未保存明文", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        public static byte[]? Read(string key)
        {
            if (!ReadNative(key, 1, 0, out var pointer)) { if (Marshal.GetLastPInvokeError() == 1168) return null; throw Error(); }
            try { var c = Marshal.PtrToStructure<Credential>(pointer); if (c.Size > 2400) throw new IOException("系统凭据长度超出边界"); byte[] result = new byte[c.Size]; Marshal.Copy(c.Blob, result, 0, result.Length); Marshal.Copy(new byte[result.Length], 0, c.Blob, result.Length); return result; } finally { CredFree(pointer); }
        }
        public static void Write(string key, byte[] secret)
        {
            var blob = Marshal.AllocHGlobal(secret.Length); try { Marshal.Copy(secret, 0, blob, secret.Length); var c = new Credential { Type = 1, Target = key, Size = (uint)secret.Length, Blob = blob, Persist = 2 }; if (!WriteNative(ref c, 0)) throw Error(); } finally { Marshal.Copy(new byte[secret.Length], 0, blob, secret.Length); Marshal.FreeHGlobal(blob); }
        }
        public static void Delete(string key) { if (!DeleteNative(key, 1, 0) && Marshal.GetLastPInvokeError() != 1168) throw Error(); }
    }
    static class Mac
    {
        const string Security = "/System/Library/Frameworks/Security.framework/Security", Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        static readonly IntPtr security = NativeLibrary.Load(Security), foundation = NativeLibrary.Load(Foundation);
        static IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(security, name));
        static IntPtr CFConstant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(foundation, name));
        [DllImport(Foundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
        [DllImport(Foundation)] static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint count);
        [DllImport(Foundation)] static extern nint CFDataGetLength(IntPtr data);
        [DllImport(Foundation)] static extern IntPtr CFDataGetBytePtr(IntPtr data);
        [DllImport(Foundation)] static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keys, IntPtr values);
        [DllImport(Foundation)] static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
        [DllImport(Foundation)] static extern void CFRelease(IntPtr value);
        [DllImport(Security)] static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
        [DllImport(Security)] static extern int SecItemAdd(IntPtr attributes, IntPtr result);
        [DllImport(Security)] static extern int SecItemUpdate(IntPtr query, IntPtr attributes);
        [DllImport(Security)] static extern int SecItemDelete(IntPtr query);
        sealed class Dictionary : IDisposable
        {
            public readonly IntPtr Value = CFDictionaryCreateMutable(IntPtr.Zero, 0, NativeLibrary.GetExport(foundation, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(foundation, "kCFTypeDictionaryValueCallBacks"));
            public void ConstantValue(string key, string value) => CFDictionarySetValue(Value, Constant(key), Constant(value));
            public void String(string key, string value) { var cf = CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100); try { CFDictionarySetValue(Value, Constant(key), cf); } finally { CFRelease(cf); } }
            public void Data(string key, byte[] value) { var cf = CFDataCreate(IntPtr.Zero, value, value.Length); try { CFDictionarySetValue(Value, Constant(key), cf); } finally { CFRelease(cf); } }
            public void True(string key) => CFDictionarySetValue(Value, Constant(key), CFConstant("kCFBooleanTrue"));
            public void Dispose() => CFRelease(Value);
        }
        static Dictionary Query(string key) { var d = new Dictionary(); d.ConstantValue("kSecClass", "kSecClassGenericPassword"); d.String("kSecAttrService", "cn.forterm.avalonia.v1"); d.String("kSecAttrAccount", key); d.ConstantValue("kSecUseAuthenticationUI", "kSecUseAuthenticationUIFail"); return d; }
        static void Check(int result) { if (result != 0) throw new IOException($"macOS钥匙串操作失败（{result}）；可能需要手动解锁或授权，未做明文回退"); }
        public static byte[]? Read(string key)
        {
            using var q = Query(key); q.True("kSecReturnData"); q.ConstantValue("kSecMatchLimit", "kSecMatchLimitOne"); int result = SecItemCopyMatching(q.Value, out var value); if (result == -25300) return null; Check(result);
            try { nint n = CFDataGetLength(value); if (n < 0 || n > 2400) throw new IOException("系统凭据长度超出边界"); byte[] bytes = new byte[(int)n]; Marshal.Copy(CFDataGetBytePtr(value), bytes, 0, bytes.Length); return bytes; } finally { CFRelease(value); }
        }
        public static void Write(string key, byte[] secret)
        {
            using var q = Query(key); using var update = new Dictionary(); update.Data("kSecValueData", secret); int result = SecItemUpdate(q.Value, update.Value); if (result == -25300) { q.Data("kSecValueData", secret); result = SecItemAdd(q.Value, IntPtr.Zero); }
            Check(result);
        }
        public static void Delete(string key) { using var q = Query(key); int result = SecItemDelete(q.Value); if (result != -25300) Check(result); }
        public static object Probe() { string[] names = { "kSecClass", "kSecClassGenericPassword", "kSecAttrService", "kSecAttrAccount", "kSecUseAuthenticationUI", "kSecUseAuthenticationUIFail", "kSecReturnData", "kSecMatchLimit", "kSecMatchLimitOne", "kSecValueData" }; foreach (var name in names) if (Constant(name) == IntPtr.Zero) throw new IOException("系统钥匙串符号不可用"); foreach (var name in new[] { "SecItemCopyMatching", "SecItemAdd", "SecItemUpdate", "SecItemDelete" }) NativeLibrary.GetExport(security, name); return new { Platform = "macOS", NativeSymbols = names.Length + 4, CredentialsAccessed = false }; }
    }
}
