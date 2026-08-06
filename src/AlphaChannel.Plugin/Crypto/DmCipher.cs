using System.Security.Cryptography;
using System.Text;

namespace AlphaChannel.Plugin.Crypto;

// Static-static ECDH (NIST P-521, the strongest curve .NET's BCL ships - no third-party crypto
// dependency needed) between the two conversation participants' long-term identity keys derives
// the same AES-256-GCM key on both ends independently; nothing is ever wrapped, rotated, or stored
// server-side beyond the two public keys (see AlphaChannel.Server's DmMessage doc comment). This
// means no forward secrecy (the same key covers the whole conversation's history unless a party
// regenerates their identity key) - an accepted v1 simplification, not an oversight.
//
// Every message embeds a random 32-byte franking key ahead of the plaintext before encrypting, and
// ships a separate HMAC-SHA512 "commitment tag" (of the franking key over the plaintext) alongside
// the ciphertext in the clear. The server stores that tag but can never compute it itself. If this
// message is later reported, revealing the plaintext + franking key lets the server verify the
// reveal is genuine without ever having had the ability to decrypt on its own - see ReportEndpoints.
internal static class DmCipher
{
    private const int FrankingKeyLength = 32;
    private const int AesKeyLength = 32; // AES-256
    private const int NonceLength = 12;
    private const int TagLength = 16;

    internal static ECDiffieHellman GenerateIdentity() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521);

    internal static byte[] ExportPublicKey(ECDiffieHellman key) => key.ExportSubjectPublicKeyInfo();

    internal static ECDiffieHellman ImportPublicKey(byte[] spki)
    {
        var key = ECDiffieHellman.Create();
        key.ImportSubjectPublicKeyInfo(spki, out _);
        return key;
    }

    // SHA-512-based NIST SP 800-56A Concat KDF (built into ECDiffieHellman.DeriveKeyFromHash) -
    // truncated to 32 bytes for the AES-256 key. Deliberately the strongest hash the BCL offers for
    // this derivation rather than SHA-256, per the "strongest available everywhere" steer.
    private static byte[] DeriveSharedKey(ECDiffieHellman myPrivate, ECDiffieHellman otherPublic) =>
        myPrivate.DeriveKeyFromHash(otherPublic.PublicKey, HashAlgorithmName.SHA512)[..AesKeyLength];

    internal sealed record Sealed(byte[] Ciphertext, byte[] Nonce, byte[] Tag, byte[] CommitmentTag);

    internal static Sealed Encrypt(ECDiffieHellman myPrivate, ECDiffieHellman otherPublic, string plaintext)
    {
        var sharedKey = DeriveSharedKey(myPrivate, otherPublic);
        var frankingKey = RandomNumberGenerator.GetBytes(FrankingKeyLength);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        var payload = new byte[FrankingKeyLength + plaintextBytes.Length];
        frankingKey.CopyTo(payload, 0);
        plaintextBytes.CopyTo(payload, FrankingKeyLength);

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[TagLength];
        using (var gcm = new AesGcm(sharedKey, TagLength))
        {
            gcm.Encrypt(nonce, payload, ciphertext, tag);
        }

        var commitmentTag = HMACSHA512.HashData(frankingKey, plaintextBytes);
        return new Sealed(ciphertext, nonce, tag, commitmentTag);
    }

    internal sealed record Opened(string Plaintext, byte[] FrankingKey);

    // Returns null on any failure (wrong key, tampered ciphertext, corrupt data) rather than
    // throwing - callers show "couldn't decrypt this message" instead of crashing the draw loop.
    internal static Opened? Decrypt(ECDiffieHellman myPrivate, ECDiffieHellman otherPublic, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        try
        {
            var sharedKey = DeriveSharedKey(myPrivate, otherPublic);
            var payload = new byte[ciphertext.Length];
            using (var gcm = new AesGcm(sharedKey, TagLength))
            {
                gcm.Decrypt(nonce, ciphertext, tag, payload);
            }

            var frankingKey = payload[..FrankingKeyLength];
            var plaintext = Encoding.UTF8.GetString(payload[FrankingKeyLength..]);
            return new Opened(plaintext, frankingKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
