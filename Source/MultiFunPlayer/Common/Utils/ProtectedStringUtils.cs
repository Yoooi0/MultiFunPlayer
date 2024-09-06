using System.Security.Cryptography;
using System.Text;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace MultiFunPlayer.Common;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public static class ProtectedStringUtils
{
    public static bool TryProtect(string decrypted, out string encrypted)
    {
        encrypted = null;

        try { encrypted = Protect(decrypted); }
        catch { }

        return encrypted != null;
    }

    public static string Protect(string decrypted)
    {
        if (string.IsNullOrWhiteSpace(decrypted))
            return null;

        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(decrypted), null, DataProtectionScope.CurrentUser));
    }

    public static string Protect(string decrypted, Action<Exception> exceptionCallback)
    {
        try
        {
            return Protect(decrypted);
        }
        catch (Exception e)
        {
            exceptionCallback(e);
            return null;
        }
    }

    public static bool TryUnprotect(string encrypted, out string decrypted)
    {
        decrypted = null;

        try { decrypted = Unprotect(encrypted); }
        catch { }

        return decrypted != null;
    }

    public static string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return null;

        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string encrypted, Action<Exception> exceptionCallback)
    {
        try
        {
            return Unprotect(encrypted);
        }
        catch (Exception e)
        {
            exceptionCallback(e);
            return null;
        }
    }
}
