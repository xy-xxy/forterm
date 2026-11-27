using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forterm.Studio.Services.Bastion;

/// <summary>A resource (asset) the portal lists, with the accounts it may be opened with.</summary>
public sealed record FortAsset(string Id, string Name, string Ip, string TypeId, string TypeName, IReadOnlyList<FortAccount> Accounts);

/// <summary>An account of a resource. <c>$user</c> means: the user names the target's account and password when opening
/// (for SSH and SFTP the page treats only <c>$user</c> so; any other account may still need a password, see
/// <see cref="FortPortal.OpenAsync"/>).</summary>
public sealed record FortAccount(string Id, string Name)
{
    public bool AsksForCredentials => Name == "$user";
}

/// <summary>What the user typed for the target host: its account and password (they go to the portal, as the page sends
/// them, and are not kept).</summary>
public sealed record FortCredentials(string User, string Password);

public sealed record FortPage(IReadOnlyList<FortAsset> Assets, int Total, int Pages);

/// <summary>What opens a session: the bastion's own SSH service, with the one-time sid as the user name.</summary>
public sealed record FortTicket(string Sid, string Host, int Port, string Target);

/// <summary>Something the portal refused or said; the message is ours or the portal's own, never a secret.
/// <see cref="SignedOut"/>: the portal no longer knows this login (it sent the login page or a redirect).</summary>
public sealed class FortPortalException(string message, Exception? inner = null, bool signedOut = false) : Exception(message, inner)
{
    public bool SignedOut { get; } = signedOut;
}

/// <summary>The portal's TLS certificate is not the one the user trusted (or none has been yet).</summary>
public sealed class FortCertificateException(string fingerprint, string subject, DateTime notAfter, SslPolicyErrors errors, bool changed)
    : Exception(changed ? "堡垒机网页的证书和上次确认的不同" : "还没有确认过堡垒机网页的证书")
{
    public string Fingerprint { get; } = fingerprint;
    public string Subject { get; } = subject;
    public DateTime NotAfter { get; } = notAfter;
    public SslPolicyErrors Errors { get; } = errors;
    public bool Changed { get; } = changed;
}

/// <summary>A Sangfor-style "fort" bastion portal, as its own pages talk to it (see docs/STUDIO-ALPHA13-DELIVERY.md and the
/// 2026-11-27 recording): log in with user name, SM3 of the password and the app's one-time code (method 34), list the
/// resources, and get a one-time sid for SSH or SFTP, which the bastion's own SSH service takes as the user name (the
/// password is two apostrophes, as the vendor's client passes it). One instance is one login; nothing is kept on disk.
/// One call at a time: the portal's session (its cookie, its tokens, the action token that rotates with every session
/// that is registered) is one, so every public operation waits for the one before it.</summary>
public sealed class FortPortal : IDisposable
{
    public const string LoginMethod = "34";
    const string SshProtocol = "1000000000002", SftpProtocol = "1000000000005", XshellClient = "1000000000002", XftpClient = "1000000000032";

    readonly HttpClient http;
    readonly Uri context;
    readonly string? pinned;
    readonly TimeProvider time;
    readonly SemaphoreSlim gate = new(1, 1);
    string accessToken = "null", actionToken = "";
    bool signedIn;
    string fortIp = "";
    int sshPort;
    X509Certificate2? seen;
    SslPolicyErrors seenErrors;

    /// <param name="portal">The address of the portal as the user gave it (any page of it).</param>
    /// <param name="pinnedFingerprint">SHA-256 of the certificate the user trusted, hex; null: none yet.</param>
    public FortPortal(Uri portal, string? pinnedFingerprint, TimeProvider? time = null)
    {
        if (portal.Scheme != Uri.UriSchemeHttps) throw new FortPortalException("堡垒机网页地址必须是 https://");
        context = new UriBuilder(portal.Scheme, portal.Host, portal.Port, "/fort/").Uri;
        pinned = pinnedFingerprint?.Trim().ToLowerInvariant();
        this.time = time ?? TimeProvider.System;
        var handler = new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = Check },
        };
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Forterm-Studio", StudioPaths.Version.Replace(' ', '-')));
    }

    /// <summary>The bastion's host as the portal's pages use it for sessions (window.location.hostname).</summary>
    public string Host => context.Host;
    public int SshPort => sshPort;

    /// <summary>Trust only the certificate the user confirmed: its SHA-256. A certificate a public authority vouches for is
    /// still shown once, so that a change is noticed.</summary>
    bool Check(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate == null) return false;
        var cert = new X509Certificate2(certificate);
        var before = Interlocked.Exchange(ref seen, cert);
        if (before != null && !ReferenceEquals(before, certificate)) before.Dispose();
        seenErrors = errors;
        return pinned != null && string.Equals(Fingerprint(cert), pinned, StringComparison.Ordinal);
    }

    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexStringLower(SHA256.HashData(cert.RawData));

    // ---------- requests as the portal's script sends them ----------

    Uri Call(string path, bool signed = true)
    {
        string url = context + path;
        if (!signed) return new Uri(url);
        string rd = "rd1=" + FortCrypto.Rd1(time.GetUtcNow()) + "&rd2=" + FortCrypto.Rd2();
        return new Uri(url + (url.Contains('?') ? "&" : "?") + rd);
    }

    /// <summary>jQuery's $.param of an object: each value encoded.</summary>
    static string Form(params (string Name, string Value)[] fields) =>
        string.Join("&", fields.Select(f => Uri.EscapeDataString(f.Name) + "=" + Uri.EscapeDataString(f.Value).Replace("%20", "+")));

    /// <summary>A string the portal's script puts together itself: sent as it is (hex, digits, ids of the portal).</summary>
    static string Raw(params (string Name, string Value)[] fields) => string.Join("&", fields.Select(f => f.Name + "=" + f.Value));

    /// <summary>What the user types goes into such a string unencoded, as the page does: whatever would add or change a
    /// parameter there is refused instead.</summary>
    static void Plain(string value, string what)
    {
        if (value.IndexOfAny(['&', '=', '%', '+', '#', '\r', '\n']) >= 0 || value.Any(char.IsControl))
            throw new FortPortalException($"{what}里有 & = % + # 或控制字符，堡垒机网页不能这样提交");
    }

    async Task<JsonElement> PostAsync(string path, string body, CancellationToken ct, bool signed = true, string? action = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Call(path, signed)) { Content = new StringContent(body, Encoding.UTF8) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded") { CharSet = "UTF-8" };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("access_token", accessToken);
        if (action != null) request.Headers.TryAddWithoutValidation("action_token", action);
        using var response = await SendAsync(request, ct);
        if (response.Headers.TryGetValues("action_token", out var rotated) && rotated.FirstOrDefault() is { Length: > 0 } next) actionToken = next;
        string text = await ReadAsync(response, ct);
        if ((int)response.StatusCode is >= 300 and < 400) throw new FortPortalException("堡垒机要求重新登录（会话已过期或被注销）", signedOut: true);
        if (!response.IsSuccessStatusCode) throw new FortPortalException($"堡垒机网页返回 {(int)response.StatusCode}（{path.Split('?')[0]}）");
        try
        {
            using var document = JsonDocument.Parse(text.Length == 0 ? "{}" : text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
            var root = document.RootElement.Clone();
            Remember(root);
            return root;
        }
        catch (JsonException e)
        {
            if (text.Contains("<html", StringComparison.OrdinalIgnoreCase)) throw new FortPortalException("堡垒机要求重新登录（返回了网页而不是数据）", e, signedOut: true);
            throw new FortPortalException("堡垒机网页返回了无法识别的内容（" + path.Split('?')[0] + "）", e);
        }
    }

    async Task<string> GetPageAsync(string path, CancellationToken ct, bool signed = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Call(path, signed));
        using var response = await SendAsync(request, ct);
        if ((int)response.StatusCode is >= 300 and < 400) throw new FortPortalException("堡垒机要求重新登录（会话已过期或被注销）", signedOut: true);
        if (!response.IsSuccessStatusCode) throw new FortPortalException($"堡垒机网页返回 {(int)response.StatusCode}（{path.Split('?')[0]}）");
        return await ReadAsync(response, ct);
    }

    async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException e) when (seen != null && (pinned == null || !string.Equals(Fingerprint(seen), pinned, StringComparison.Ordinal)))
        {
            _ = e;
            throw new FortCertificateException(Fingerprint(seen), seen.Subject, seen.NotAfter, seenErrors, changed: pinned != null);
        }
        catch (HttpRequestException e) { throw new FortPortalException("连不上堡垒机网页：" + e.Message, e); }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested) { throw new FortPortalException("堡垒机网页没有在 30 秒内回应", e); }
    }

    /// <summary>At most 4 MiB: a list page is a few KiB.</summary>
    static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + n > 4 * 1024 * 1024) throw new FortPortalException("堡垒机网页返回的内容超过 4 MiB，没有读取");
            buffer.Write(chunk, 0, n);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>The tokens the portal hands out (kept in the browser's localStorage): the access token goes with every call.</summary>
    void Remember(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        if (Text(root, "accessToken") is { Length: > 0 } access) accessToken = access;
        if (Text(root, "actionToken") is { Length: > 0 } action) actionToken = action;
    }

    static string Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Number => v.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => "" } : "";

    static string Errors(JsonElement root)
    {
        if (Text(root, "errorMessage") is { Length: > 0 } message) return Short(message);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errorList", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
            return Short(Text(list[0], "error") is { Length: > 0 } e ? e : "登录被拒绝");
        return "";
    }

    /// <summary>Text from the portal as it may be shown: no control or format characters, at most 200.</summary>
    static string Short(string text)
    {
        var kept = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.PrivateUse) continue;
            if (kept.Length >= 200) { kept.Append('…'); break; }
            kept.Append(c);
        }
        return kept.ToString().Trim();
    }

    /// <summary>JavaScript's <c>null != x</c> on a field of an answer: there, and not null.</summary>
    static bool Present(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    /// <summary>JavaScript's truth of a field: what the page's <c>if (x)</c> takes as true.</summary>
    static bool Truthy(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.Object or JsonValueKind.Array => true,
            JsonValueKind.String => (v.GetString() ?? "").Length > 0,
            JsonValueKind.Number => v.TryGetDouble(out double d) && d != 0 && !double.IsNaN(d),
            _ => false,
        };

    async Task<T> OneAtATimeAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await work(); }
        finally { gate.Release(); }
    }

    // ---------- login ----------

    /// <summary>Logs in with method 34: user name, password (sent as SM3), and the 6-digit code of the phone app, which
    /// <paramref name="code"/> gives only when the page would ask for it (after the portal was asked to expect it).</summary>
    public Task LoginAsync(string tenant, string user, string password, Func<CancellationToken, Task<string?>> code, CancellationToken ct) =>
        OneAtATimeAsync(async () => { await SignInAsync(tenant, user, password, code, ct); return true; }, ct);

    async Task SignInAsync(string tenant, string user, string password, Func<CancellationToken, Task<string?>> code, CancellationToken ct)
    {
        if (signedIn) throw new FortPortalException("已经登录");
        if (tenant.Length == 0) throw new FortPortalException("没有填租户名称");
        if (user.Length == 0 || password.Length == 0) throw new FortPortalException("没有账号或密码");
        Plain(user, "用户名");
        Plain(tenant, "租户名称");
        string page = await GetPageAsync("login", ct);
        string r = Regex.Match(page, @"id=[""']r[""'][^>]*value=[""'](\d{1,10})[""']|value=[""'](\d{1,10})[""'][^>]*id=[""']r[""']") is { Success: true } m ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : "";
        // What the page sends when it opens: the time, its random number and where it is (login/abc is not signed).
        string ms = time.GetUtcNow().ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        await PostAsync("login/abc", Form(("abc", ms.Substring(2, 8)), ("r", r), ("haref", Convert.ToBase64String(Encoding.UTF8.GetBytes(context.ToString())))), ct, signed: false);

        var t = await PostAsync("login/login_tenant", Form(("fortTenantName", tenant)), ct);
        if (Text(t, "checkTenantName") != "1") throw new FortPortalException("租户名称不对（堡垒机网页这样说）");
        string tenantName = Text(t, "fortTenantName"), tenantId = Text(t, "fortTenantId"), tenantReal = Text(t, "fortTenantIdReal");

        var auth = await PostAsync("login/get_auth", Form(("fortTenantId", tenantReal)), ct);
        var methods = auth.ValueKind == JsonValueKind.Object && auth.TryGetProperty("contentsAuths", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText()).ToList() : [];
        if (methods.Count > 0 && !methods.Any(x => x.StartsWith(LoginMethod + "-", StringComparison.Ordinal)))
            throw new FortPortalException("这个租户不提供“用户名+密码+手机APP动态口令”登录；可用的是：" + Short(string.Join("、", methods.Select(x => x.Contains('-') ? x[(x.IndexOf('-') + 1)..] : x))));
        await PostAsync("login/get_code", Form(("code", LoginMethod), ("fortTenantId", tenantReal)), ct);
        // Asked only now, as on the page: a code typed before the certificate was confirmed would have run out.
        string answer = (await code(ct))?.Trim() ?? "";
        if (answer.Length == 0) throw new FortPortalException("没有输入动态口令，没有登录");
        if (answer.Length != 6 || !answer.All(char.IsAsciiDigit)) throw new FortPortalException("动态口令应是 6 位数字");

        string random = (Random.Shared.NextDouble() * 100).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        // Once sent, the login is waited for (within the 30 s of every call) even when the window closes meanwhile: a login
        // the portal let in must be known, so that it can be left again.
        var login = await PostAsync("login/do_login", Raw(("loginRandom", random), ("loginMethod", LoginMethod), ("username", user), ("pwd", FortCrypto.Sm3Hex(password)),
            ("cellPhonePwd", FortCrypto.Encode64(answer)), ("fortTenantName", tenantName), ("fortTenantId", tenantId), ("fortTenantIdReal", tenantReal)), CancellationToken.None);
        if (Errors(login) is { Length: > 0 } refused) throw new FortPortalException("登录失败：" + refused);
        if (Text(login, "success") != "success") throw new FortPortalException("登录失败（堡垒机网页没有说明原因）");
        signedIn = true;
        // From here on the portal has a login: whatever ends this (a refusal, a failure, the window closing) leaves it again.
        try { ct.ThrowIfCancellationRequested(); await AfterLoginAsync(login, ct); }
        catch { await LeaveAsync(); throw; }
    }

    async Task AfterLoginAsync(JsonElement login, CancellationToken ct)
    {
        // What the page does with the answer, in its order; where it stops, Studio stops too.
        bool admin = Text(login, "isAdmin") == "1";
        string? stop = (Present(login, "fortInitializePassword") || Text(login, "fortPeriod") == "1") && !admin ? "堡垒机要求先修改密码（首次登录），请在网页上登录一次改好"
            : Text(login, "fortPeriod") == "2" && !admin ? "堡垒机要求先修改密码（密码超过有效期），请在网页上登录一次改好"
            : Text(login, "fortAdmin") == "1" && !admin ? "堡垒机要求先修改密码（初始化用户），请在网页上登录一次改好"
            : Present(login, "fortAdminIp") ? "堡垒机没有让这次登录继续：" + (Short(Text(login, "fortAdminIp")) is { Length: > 0 } said ? said : "（没有说明）")
            : Text(login, "changePasswordStrategy") == "1" && !admin ? "堡垒机要求先修改密码（不符合当前密码策略），请在网页上登录一次改好"
            : null;
        if (stop != null) throw new FortPortalException(stop);

        // The page hands its answer to isWarn; "msg"/"delMsg" opens "用户会话信息" there instead of going on.
        var warn = await PostAsync("login/isWarn", login.GetRawText(), ct);
        if (Text(warn, "isWarn") is "msg" or "delMsg")
            throw new FortPortalException("堡垒机要先处理这个账号的其他登录会话（网页上会弹出“用户会话信息”），本程序不代为处理。请在浏览器里登录一次处理后再试");
        await PostAsync("login/setReload2", "", ct);
        await GetPageAsync("", ct);
        string sso = await GetPageAsync("operation/sso/sso?checkInit=1", ct, signed: true);
        fortIp = Regex.Match(sso, @"fort_ip\s*=\s*[""']([0-9A-Za-z.:\[\]_-]{1,255})[""']") is { Success: true } ip ? ip.Groups[1].Value : Host;
        var port = await PostAsync("sso/get_sshd_port", "", ct);
        sshPort = int.TryParse(Text(port, "sshd_port"), out int p) && p is > 0 and < 65536 ? p : 0;
        if (sshPort == 0) throw new FortPortalException("堡垒机网页没有给出 SSH 端口");
    }

    /// <summary>The page's logout, best effort, for a login Studio does not go on with.</summary>
    async Task LeaveAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await PostAsync("login/do_logout", "", limit.Token); }
        catch (Exception e) when (e is FortPortalException or OperationCanceledException or FortCertificateException or ObjectDisposedException or HttpRequestException) { }
        signedIn = false;
        sshPort = 0;
    }

    // ---------- resources ----------

    public Task<FortPage> ListAsync(string query, int page, int size, CancellationToken ct) => OneAtATimeAsync(() => FindAsync(query, page, size, ct), ct);

    async Task<FortPage> FindAsync(string query, int page, int size, CancellationToken ct)
    {
        if (sshPort == 0) throw new FortPortalException("还没有登录堡垒机");
        var tree = await PostAsync("sso/list_department_tree", "", ct);
        var ids = tree.ValueKind == JsonValueKind.Object && tree.TryGetProperty("fortTree", out var nodes) && nodes.ValueKind == JsonValueKind.Array
            ? nodes.EnumerateArray().Select(n => Text(n, "id")).Where(id => id.Length > 0).ToList() : [];
        string path = "sso/list_sso?" + Form(("fortSsoQuery.fortResourceNameOrIp", query.Trim()), ("fortSsoQuery.fortResourceType", "-1"),
            ("fortSsoQuery.fortDepartmentsize", Math.Max(1, ids.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)), ("fortSsoQuery.fortDepartmentIds", string.Join(",", ids)), ("isQuickQuery", query.Trim().Length > 0 ? "1" : "0"));
        var listed = await PostAsync(path, Form(("pagination.pageIndex", page.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("pagination.pageSize", size.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("example.orderByClause", "fort_resource_name asc")), ct);
        if (Errors(listed) is { Length: > 0 } refused) throw new FortPortalException("读取资产失败：" + refused);
        if (!listed.TryGetProperty("pagination", out var pagination)) throw new FortPortalException("堡垒机网页没有返回资产列表");
        var assets = new List<FortAsset>();
        if (pagination.TryGetProperty("list", out var rows) && rows.ValueKind == JsonValueKind.Array)
            foreach (var row in rows.EnumerateArray().Take(500))
            {
                var accounts = Text(row, "fortAccounts").Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Split(':', 2)).Where(a => a.Length == 2).Select(a => new FortAccount(a[0], a[1])).ToList();
                assets.Add(new FortAsset(Text(row, "fortResourceId"), Short(Text(row, "fortResourceName")), Short(Text(row, "fortResourceIp")), Text(row, "fortResourceTypeId"), Short(Text(row, "fortResourceTypeName")), accounts));
            }
        int total = int.TryParse(Text(pagination, "recondSum"), out int n) ? n : assets.Count;
        int pages = int.TryParse(Text(pagination, "pageSum"), out int s) ? s : 1;
        return new FortPage(assets, total, Math.Max(1, pages));
    }

    // ---------- sessions ----------

    static readonly Regex SidShape = new(@"^(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$", RegexOptions.CultureInvariant);

    /// <summary>A one-time sid for SSH (<paramref name="files"/> false) or SFTP of <paramref name="asset"/> as
    /// <paramref name="account"/>. As the page does: a <c>$user</c> account asks for the target's account and password; any
    /// other one is first checked with <c>sso/is_no_pass</c>, and where the portal keeps no password for it, its password is
    /// asked (<paramref name="ask"/> with the account's own name). <paramref name="ask"/> returning null opens nothing.</summary>
    public Task<FortTicket> OpenAsync(FortAsset asset, FortAccount account, bool files, Func<FortAccount, bool, CancellationToken, Task<FortCredentials?>> ask, CancellationToken ct) =>
        OneAtATimeAsync(() => TicketAsync(asset, account, files, ask, ct), ct);

    async Task<FortTicket> TicketAsync(FortAsset asset, FortAccount account, bool files, Func<FortAccount, bool, CancellationToken, Task<FortCredentials?>> ask, CancellationToken ct)
    {
        if (sshPort == 0) throw new FortPortalException("还没有登录堡垒机");
        var tools = await PostAsync("sso/get_operator_client_by_resource", Raw(("fortSsoClientToolQuery.fortResoureceId", asset.Id), ("fortSsoClientToolQuery.fortResourceTypeId", asset.TypeId), ("fortSsoClientToolQuery.fortAccountId", account.Id)), ct);
        var client = tools.TryGetProperty("client", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() > 0 ? c[0] : default;
        string clients = Text(client, "fortProtocolClient"), protocols = Text(client, "fortOperationsProtocol");
        string protocol = files ? SftpProtocol : SshProtocol, tool = files ? XftpClient : XshellClient;
        if (protocols.Length > 0 && !protocols.Split('|').Any(x => x.Split(':') is [_, var id, ..] && id == protocol))
            throw new FortPortalException(files ? "这台资产没有开放 SFTP" : "这台资产没有开放 SSH");
        if (clients.Length > 0 && !clients.Split('|').Any(x => x.Split(':')[0] == tool))
            throw new FortPortalException(files ? "堡垒机没有为这台资产配置 Xftp 客户端" : "堡垒机没有为这台资产配置 Xshell 客户端");

        // The page's getSsoCmdData/getSsoFtpData with what its question returned, or with nothing: then it sends the
        // literal "undefined" for both (func[0] of an empty string), and so does Studio.
        string targetUser = "undefined", pw = "undefined";
        bool asks = account.AsksForCredentials;
        if (!asks)
        {
            var noPass = await PostAsync("sso/is_no_pass", Form(("fortIp", fortIp), ("accountId", account.Id)), ct);
            asks = Truthy(noPass, "isNoPass");
        }
        if (asks)
        {
            var typed = await ask(account, !account.AsksForCredentials, ct);
            if (typed == null) throw new FortPortalException("没有打开（没有填写目标主机的账号和密码）");
            if (typed.Password.Length == 0) throw new FortPortalException("密码不能为空");
            targetUser = account.AsksForCredentials ? typed.User : account.Name;
            if (targetUser.Length == 0) throw new FortPortalException("没有填写目标主机的账号");
            Plain(targetUser, "目标主机的账号");
            pw = Uri.EscapeDataString(Uri.EscapeDataString(FortCrypto.Encode64(typed.Password)));
        }
        var body = files
            ? Raw(("fortSsoVoQuery.fortResoureceId", asset.Id), ("fortSsoVoQuery.fortAccountId", account.Id), ("fortSsoVoQuery.fortOperationsProtocolId", protocol), ("fortSsoVoQuery.fortIp", fortIp), ("selectResourceIp", asset.Ip), ("fortSsoVoQuery.fortClientToolId", tool), ("ssoAccount", targetUser), ("ssoPwd", pw))
            : Raw(("fortSsoVoQuery.fortResoureceId", asset.Id), ("fortSsoVoQuery.fortAccountId", account.Id), ("fortSsoVoQuery.fortIp", fortIp), ("fortSsoVoQuery.fortOperationsProtocolId", protocol), ("selectResourceIp", asset.Ip), ("fortSsoVoQuery.clientId", tool), ("ssoAccount", targetUser), ("ssoPwd", pw));
        var answer = await PostAsync(files ? "sso/get_sso_ftp" : "sso/get_sso_cmd", body, ct);
        if (Errors(answer) is { Length: > 0 } refused) throw new FortPortalException("堡垒机没有给出会话：" + refused);
        if (!answer.TryGetProperty("fortSsoVoQuery", out var vo) || vo.ValueKind != JsonValueKind.Object) throw new FortPortalException("堡垒机没有给出会话（协议代理异常）");
        string sid = Text(vo, "sid");
        if (!SidShape.IsMatch(sid)) throw new FortPortalException("堡垒机给出的会话编号不是预期的格式，没有打开");
        var registered = await PostAsync("process/set_double_approval_sid", Form(("sid", sid), ("fortAccountId", Text(vo, "fortAccountId") is { Length: > 0 } a ? a : account.Id)), ct, action: actionToken);
        if (Errors(registered) is { Length: > 0 } notRegistered) throw new FortPortalException("堡垒机没有登记这次会话：" + notRegistered);
        return new FortTicket(sid, Host, sshPort, asset.Ip.Length > 0 ? asset.Ip : asset.Name);
    }

    /// <summary>Leaves the portal (its logout), when this instance is logged in; waits for a call that is still running.</summary>
    public Task LogoutAsync(CancellationToken ct) => OneAtATimeAsync(async () => { if (signedIn) await LeaveAsync(); return true; }, ct);

    public bool IsSignedIn => signedIn;

    public void Dispose() { http.Dispose(); seen?.Dispose(); }
}
