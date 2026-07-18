using System;
using System.Security.Cryptography;
using System.Text;

namespace IMVUCompanion;

/// <summary>
/// Protect secrets at rest with Windows DPAPI (CurrentUser scope).
/// Values are only decryptable by the same Windows user on this machine.
/// </summary>
internal static class SecretProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
            return "";
        // Already protected (should not normally happen for in-memory values)
        if (plain.StartsWith(Prefix, StringComparison.Ordinal))
            return plain;

        byte[] data = Encoding.UTF8.GetBytes(plain);
        byte[] protectedBytes = ProtectedData.Protect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return "";

        // Legacy plaintext from older builds — still load, re-protect on next save
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(stored.Substring(Prefix.Length));
            byte[] data = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (CryptographicException)
        {
            // Wrong user/machine or corrupted blob
            return "";
        }
        catch (FormatException)
        {
            return "";
        }
    }

    public static bool IsProtected(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);
}
