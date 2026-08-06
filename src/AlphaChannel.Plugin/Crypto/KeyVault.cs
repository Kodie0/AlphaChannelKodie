using System.Security.Cryptography;
using AlphaChannel.Plugin.Auth;

namespace AlphaChannel.Plugin.Crypto;

// Owns the local DM identity keypair: generates one on first use, persists the private half in
// Configuration.DmPrivateKeys (DPAPI-protected on Windows), uploads the public half to the server,
// and caches other accounts' public keys fetched during a session (their public keys aren't
// sensitive, no need to persist them locally - refetching is cheap and always current).
internal sealed class KeyVault(Configuration configuration, KeysClient keysClient)
{
    private readonly Dictionary<string, ECDiffieHellman> identityCache = new();
    private readonly Dictionary<string, ECDiffieHellman> otherPartyCache = new();

    internal async Task<ECDiffieHellman> EnsureIdentityAsync(string accountId, string bearerToken)
    {
        if (identityCache.TryGetValue(accountId, out var cached))
        {
            return cached;
        }

        ECDiffieHellman key;
        if (configuration.DmPrivateKeys.TryGetValue(accountId, out var storedBase64))
        {
            key = ECDiffieHellman.Create();
            key.ImportPkcs8PrivateKey(Unprotect(Convert.FromBase64String(storedBase64)), out _);
        }
        else
        {
            key = DmCipher.GenerateIdentity();
            configuration.DmPrivateKeys[accountId] = Convert.ToBase64String(Protect(key.ExportPkcs8PrivateKey()));
            configuration.Save();

            var publicKeyBase64 = Convert.ToBase64String(DmCipher.ExportPublicKey(key));
            await keysClient.UploadPublicKeyAsync(bearerToken, publicKeyBase64).ConfigureAwait(false);
        }

        identityCache[accountId] = key;
        return key;
    }

    internal async Task<ECDiffieHellman?> GetOtherPartyKeyAsync(string otherAccountId, string bearerToken)
    {
        if (otherPartyCache.TryGetValue(otherAccountId, out var cached))
        {
            return cached;
        }

        var publicKeyBase64 = await keysClient.GetPublicKeyAsync(bearerToken, otherAccountId).ConfigureAwait(false);
        if (publicKeyBase64 is null)
        {
            return null;
        }

        var key = DmCipher.ImportPublicKey(Convert.FromBase64String(publicKeyBase64));
        otherPartyCache[otherAccountId] = key;
        return key;
    }

    // DPAPI (Windows-only) as defense-in-depth against other local processes reading the plugin's
    // config file - not a network protection, and not the whole story: a real recovery-code-backed
    // scheme (so a private key isn't silently unrecoverable if this file is ever lost, and so
    // non-Windows/Wine users get equivalent protection instead of a plain fallback) is real future
    // work, deliberately out of scope here - see the coordinator's notes on this tradeoff.
    private static byte[] Protect(byte[] data)
    {
        try
        {
            return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        }
        catch (PlatformNotSupportedException)
        {
            return data;
        }
    }

    private static byte[] Unprotect(byte[] data)
    {
        try
        {
            return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or CryptographicException)
        {
            // Either DPAPI isn't available on this platform, or the bytes were never protected in
            // the first place (the PlatformNotSupportedException fallback path above) - either way,
            // treat as already-raw PKCS8.
            return data;
        }
    }
}
