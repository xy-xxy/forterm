using System.Text.Json;
using Forterm.Core;

namespace Forterm.Studio.Library;

/// <summary>Bastion account (堡垒机账户): portal address and username for reference, with the
/// password and the authenticator (TOTP) seed kept only in the OS credential store. Studio never
/// contacts the portal or logs in with these; they are shown and copied on request.</summary>
public sealed record BastionAccount
{
    public string Id { get; init; } = StudioProfile.NewId();
    public string Name { get; init; } = "";
    public string PortalUrl { get; init; } = "";
    public string Username { get; init; } = "";
    public string Note { get; init; } = "";
    public bool HasPassword { get; init; }
    public bool HasSeed { get; init; }
    public string Algorithm { get; init; } = "SHA1";
    public int Digits { get; init; } = 6;
    public int Period { get; init; } = 30;

    public TotpParameters Parameters => new(Algorithm, Digits, Period);
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
            try
            {
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(next, Json));
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
        if (a.PortalUrl.Length > 0)
        {
            if (a.PortalUrl.Length > 2048 || !Uri.TryCreate(a.PortalUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
                throw new InvalidDataException("门户地址必须是不含账号密码的 http(s) 地址");
        }
        try { a.Parameters.Validate(); } catch (IOException e) { throw new InvalidDataException(e.Message); }
    }
}
