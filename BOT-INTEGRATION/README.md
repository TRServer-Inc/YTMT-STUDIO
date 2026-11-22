# YTMT-STUDIO — Sunucu Yönetimi ve YTMT-BOT entegrasyonu

## Bu sürümde gelenler

- Discord OAuth2 Authorization Code + PKCE masaüstü girişi.
- Loopback callback: `http://127.0.0.1:53682/callback`.
- Yalnızca Discord'da sahibi, Administrator, Manage Server veya Manage Roles yetkisi bulunan sunucuların listelenmesi.
- YTMT BOT'un seçilen sunucuda olup olmadığını kontrol etme.
- Bot davet bağlantısı oluşturma; gerekli temel izinler istenir, Administrator izni istenmez.
- Bot rol hiyerarşisinin düzenleyebildiği rolleri listeleme.
- Birden fazla otomatik rol taslağı.
- Mevcut mesaj ID'si veya yeni bot mesajı yöntemiyle mesaj-rol taslakları.
- Karşılama kanalı/mesajı, log kanalı ve temel moderasyon ayarı taslakları.
- Yerel JSON kaydetme, içe/dışa aktarma, doğrulama ve yerel denetim kaydı.
- Discord erişim ve yenileme token'larının Windows DPAPI CurrentUser ile şifrelenmesi.

## 1. Discord Developer Portal

Aynı Discord uygulamasında aşağıdakileri kontrol et:

1. OAuth2 Redirects listesinde tam olarak `http://127.0.0.1:53682/callback` kayıtlı olmalı.
2. Desktop PKCE akışının Client Secret olmadan token değişimi yapabilmesi için uygulamada Public OAuth2 Client seçeneği etkin olmalı.
3. Uygulamanın Client ID değerini YTMT-STUDIO > Sunucu Yönetimi sayfasında kaydet.

**Client Secret masaüstü projesine, ZIP'e, GitHub deposuna veya kullanıcı ayarlarına eklenmez.** Bu uygulama PKCE public-client akışını kullanır. Discord izin ekranında yalnızca `identify` ve `guilds` kapsamları istenir.

## 2. YTMT BOT sürümünü güncelle

ZIP içindeki `Bot-Integration/YTMT-BOT` klasörü, mevcut YTMT-BOT deposuna aynı göreli yollarla uygulanması gereken dosyaları içerir. Bu değişiklikler GitHub'daki YTMT-BOT deposuna da işlendi.

Bot tarafındaki eklemeler:

- Çoklu oto rol alanı `autoRoleIds`; eski `autoRoleId` alanı geriye dönük uyumluluk için tutulur.
- Üye katılımında seçili birden fazla rolün atanması.
- `ReactionRoleConfig` MongoDB modeli.
- Emoji tepkisi eklenince rol verme, tepki kaldırılınca rolü geri alma.
- YTMT-STUDIO'nun bot durumunu kontrol etmesi için salt-okunur endpoint.
- Sunucu rol/kanal listesini yalnızca Discord OAuth token'ı üzerinden sunucu yetkisi doğrulandıktan sonra döndüren endpoint.
- Bekleyen yeni mesaj rol taslaklarını botun göndermesi için 30 saniyelik işleyici.

Bot için gerekli Discord izinleri:
- Manage Roles
- View Channels
- Send Messages
- Read Message History
- Add Reactions
- Manage Messages (bot davet bağlantısında temel izin setine dahil)

Seçilen roller botun en yüksek rolünün altında olmalı. Discord Developer Portal'da botun gerekli Gateway Intent ayarları da açık olmalı; mevcut uygulamada Guild Members ve Message Content intent'leri zaten kullanılıyordu. Bu sürüm Guild Message Reactions intent'ini ekler.

## 3. Sunucu ayarlarını uygulama

YTMT-STUDIO içindeki ayarlar güvenli şekilde **yerel taslak** olarak kaydedilir:

`%LOCALAPPDATA%\YTMT-STUDIO\GuildWorkspaces\`

JSON dışa aktardıktan sonra dosyayı YTMT-BOT projesinin bulunduğu makineye aktar. YTMT-BOT'un `.env` dosyasında `MONGO_URI` bulunmalı. Bu sırları kimseyle paylaşma ve kaynak koda commit etme.

Bot proje klasöründe komut:

```bash
npm run import-studio-config -- "C:\path\to\ytmt-guild-123456789012345678.json"
```

Komut seçili sunucunun `GuildConfig`, karşılama ve mesaj-rol kayıtlarını MongoDB'ye aktarır. Bu CLI aracı sunucu yöneticisinin kendi ortamında çalıştırılması için tasarlanmıştır; MongoDB bağlantı bilgisini masaüstü uygulamasına gömmez.

- Mevcut mesaj ID'si yöntemi, mesajı bulduğu anda tepki/rol eşleştirmesini kullanır.
- Yeni mesaj yöntemi `pending` olarak kaydedilir; çalışan bot en geç yaklaşık 30 saniyelik kontrol döngüsünde mesajı gönderir ve tepkileri ekler.
- Oto rol yeni katılan üyelerde uygulanır.
- Karşılama ve moderasyon alanları mevcut GuildConfig kayıtlarına aktarılır.

İçe aktarma aracı seçili sunucunun önceki mesaj-rol kayıtlarını yenileriyle değiştirir. Yalnızca doğru sunucu JSON dosyasını kullandığından emin ol.

## 4. Güvenlik sınırları

- Masaüstü uygulaması Discord token'larını DPAPI ile CurrentUser kapsamına şifreler.
- Yerel JSON yapılandırma dosyaları Discord token'ı veya bot token'ı içermez.
- YTMT BOT kaynak uç noktası sunucu kaynaklarını döndürmeden önce Discord'dan kullanıcının sunucu yetkisini tekrar doğrular.
- Salt-okunur bot durum uç noktası dışında kaynak API'si OAuth Bearer başlığı ister.
- Canlı ayar değişikliği, açık bir kullanıcı eylemi olan yerel CLI içe aktarma adımı ile yapılır; uygulama taslak kaydını canlıya yayımlanmış gibi göstermez.
- Bot token'ı, MongoDB URI'si, GitHub token'ı veya Discord Client Secret asla ZIP'e eklenmez.

## 5. Derleme

ZIP'in `YTMT-STUDIO` klasöründe kaynak kod bulunur; `YTMT-STUDIO-Windows` klasöründe ise Windows x64 yayın çıktısı bulunur.

Yayın çıktısı framework-dependent'dır ve Windows üzerinde .NET 10 Desktop Runtime gerektirir. Kaynak kodu Visual Studio 2026 veya .NET 10 SDK ile yeniden derlenebilir.
