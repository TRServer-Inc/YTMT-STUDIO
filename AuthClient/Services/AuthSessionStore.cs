using System.Security.Cryptography;
using System.Text.Json;
using YTMT_STUDIO.AuthClient.Models;

namespace YTMT_STUDIO.AuthClient.Services;

/// <summary>
/// Stores remembered sessions encrypted for the current Windows user by DPAPI.
/// The file never contains a plaintext access or refresh token.
/// </summary>
public sealed class AuthSessionStore
{
    private static readonly byte[] Entropy = "YTMT-STUDIO/AuthSession/v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _sessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YTMT-STUDIO", "Auth", "session.bin");

    public AuthSession? Load()
    {
        if (!File.Exists(_sessionPath))
            return null;

        byte[]? clear = null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(_sessionPath);
            clear = ProtectedData.Unprotect(
                encrypted, Entropy, DataProtectionScope.CurrentUser);

            AuthSession? session = JsonSerializer.Deserialize<AuthSession>(clear, JsonOptions);
            if (session is null ||
                !session.RememberMe ||
                string.IsNullOrWhiteSpace(session.RefreshToken) ||
                session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Clear();
                return null;
            }

            return session;
        }
        catch (Exception ex) when (
            ex is CryptographicException or IOException or UnauthorizedAccessException or
            JsonException or PlatformNotSupportedException or InvalidDataException)
        {
            Clear();
            return null;
        }
        finally
        {
            if (clear is not null)
                CryptographicOperations.ZeroMemory(clear);
        }
    }

    public void Save(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.RememberMe)
        {
            Clear();
            return;
        }

        if (string.IsNullOrWhiteSpace(session.RefreshToken))
            throw new ArgumentException("Refresh token boş olamaz.", nameof(session));

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Süresi geçmiş oturum kaydedilemez.", nameof(session));

        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        string tempPath = _sessionPath + ".tmp";

        try
        {
            byte[] encrypted = ProtectedData.Protect(
                clear, Entropy, DataProtectionScope.CurrentUser);

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            File.WriteAllBytes(tempPath, encrypted);
            CryptographicOperations.ZeroMemory(encrypted);

            if (File.Exists(_sessionPath))
                File.Replace(tempPath, _sessionPath, null);
            else
                File.Move(tempPath, _sessionPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_sessionPath))
                File.Delete(_sessionPath);

            string tempPath = _sessionPath + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (IOException)
        {
            // Local cleanup failures must not crash application shutdown.
        }
        catch (UnauthorizedAccessException)
        {
            // The next startup will reject an unreadable session file.
        }
    }
}