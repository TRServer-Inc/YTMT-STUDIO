using System.Security.Cryptography;
using System.Text;

namespace YTMT_STUDIO.Services;

/// <summary>
/// Stores the Render API key encrypted with Windows DPAPI for the current user.
/// The key is deliberately kept out of settings.json and source control.
/// </summary>
public sealed class RenderCredentialStore
{
    private static readonly byte[] Entropy =
        "YTMT-STUDIO/RenderApiKey/v1"u8.ToArray();

    private readonly string _credentialPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YTMT-STUDIO", "Security", "render-api-key.bin");

    public bool HasSavedKey => File.Exists(_credentialPath);

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Render API anahtarı boş olamaz.", nameof(apiKey));

        Directory.CreateDirectory(Path.GetDirectoryName(_credentialPath)!);
        byte[] clear = Encoding.UTF8.GetBytes(apiKey.Trim());
        string temporaryPath = _credentialPath + ".tmp";

        try
        {
            byte[] encrypted = ProtectedData.Protect(
                clear, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporaryPath, encrypted);

            if (File.Exists(_credentialPath))
                File.Replace(temporaryPath, _credentialPath, null);
            else
                File.Move(temporaryPath, _credentialPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public string? Load()
    {
        if (!File.Exists(_credentialPath))
            return null;

        byte[]? clear = null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(_credentialPath);
            clear = ProtectedData.Unprotect(
                encrypted, Entropy, DataProtectionScope.CurrentUser);
            string apiKey = Encoding.UTF8.GetString(clear);
            return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Kayıtlı Render anahtarı bu Windows hesabında çözülemedi. Anahtarı yeniden kaydetmen gerekiyor.", ex);
        }
        finally
        {
            if (clear is not null)
                CryptographicOperations.ZeroMemory(clear);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_credentialPath))
                File.Delete(_credentialPath);
            TryDeleteTemporaryFile(_credentialPath + ".tmp");
        }
        catch (IOException)
        {
            // Credential cleanup should not crash the UI.
        }
        catch (UnauthorizedAccessException)
        {
            // The user can retry after fixing local file permissions.
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}