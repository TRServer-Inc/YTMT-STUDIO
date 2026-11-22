using YTMT_STUDIO.AuthClient.Forms;
using YTMT_STUDIO.Services;

namespace YTMT_STUDIO;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        DiscordOAuthService? oauthService = null;
        DiscordSession? discordSession = null;

        try
        {
            if (ulong.TryParse(settings.DiscordClientId, out _))
            {
                oauthService = new DiscordOAuthService(settings.DiscordClientId, settings.BotApiUrl);
                discordSession = oauthService.LoadSavedSessionAsync().GetAwaiter().GetResult();
            }

            if (discordSession is null)
            {
                oauthService?.Dispose();
                oauthService = null;

                using var loginForm = new DiscordLoginForm(settingsService, settings);
                if (loginForm.ShowDialog() != DialogResult.OK ||
                    loginForm.Session is null ||
                    loginForm.OAuthService is null)
                    return;

                discordSession = loginForm.Session;
                oauthService = loginForm.OAuthService;
            }

            using (oauthService)
            {
                Application.Run(new MainForm(discordSession, oauthService));
            }
        }
        catch (Exception ex)
        {
            oauthService?.Dispose();
            MessageBox.Show(
                ex.Message,
                "YTMT-STUDIO | Discord giriş hatası",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}