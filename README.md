# YTMT-STUDIO v1.4 — Login Edition

.NET 10 Windows Forms yönetim paneli + ayrı Render Authentication API.

## Öne çıkanlar

- Modern koyu login ekranı.
- Yalnızca private GitHub hesabında önceden eklenmiş kullanıcılar giriş yapabilir.
- Signup / register sistemi yoktur.
- Kullanıcı adı + parola doğrulaması; parolalar Argon2id hash olarak saklanır.
- Beni hatırla açıkken 30 gün; kapalıyken 12 saat mutlak oturum.
- Access token 15 dakika; token yenileme 30 günlük bitiş tarihini uzatmaz.
- Refresh token Windows DPAPI CurrentUser ile şifreli saklanır.
- Ana ekranda kullanıcı bilgisi ve çıkış düğmesi bulunur.
- Hesap devre dışı bırakma ve `sessionVersion` ile oturum iptali desteklenir.

## Önce kurulum rehberi

**Önemli:** Önce `AuthService/README.md`, sonra `APPLY_GUIDE.md` dosyasını oku. Render servisi, PostgreSQL ve private GitHub hesap deposu kurulmadan masaüstü login çalışmayacaktır.

## Proje yapısı

```text
YTMT-STUDIO-v1.4/
├── AuthClient/             # WinForms login, API client, DPAPI session
├── AuthService/            # Render Node.js auth backend
├── Controls/               # Existing dashboard controls
├── Models/                 # Existing bot monitoring models
├── Services/               # Existing bot API + settings services
├── MainForm.cs             # Existing main UI + auth user/logout integration
├── Program.cs              # Auth-first startup
├── auth.config.json        # Render Auth API URL
└── YTMT-STUDIO.csproj
```

## Mevcut özellikler korunur

Dashboard, WebView2 terminal, komut kataloğu, API izleme ve SSE canlı olay sayfası aynı projede kalır. Bot API uçları bu değişiklikte değiştirilmemiştir.

## Doğrulama

`BUILD_VALIDATION.md` yapılan kontrolleri ve henüz çalıştırılamayan testleri açıkça listeler. Bu çalışma ortamında .NET SDK bulunmadığından C# derlemesi yapılmamıştır.