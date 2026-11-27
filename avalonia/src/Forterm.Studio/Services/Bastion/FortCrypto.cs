using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Forterm.Studio.Services.Bastion;

/// <summary>What the fort portal's own scripts do to a request before it is sent (2026-11-27 recording of a Sangfor
/// "fort" portal): the <c>rd1</c>/<c>rd2</c> parameters of every call (<c>myajax.js</c>), the "encode64" of one-time codes
/// (<c>snmpEncode.js</c>), and SM3 of the password (GB/T 32905, lower-case hex, as the login page sends it).</summary>
public static class FortCrypto
{
    /// <summary>"0712" as the portal's script rearranges it: the second digit of each pair.</summary>
    const string Key = "02270171";

    static readonly string[] Pairs = BuildPairs();

    static string[] BuildPairs()
    {
        // The table of encyptyArray(): two digits → four letters and digits.
        string[] rows = ["a?b?", "e?f?", "c?d?", "g?h?", "k?l?", "i?j?", "m?n?", "q?r?", "o?p?", "s?t?"];
        // The second digit runs down in the first half, and up or the same down again in the second, per row.
        bool[] mirrored = [true, true, false, false, false, true, true, true, false, false];
        var pairs = new string[100];
        for (int tens = 0; tens < 10; tens++)
            for (int ones = 0; ones < 10; ones++)
            {
                string row = rows[tens];
                char first = (char)('0' + 9 - ones), second = mirrored[tens] ? (char)('0' + ones) : (char)('0' + 9 - ones);
                pairs[tens * 10 + ones] = $"{row[0]}{first}{row[2]}{second}";
            }
        return pairs;
    }

    /// <summary>encrypt() of <c>myajax.js</c>: eight digits to 32 characters.</summary>
    public static string Encrypt(string digits)
    {
        if (digits.Length != 8 || !digits.All(char.IsAsciiDigit)) throw new ArgumentException("需要 8 位数字", nameof(digits));
        var joined = new StringBuilder(32);
        for (int i = 0; i < 8; i++) joined.Append(Pairs[(digits[i] - '0') * 10 + (Key[i] - '0')]);
        string r = joined.ToString();
        r = r[28..31] + r[3..28] + r[0..3] + r[31..];
        r = r[0..7] + r[15..18] + r[10..15] + r[7..10] + r[18..];
        const int s = 7; // parseInt("0712".substring(1, 2))
        return r[0..s] + r[31..] + r[(s + 1)..31] + r[s..(s + 1)];
    }

    /// <summary>The inverse, for tests: what <see cref="Encrypt"/> was given.</summary>
    internal static string Decrypt(string text)
    {
        if (text.Length != 32) throw new ArgumentException("需要 32 个字符", nameof(text));
        const int s = 7;
        // Undo the last step: r[0..7] + r[31] + r[8..31) + r[7]
        string r = text[0..s] + text[31..] + text[(s + 1)..31] + text[s..(s + 1)];
        // Undo r = r[0..7) + r[15..18) + r[10..15) + r[7..10) + r[18..]: positions 7..18 hold old 15,16,17,10..14,7,8,9.
        r = r[0..7] + r[15..18] + r[10..15] + r[7..10] + r[18..];
        // Undo r = r[28..31) + r[3..28) + r[0..3) + r[31..]
        r = r[28..31] + r[3..28] + r[0..3] + r[31..];
        var digits = new StringBuilder(8);
        for (int i = 0; i < 8; i++)
        {
            int index = Array.IndexOf(Pairs, r.Substring(i * 4, 4));
            if (index < 0 || index % 10 != Key[i] - '0') throw new FormatException("不是这种签名");
            digits.Append((char)('0' + index / 10));
        }
        return digits.ToString();
    }

    /// <summary>The <c>rd1</c> of a call: digits 3 to 10 of the time in milliseconds since 1970.</summary>
    public static string Rd1(DateTimeOffset now) => Encrypt(now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture).Substring(2, 8));

    /// <summary>The <c>rd2</c> of a call: eight random digits.</summary>
    public static string Rd2()
    {
        Span<char> digits = stackalloc char[8];
        for (int i = 0; i < 8; i++) digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return Encrypt(new string(digits));
    }

    /// <summary>encode64() of <c>snmpEncode.js</c>: base64 of the UTF-8, every third character followed by the one before it,
    /// all reversed, and "+" written as "%2B" (the caller sends it as it is).</summary>
    public static string Encode64(string text)
    {
        string b = Convert.ToBase64String(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")));
        var e = new StringBuilder(b.Length + b.Length / 3 + 1);
        for (int i = 0; i < b.Length; i++)
        {
            e.Append(b[i]);
            if (i % 3 == 0 && i > 0) e.Append(b[i - 1]);
        }
        var chars = e.ToString().ToCharArray();
        Array.Reverse(chars);
        return new string(chars).Replace("+", "%2B");
    }

    /// <summary>SM3 (GB/T 32905-2016) of the UTF-8 of <paramref name="text"/>, lower-case hex.</summary>
    public static string Sm3Hex(string text) => Convert.ToHexStringLower(Sm3(Encoding.UTF8.GetBytes(text)));

    public static byte[] Sm3(ReadOnlySpan<byte> message)
    {
        uint[] v = [0x7380166F, 0x4914B2B9, 0x172442D7, 0xDA8A0600, 0xA96F30BC, 0x163138AA, 0xE38DEE4D, 0xB0FB0E4E];
        long bits = (long)message.Length * 8;
        int padded = ((message.Length + 9 + 63) / 64) * 64;
        var data = new byte[padded];
        message.CopyTo(data);
        data[message.Length] = 0x80;
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(padded - 8), bits);
        Span<uint> w = stackalloc uint[68];
        Span<uint> w1 = stackalloc uint[64];
        for (int block = 0; block < padded; block += 64)
        {
            for (int j = 0; j < 16; j++) w[j] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(block + j * 4));
            for (int j = 16; j < 68; j++)
                w[j] = P1(w[j - 16] ^ w[j - 9] ^ uint.RotateLeft(w[j - 3], 15)) ^ uint.RotateLeft(w[j - 13], 7) ^ w[j - 6];
            for (int j = 0; j < 64; j++) w1[j] = w[j] ^ w[j + 4];
            uint a = v[0], b = v[1], c = v[2], d = v[3], e = v[4], f = v[5], g = v[6], h = v[7];
            for (int j = 0; j < 64; j++)
            {
                uint t = j < 16 ? 0x79CC4519u : 0x7A879D8Au;
                uint ss1 = uint.RotateLeft(uint.RotateLeft(a, 12) + e + uint.RotateLeft(t, j % 32), 7);
                uint ss2 = ss1 ^ uint.RotateLeft(a, 12);
                uint ff = j < 16 ? a ^ b ^ c : (a & b) | (a & c) | (b & c);
                uint gg = j < 16 ? e ^ f ^ g : (e & f) | (~e & g);
                uint tt1 = ff + d + ss2 + w1[j];
                uint tt2 = gg + h + ss1 + w[j];
                d = c; c = uint.RotateLeft(b, 9); b = a; a = tt1;
                h = g; g = uint.RotateLeft(f, 19); f = e; e = P0(tt2);
            }
            v[0] ^= a; v[1] ^= b; v[2] ^= c; v[3] ^= d; v[4] ^= e; v[5] ^= f; v[6] ^= g; v[7] ^= h;
        }
        var digest = new byte[32];
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i * 4), v[i]);
        return digest;

        static uint P0(uint x) => x ^ uint.RotateLeft(x, 9) ^ uint.RotateLeft(x, 17);
        static uint P1(uint x) => x ^ uint.RotateLeft(x, 15) ^ uint.RotateLeft(x, 23);
    }
}
