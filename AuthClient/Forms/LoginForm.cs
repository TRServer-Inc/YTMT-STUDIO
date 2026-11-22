using System.Drawing;
using System.Net.Http;
using YTMT_STUDIO.AuthClient.Models;
using YTMT_STUDIO.AuthClient.Services;
using AuthenticationClient = YTMT_STUDIO.AuthClient.Services.AuthClient;

namespace YTMT_STUDIO.AuthClient.Forms;

public sealed class LoginForm : Form
{
    private static readonly Color Background = Color.FromArgb(10, 14, 22);
    private static readonly Color Surface = Color.FromArgb(19, 26, 38);
    private static readonly Color Input = Color.FromArgb(12, 18, 29);
    private static readonly Color Border = Color.FromArgb(43, 55, 75);
    private static readonly Color Primary = Color.FromArgb(55, 116, 235);
    private static readonly Color Foreground = Color.FromArgb(237, 242, 250);
    private static readonly Color Muted = Color.FromArgb(145, 158, 180);
    private static readonly Color Danger = Color.FromArgb(255, 118, 118);

    private readonly AuthenticationClient _authClient;
    private readonly AuthSessionStore _sessionStore;
    private readonly Panel _card = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _rememberMe = new();
    private readonly Button _loginButton = new();
    private readonly Button _showPasswordButton = new();
    private readonly Label _status = new();
    private bool _passwordVisible;
    private bool _busy;

    public AuthSession? Session { get; private set; }

    public LoginForm(
        AuthenticationClient authClient,
        AuthSessionStore sessionStore,
        string? initialMessage = null)
    {
        _authClient = authClient;
        _sessionStore = sessionStore;

        Text = "YTMT-STUDIO | Giriş";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(500, 650);
        Size = new Size(560, 720);
        MaximizeBox = false;
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 9.5F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
        if (!string.IsNullOrWhiteSpace(initialMessage))
            SetStatus(initialMessage, Danger);

        AcceptButton = _loginButton;
    }

    private void BuildInterface()
    {
        _card.Size = new Size(430, 570);
        _card.BackColor = Surface;
        _card.Padding = new Padding(34, 30, 34, 26);
        _card.Anchor = AnchorStyles.None;
        _card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _card.Width - 1, _card.Height - 1);
        };
        Controls.Add(_card);

        var brandMark = new Label
        {
            Text = "YT",
            Size = new Size(48, 48),
            Location = new Point(34, 30),
            BackColor = Primary,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var brandName = new Label
        {
            Text = "YTMT-STUDIO",
            AutoSize = true,
            Location = new Point(94, 34),
            ForeColor = Foreground,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold)
        };

        var brandCaption = new Label
        {
            Text = "BOT CONTROL CENTER",
            AutoSize = true,
            Location = new Point(95, 58),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold)
        };

        var title = new Label
        {
            Text = "Tekrar hoş geldin.",
            AutoSize = true,
            Location = new Point(34, 120),
            ForeColor = Foreground,
            Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold)
        };

        var subtitle = new Label
        {
            Text = "Devam etmek için yetkili hesabınla giriş yap.",
            AutoSize = true,
            Location = new Point(36, 163),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F)
        };

        var usernameLabel = CreateFieldLabel("KULLANICI ADI", 215);
        ConfigureTextBox(_username, new Point(34, 240), new Size(360, 43));
        _username.PlaceholderText = "Kullanıcı adını gir";
        _username.MaxLength = 32;
        _username.TabIndex = 0;

        var passwordLabel = CreateFieldLabel("ŞİFRE", 307);
        ConfigureTextBox(_password, new Point(34, 332), new Size(290, 43));
        _password.PlaceholderText = "Şifreni gir";
        _password.UseSystemPasswordChar = true;
        _password.MaxLength = 256;
        _password.TabIndex = 1;

        _showPasswordButton.Text = "Göster";
        _showPasswordButton.Location = new Point(330, 332);
        _showPasswordButton.Size = new Size(64, 43);
        _showPasswordButton.FlatStyle = FlatStyle.Flat;
        _showPasswordButton.FlatAppearance.BorderColor = Border;
        _showPasswordButton.FlatAppearance.BorderSize = 1;
        _showPasswordButton.BackColor = Input;
        _showPasswordButton.ForeColor = Muted;
        _showPasswordButton.Cursor = Cursors.Hand;
        _showPasswordButton.TabStop = false;
        _showPasswordButton.Click += (_, _) => TogglePasswordVisibility();

        _rememberMe.Text = "Beni hatırla · 30 gün boyunca açık tut";
        _rememberMe.Checked = true;
        _rememberMe.AutoSize = true;
        _rememberMe.Location = new Point(35, 392);
        _rememberMe.ForeColor = Muted;
        _rememberMe.BackColor = Surface;
        _rememberMe.Cursor = Cursors.Hand;
        _rememberMe.TabIndex = 2;

        _loginButton.Text = "Giriş Yap";
        _loginButton.Location = new Point(34, 438);
        _loginButton.Size = new Size(360, 48);
        _loginButton.FlatStyle = FlatStyle.Flat;
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.BackColor = Primary;
        _loginButton.ForeColor = Color.White;
        _loginButton.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _loginButton.Cursor = Cursors.Hand;
        _loginButton.TabIndex = 3;
        _loginButton.Click += async (_, _) => await LoginAsync();

        _status.AutoSize = false;
        _status.Location = new Point(34, 495);
        _status.Size = new Size(360, 38);
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 8.5F);
        _status.TextAlign = ContentAlignment.MiddleCenter;

        var footer = new Label
        {
            Text = "Yalnızca GitHub deposuna önceden eklenmiş hesaplar giriş yapabilir.",
            Location = new Point(34, 538),
            Size = new Size(360, 24),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 7.5F),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _card.Controls.AddRange([
            brandMark, brandName, brandCaption, title, subtitle,
            usernameLabel, _username, passwordLabel, _password,
            _showPasswordButton, _rememberMe, _loginButton, _status, footer
        ]);

        Resize += (_, _) => CenterCard();
        Shown += (_, _) =>
        {
            CenterCard();
            _username.Focus();
        };
    }

    private static Label CreateFieldLabel(string text, int y)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(36, y),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold)
        };
    }

    private static void ConfigureTextBox(TextBox textBox, Point location, Size size)
    {
        textBox.Location = location;
        textBox.Size = size;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Input;
        textBox.ForeColor = Foreground;
        textBox.Font = new Font("Segoe UI", 10F);
    }

    private void CenterCard()
    {
        _card.Left = Math.Max(12, (ClientSize.Width - _card.Width) / 2);
        _card.Top = Math.Max(12, (ClientSize.Height - _card.Height) / 2);
    }

    private void TogglePasswordVisibility()
    {
        _passwordVisible = !_passwordVisible;
        _password.UseSystemPasswordChar = !_passwordVisible;
        _showPasswordButton.Text = _passwordVisible ? "Gizle" : "Göster";
        _password.Focus();
    }

    private async Task LoginAsync()
    {
        if (_busy) return;

        string username = _username.Text.Trim();
        string password = _password.Text;
        if (username.Length == 0 || password.Length == 0)
        {
            SetStatus("Kullanıcı adı ve şifre gerekli.", Danger);
            return;
        }

        SetBusy(true);
        SetStatus("Hesap doğrulanıyor...", Muted);

        try
        {
            AuthSession session = await _authClient.LoginAsync(
                username, password, _rememberMe.Checked);
            _sessionStore.Save(session);
            Session = session;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (AuthApiException ex)
        {
            SetStatus(ex.Message, Danger);
            _password.Clear();
            _password.Focus();
        }
        catch (HttpRequestException)
        {
            SetStatus("Sunucuya bağlanılamadı. İnternet ve Render servisini kontrol et.", Danger);
        }
        catch (TaskCanceledException)
        {
            SetStatus("Bağlantı zaman aşımına uğradı. Tekrar deneyin.", Danger);
        }
        catch (Exception ex)
        {
            SetStatus("Giriş işlemi tamamlanamadı: " + ex.Message, Danger);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _loginButton.Enabled = !busy;
        _username.Enabled = !busy;
        _password.Enabled = !busy;
        _rememberMe.Enabled = !busy;
        _showPasswordButton.Enabled = !busy;
        _loginButton.Text = busy ? "Kontrol ediliyor..." : "Giriş Yap";
        UseWaitCursor = busy;
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}