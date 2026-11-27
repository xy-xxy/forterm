using System.Text.Json;
using Forterm.Core;

namespace Forterm.Studio.Library;

/// <summary>Bastion account (堡垒机账户): portal address, tenant and username, with the password and the authenticator
/// (TOTP) seed kept only in the OS credential store. Shown and copied on request; with a fort portal (alpha.13) also used
/// to log in without a browser ("登录堡垒机"), its certificate pinned by the SHA-256 the user confirmed.</summary>
public sealed record BastionAccount
{
    public string Id { get; init; } = StudioProfile.NewId();
    public string Name { get; init; } = "";
    public string PortalUrl { get; init; } = "";
    public string Username { get; init; } = "";
    /// <summary>The fort portal's "租户名称"; "" when not used.</summary>
    public string TenantName { get; init; } = "";
    /// <summary>SHA-256 (hex) of the portal certificate the user trusted; "" until then.</summary>
    public string CertificatePin { get; init; } = "";
    public string Note { get; init; } = "";
    public bool HasPassword { get; init; }
    public bool HasSeed { get; init; }
    public string Algorithm { get; init; } = "SHA1";
    public int Digits { get; init; } = 6;
    public int Period { get; init; } = 30;

    public TotpParameters Parameters => new(Algorithm, Digits, Period);
    /// <summary>Logins of target hosts kept for the portal's <c>$user</c> accounts ("目标主机凭据"): name and user here,
    /// the password only in the credential store (kind <see cref="BastionLogin.SecretKind"/> under the login's id).</summary>
    public IReadOnlyList<BastionLogin> Logins { get; init; } = [];
    /// <summary>What the user chose last in the account list of each machine (its portal id; <c>*</c>: the last of all):
    /// <c>account:&lt;name&gt;</c>, <c>login:&lt;id&gt;</c> or <c>ask</c>. No secret.</summary>
    public IReadOnlyDictionary<string, string> Choices { get; init; } = new Dictionary<string, string>();
}

/// <summary>A target host's login kept for a bastion account: the name the user gave it and the user name; the password is
/// in the credential store only.</summary>
public sealed record BastionLogin
{
    public const string SecretKind = "target";
    public string Id { get; init; } = StudioProfile.NewId();
    public string Name { get; init; } = "";
    public string User { get; init; } = "";
    public string Label => Name.Length > 0 ? $"{Name}（{User}）" : User;
}

public sealed class AccountStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly string? path;
    List<BastionAccount> accounts = new();

    public AccountStore(string? directory)
    {
        path = directory == null ? null : Path.Combine(directory, "accounts.json");
        if (path != null && File.Exists(path))
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("账户文件超过4MiB，未读取");
            accounts = JsonSerializer.Deserialize<List<BastionAccount>>(File.ReadAllBytes(path), Json) ?? new();
            try { foreach (var a in accounts) Validate(a); }
            catch (Exception e) when (e is NullReferenceException or ArgumentNullException) { throw new InvalidDataException("账户文件含缺失或为空的字段", e); }
        }
    }

    public IReadOnlyList<BastionAccount> Accounts => accounts;
    public event Action? Changed;
    public BastionAccount? Find(string id) => accounts.FirstOrDefault(a => a.Id == id);

    public void Upsert(BastionAccount account)
    {
        Validate(account);
        var next = accounts.ToList();
        int index = next.FindIndex(a => a.Id == account.Id);
        if (index >= 0) next[index] = account; else next.Add(account);
        if (next.Count > 200) throw new InvalidDataException("账户最多 200 个");
        Write(next);
    }

    public void Remove(string id) => Write(accounts.Where(a => a.Id != id).ToList());

    void Write(List<BastionAccount> next)
    {
        if (path != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("账户文件会超过 4MiB（读取时的上限），没有保存");
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        accounts = next;
        Changed?.Invoke();
    }

    public static void Validate(BastionAccount a)
    {
        if (a.Id.Length is 0 or > 64 || !a.Id.All(char.IsAsciiLetterOrDigit)) throw new InvalidDataException("账户标识无效");
        if (a.Name.Trim().Length is 0 or > 64 || a.Name.Any(char.IsControl)) throw new InvalidDataException("账户名称不能为空，最长 64 个字符");
        if (a.Username.Length > 128 || a.Username.Any(char.IsControl)) throw new InvalidDataException("用户名过长或包含控制字符");
        if (a.Note.Length > 1000) throw new InvalidDataException("备注过长");
        if (a.TenantName.Length > 128 || a.TenantName.Any(char.IsControl) || a.TenantName.IndexOfAny(['&', '=', '%', '+']) >= 0) throw new InvalidDataException("租户名称过长或包含不允许的字符");
        if (a.CertificatePin.Length > 0 && (a.CertificatePin.Length != 64 || !a.CertificatePin.All(char.IsAsciiHexDigit))) throw new InvalidDataException("证书指纹无效");
        if (a.PortalUrl.Length > 0)
        {
            if (a.PortalUrl.Length > 2048 || !Uri.TryCreate(a.PortalUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
                throw new InvalidDataException("门户地址必须是不含账号密码的 http(s) 地址");
        }
        try { a.Parameters.Validate(); } catch (IOException e) { throw new InvalidDataException(e.Message); }
        if (a.Logins.Count > 50) throw new InvalidDataException("目标主机凭据最多 50 个");
        foreach (var login in a.Logins)
        {
            if (login.Id.Length is 0 or > 64 || !login.Id.All(char.IsAsciiLetterOrDigit)) throw new InvalidDataException("凭据标识无效");
            if (login.Name.Length > 64 || login.Name.Any(char.IsControl)) throw new InvalidDataException("凭据名称最长 64 个字符，不能有控制字符");
            // Sent to the portal as its page sends it, unencoded: nothing that would add a parameter there.
            if (login.User.Trim().Length is 0 or > 128 || login.User.Any(char.IsControl) || login.User.IndexOfAny(['&', '=', '%', '+', '#']) >= 0)
                throw new InvalidDataException("凭据的用户名不能为空，最长 128 个字符，不能有 & = % + # 或控制字符");
        }
        if (a.Logins.Select(l => l.Id).Distinct().Count() != a.Logins.Count) throw new InvalidDataException("凭据标识重复");
        if (a.Choices.Count > 5000 || a.Choices.Any(c => c.Key.Length is 0 or > 64 || c.Value.Length > 200 || c.Key.Any(char.IsControl) || c.Value.Any(char.IsControl)))
            throw new InvalidDataException("记住的选择无效");
    }
}
