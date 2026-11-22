using YTMT_STUDIO.AuthClient.Forms;
using YTMT_STUDIO.AuthClient.Models;
using YTMT_STUDIO.AuthClient.Services;
using AuthenticationClient = YTMT_STUDIO.AuthClient.Services.AuthClient;

namespace YTMT_STUDIO;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        AuthenticationClient authClient;
        try
        {
            authClient = new AuthenticationClient();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "YTMT-STUDIO yapılandırması",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using (authClient)
        {
            var sessionStore = new AuthSessionStore();
            AuthSession? session = null;
            string? initialMessage = null;

            AuthSession? savedSession = sessionStore.Load();
            if (savedSession is not null)
            {
                try
                {
                    // Başlangıçta refresh token doğrulanır. Sunucu sabit 30 günlük
                    // son kullanma tarihini değiştirmez.
                    AuthSession refreshedSession = authClient.RefreshAsync(savedSession.RefreshToken)
                        .GetAwaiter().GetResult();
                    sessionStore.Save(refreshedSession);
                    session = refreshedSession;
                }
                catch (AuthApiException ex) when (ex.IsUnauthorized)
                {
                    sessionStore.Clear();
                    initialMessage = "Kaydedilmiş oturum artık geçerli değil. Yeniden giriş yap.";
                }
                catch (Exception)
                {
                    // Ağ kesintisinde saklanan oturum silinmez. Kullanıcı login
                    // ekranından sunucu erişimini kontrol edip tekrar deneyebilir.
                    initialMessage = "Kayıtlı oturum doğrulanamadı. Sunucu bağlantısını kontrol et ve giriş yap.";
                }
            }

            if (session is null)
            {
                using var loginForm = new LoginForm(authClient, sessionStore, initialMessage);
                if (loginForm.ShowDialog() != DialogResult.OK || loginForm.Session is null)
                    return;

                session = loginForm.Session;
            }

            Application.Run(new MainForm(session, authClient, sessionStore));
        }
    }
}