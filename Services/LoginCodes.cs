using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Loom.Services;

public sealed class LoginCodes(IMemoryCache cache)
{
    public const string CookieName = "Loom.LoginChallenge";
    public static TimeSpan Lifetime => TimeSpan.FromMinutes(5);

    public static string NewCode() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");

    public string Create(string code, string userId, string securityStamp, bool rememberMe, string? returnUrl)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set((CookieName, key), new Entry(Hash(code), new Login(userId, securityStamp, rememberMe, returnUrl)), Lifetime);
        return key;
    }

    public bool Exists(string? key) =>
        key is { Length: 64 } && cache.TryGetValue((CookieName, key), out _);

    public bool HasDeliveryFailed(string? key) =>
        key is { Length: 64 } && cache.TryGetValue<Entry>((CookieName, key), out var entry) &&
        entry?.DeliveryFailed == true;

    public void Fail(string? key)
    {
        if (key is { Length: 64 } && cache.TryGetValue<Entry>((CookieName, key), out var entry) && entry != null)
            entry.DeliveryFailed = true;
    }

    public void Remove(string? key)
    {
        if (key is { Length: 64 })
            cache.Remove((CookieName, key));
    }

    public Login? Verify(string? key, string code)
    {
        if (key is not { Length: 64 } ||
            !cache.TryGetValue<Entry>((CookieName, key), out var entry) || entry == null)
            return null;

        lock (entry)
        {
            if (entry.DeliveryFailed || entry.Remaining <= 0 || !Exists(key))
                return null;

            entry.Remaining--;
            if (CryptographicOperations.FixedTimeEquals(entry.CodeHash, Hash(code)))
            {
                entry.Remaining = 0;
                Remove(key);
                return entry.Login;
            }

            if (entry.Remaining == 0)
                Remove(key);

            return null;
        }
    }

    private static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

    public sealed record Login(string UserId, string SecurityStamp, bool RememberMe, string? ReturnUrl);

    private sealed record Entry(byte[] CodeHash, Login Login)
    {
        public int Remaining { get; set; } = 5;
        public bool DeliveryFailed { get; set; }
    }
}
