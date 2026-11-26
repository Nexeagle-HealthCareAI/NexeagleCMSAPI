using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// Encrypts the Meta token and secrets kept in WaConnection (AES-256-GCM, "v1:" + base64 of nonce|tag|data).
/// The key is WhatsAppMarketing__EncryptionKey (base64 of 32 bytes) when set; otherwise it is derived from the
/// API's sign-in key (TokenSettings__Key), which every environment already has. Changing that key makes the
/// stored secrets unreadable: the Setup page then asks for them again. Nothing else is lost.
/// </summary>
public class WaSecretBox
{
    private const string Prefix = "v1:";
    private readonly byte[]? _key;

    public WaSecretBox(IConfiguration config)
    {
        var own = config["WhatsAppMarketing:EncryptionKey"];
        if (!string.IsNullOrWhiteSpace(own))
        {
            try
            {
                var k = Convert.FromBase64String(own.Trim());
                if (k.Length == 32) { _key = k; return; }
            }
            catch (FormatException) { }
            throw new InvalidOperationException("WhatsAppMarketing__EncryptionKey must be base64 of 32 random bytes (openssl rand -base64 32).");
        }
        var signing = config["TokenSettings:Key"];
        if (!string.IsNullOrWhiteSpace(signing))
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(signing), 32, info: Encoding.UTF8.GetBytes("nexeagle-cms/whatsapp-marketing/secrets/v1"));
    }

    /// <summary>For tests.</summary>
    public WaSecretBox(byte[] key) { _key = key; }

    public bool CanSeal => _key != null;

    public string Seal(string plain)
    {
        if (_key == null) throw new InvalidOperationException("No key to encrypt the WhatsApp secrets: set TokenSettings__Key or WhatsAppMarketing__EncryptionKey.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, 16)) aes.Encrypt(nonce, data, cipher, tag);
        return Prefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    /// <summary>The secret, or null when there is none; throws CryptographicException when it cannot be read.</summary>
    public string? Open(string? sealedText)
    {
        if (string.IsNullOrEmpty(sealedText)) return null;
        if (_key == null || !sealedText.StartsWith(Prefix)) throw new CryptographicException("Unreadable WhatsApp secret.");
        var all = Convert.FromBase64String(sealedText[Prefix.Length..]);
        if (all.Length < 28) throw new CryptographicException("Unreadable WhatsApp secret.");
        var plain = new byte[all.Length - 28];
        using (var aes = new AesGcm(_key, 16)) aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
