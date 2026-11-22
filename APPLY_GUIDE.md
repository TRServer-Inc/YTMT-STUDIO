# YTMT-STUDIO v1.4 — Login entegrasyonu

Bu paket, önceki YTMT-STUDIO 1.3 Windows Forms kaynaklarını korur ve login sistemini temiz bir modül olarak ekler. Yalnızca masaüstü projesi değil, ayrı deploy edilecek Render Authentication API kaynakları da pakete dahildir.

## ÖNEMLİ: Önce backend'i kur

Masaüstü programı login API'si hazır değilken başarılı giriş yapamaz. Sırayı değiştirme:

1. Ayrı bir **private GitHub repo** oluştur: ör. `ytmt-studio-auth-data`.
2. `AuthService/auth/accounts.example.json` içeriğini bu private repo içine `auth/accounts.json` adıyla koy. Başlangıçta `accounts` boş olabilir.
3. Render'da PostgreSQL veritabanı oluştur.
4. Render Web Service oluştur. Kaynak aynı YTMT-STUDIO repo'sundaysa **Root Directory**: `AuthService`; ayrı auth repo'su hazırladıysan onun kök dizini.
5. Build Command `npm install`, Start Command `npm start`, Runtime Node.js 22+.
6. `AuthService/README.md` içindeki environment variable'ları Render'a ekle. GitHub token yalnızca Render'da bulunmalı.
7. Deploy et ve `https://AUTH-SERVIS-ADRESIN/health` adresinde `database: connected` yanıtını doğrula.
8. İlk hesabın hash'ini üret: `AuthService` klasöründe `npm install` ve `npm run hash-password`. Hash'i private GitHub'daki `auth/accounts.json` içine ekle.
9. Proje kökündeki `auth.config.json` içinde `baseUrl` değerini kendi Render Auth API URL'inle değiştir.

## Visual Studio

1. `YTMT-STUDIO.csproj` dosyasını Visual Studio 2026 ile aç.
2. NuGet restore tamamlanana kadar bekle.
3. `Build > Rebuild Solution` çalıştır.
4. `F5` ile aç. Kaydedilmiş oturum yoksa koyu login ekranı gelir.

## Uygulama davranışı

- Program açılırken kaydedilmiş refresh token sunucuda kontrol edilir.
- İlk girişte kullanıcı adı ve parola Render'a HTTPS ile gönderilir.
- Parola yalnızca Render'da GitHub hesap kaydındaki Argon2id hash'i ile doğrulanır.
- `Beni hatırla` varsayılan olarak seçilidir; 30 gün mutlak oturum süresi uygular.
- Seçim kaldırılırsa oturum 12 saat olur.
- Windows tarafında oturum verisi `%LOCALAPPDATA%\YTMT-STUDIO\Auth\session.bin` içine DPAPI CurrentUser ile şifrelenerek yazılır.
- Ana pencerenin sol altındaki `Çıkış Yap` düğmesi sunucu oturumunu iptal eder ve yerel bileti siler.
- Masaüstü uygulaması 2 dakikada bir oturum kontrolü yapar. Hesap devre dışı bırakılmışsa veya `sessionVersion` değişmişse oturum kapanır.

## Hesap ekleme / sıfırlama

Kayıt ekranı yok. Yeni hesap eklemek için hash üretip private GitHub `auth/accounts.json` dosyasına yeni nesne ekle. Parola sıfırlamada `passwordHash` alanını güncelle ve `sessionVersion` değerini artır. Hesap kapatmada `enabled` alanını `false` yap.

## Şu anki doğrulama durumu

- `AuthService/src/server.js`: `node --check` başarılı.
- `AuthService/scripts/hash-password.mjs`: `node --check` başarılı.
- JSON dosyaları: JSON parser kontrolünden geçti.
- C# kaynakları: bu çalışma ortamında .NET SDK bulunmadığından gerçek `dotnet build` çalıştırılamadı. Visual Studio 2026'da Rebuild yapılmalı.
- Render deployment, GitHub erişimi, PostgreSQL ve gerçek login testi bu ortamdan yapılmadı; bunlar senin Render/GitHub bilgilerinle yapılandırıldıktan sonra test edilebilir.

## Güvenlik sınırı

Bu login sistemi YTMT-STUDIO masaüstü uygulamasının açılmasını kontrol eder. Mevcut YTMT BOT `/api/stats` ve `/api/events/stream` uçlarına dokunmaz. Bot API uçlarını dış istemcilere karşı da korumak istersen `server.js` için ayrıca bearer-token middleware entegrasyonu gerekir.