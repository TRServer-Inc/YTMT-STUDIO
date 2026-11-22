# YTMT-STUDIO Authentication API

Bu servis, YTMT-STUDIO Windows Forms uygulamasının hesap doğrulamasını yapar. Hesapların tek doğruluk kaynağı GitHub'daki private `accounts.json` dosyasıdır. Bu serviste kayıt olma veya hesap oluşturma endpoint'i bulunmaz.

## Gereksinimler

- Node.js 22 veya üzeri
- Render Web Service
- Render PostgreSQL (kalıcı oturum kayıtları için)
- Yalnızca hesap deposunu okuyabilen GitHub fine-grained token

`argon2` paketi Node.js 22+ sürümlerini hedefler. `package.json` güncel paket sürüm aralıklarını kullanır; Render build sırasında `npm install` çalıştırır.

## 1. GitHub hesap deposu

Ayrı bir **private** repository oluştur. Önerilen içerik:

```text
ytmt-studio-auth-data/
└── auth/
    └── accounts.json
```

Örnek dosya (`auth/accounts.example.json`):

```json
{
  "accounts": []
}
```

Hesap kaydı örneği:

```json
{
  "accounts": [
    {
      "id": "account_001",
      "username": "operator",
      "displayName": "Operator",
      "passwordHash": "$argon2id$v=19$...",
      "role": "admin",
      "enabled": true,
      "sessionVersion": 1,
      "createdAt": "2026-11-08"
    }
  ]
}
```

- `id` benzersiz olmalı.
- `username` 3–32 karakter; harf, sayı, nokta, alt çizgi ve tire kabul edilir.
- `passwordHash` düz parola değil, Argon2id hash olmalı.
- `role` yalnızca `owner`, `admin` veya `user` olabilir. Alan yoksa eski hesaplar giriş yapmaya devam eder ve `user` kabul edilir.
- `enabled: true` açıkça verilmelidir; `false` veya eksik alan hesabı devre dışı bırakır.
- `sessionVersion` artırılırsa o hesaba ait mevcut oturumlar yenilemede geçersiz olur.
- Aynı kullanıcı adı veya `id` iki kez kullanılamaz.

### Parola hash'i oluşturma

Bu servis klasöründe:

```bash
npm install
npm run hash-password
```

Araç parolayı terminalde yıldızlarla gizleyerek ister. En az 12 karakter kullan. Oluşan `$argon2id$...` hash'ini GitHub'daki hesaba ekle; parolanın kendisini hiçbir dosyaya veya sohbete koyma.

## 2. Render kurulumu

1. GitHub private hesap deposunda `auth/accounts.example.json` dosyasını `auth/accounts.json` adıyla ekle.
2. Render'da PostgreSQL veritabanı oluştur.
3. Render Web Service oluştururken Root Directory alanını `AuthService` yap (servisi ayrı repo olarak oluşturduysan kök klasörü kullan).
4. Runtime: Node; Build Command: `npm install`; Start Command: `npm start`.
5. Web Service ile PostgreSQL'i aynı Render bölgesinde tut ve Render'ın internal database URL'sini `DATABASE_URL` olarak ekle.
6. Aşağıdaki environment variable'ları tanımla:

| Değişken | Değer |
|---|---|
| `NODE_ENV` | `production` |
| `DATABASE_URL` | Render PostgreSQL internal URL |
| `DATABASE_SSL` | `false` (sağlayıcın SSL istiyorsa `true`) |
| `GITHUB_TOKEN` | Private hesap deposuna yalnızca Contents: Read izni olan token |
| `GITHUB_OWNER` | GitHub kullanıcı/organizasyon adı |
| `GITHUB_REPO` | Hesap deposunun repo adı |
| `GITHUB_BRANCH` | `main` |
| `GITHUB_ACCOUNTS_PATH` | `auth/accounts.json` |
| `GITHUB_CACHE_SECONDS` | `15` |

`PORT` Render tarafından otomatik sağlanır. Oturum süreleri kodda sabitlenmiştir: beni hatırla seçiliyken 30 gün, seçili değilken 12 saat; access token 15 dakika.

Deploy tamamlanınca şu adresi kontrol et:

```text
https://SENIN-AUTH-SERVISIN.onrender.com/health
```

Beklenen yanıt:

```json
{"ok":true,"service":"ytmt-studio-auth","database":"connected"}
```

## 3. YTMT-STUDIO masaüstü ayarı

Proje kökündeki `auth.config.json` dosyasında yalnızca Render servis adresini değiştir:

```json
{
  "baseUrl": "https://SENIN-AUTH-SERVISIN.onrender.com/"
}
```

Bu ayar derleme çıktısına kopyalanır. `GITHUB_TOKEN`, PostgreSQL URL'si veya başka sunucu sırrı masaüstü uygulamasına eklenmez.

Visual Studio 2026'da `YTMT-STUDIO.csproj` aç, NuGet geri yüklemesi tamamlandıktan sonra Rebuild yap.

## 4. API

| Method | Endpoint | İşlev |
|---|---|---|
| `GET` | `/health` | Servis ve DB sağlık kontrolü |
| `POST` | `/auth/login` | Kullanıcı adı/şifre ile giriş |
| `POST` | `/auth/refresh` | Access token yenileme |
| `GET` | `/auth/me` | Mevcut kullanıcıyı doğrulama |
| `GET` | `/auth/validate` | Oturum doğrulama |
| `POST` | `/auth/logout` | Refresh token ile oturumu iptal etme |

`/auth/register` ve `/auth/signup` yoktur; bu yollar 404 döndürür.

### Oturum davranışı

- Access token: 15 dakika.
- Beni hatırla açıkken mutlak oturum süresi: 30 gün.
- Beni hatırla kapalıysa mutlak oturum süresi: 12 saat.
- Refresh token yenilemesi 30 günlük mutlak bitiş tarihini uzatmaz.
- PostgreSQL'de yalnızca token SHA-256 özetleri tutulur; düz token DB'ye yazılmaz.
- Windows tarafında refresh token DPAPI `CurrentUser` ile şifrelenir.
- Masaüstü uygulaması iki dakikada bir oturum kontrolü yapar.

## 5. Hesap kapatma ve parola sıfırlama

Hesabı geçici olarak kapatmak için GitHub kaydında `enabled` alanını `false` yap. Tüm eski oturumları geçersiz kılmak için `sessionVersion` değerini artır. Değişiklik, GitHub önbelleğinin sona ermesinden sonra ve sonraki oturum kontrolünde uygulanır (önbellek varsayılan 15 saniye).

Parola sıfırlama için hash-password aracından yeni hash üret, GitHub'da `passwordHash` alanını değiştir ve `sessionVersion` değerini artır. Kullanıcıya açık parola sıfırlama veya signup akışı yoktur.

## Güvenlik notları

- GitHub token'ını yalnızca Render secret/environment variable olarak sakla.
- Token'a sadece hesap deposunda Contents: Read izni ver.
- Hesap deposu private olmalı ve commit yetkisi yalnızca güvendiğin kişilerde olmalı.
- Üretim ortamında Render HTTPS kullan.
- API rate limit uygular; IP başına ve kullanıcı adı başına başarısız denemeler sınırlandırılır.
- Parola ve tokenlar loglanmaz. Olay kayıtları yalnızca olay türü, hesap kimliği ve zaman içerir.
- PostgreSQL yedeği ve GitHub repository erişim güvenliği ayrıca yönetilmelidir.
- Rate-limit deposu varsayılan olarak process belleğidir; birden çok Render instance'ına ölçeklenirse ortak rate-limit store kurulmalıdır.
- Bu masaüstü login bir istemci erişim kapısıdır; değiştirilebilir bir masaüstü binary'sinin tek başına sunucu tarafı yetkilendirme yerine geçmediğini unutma.

## Uygulama ile bot API arasındaki sınır

Bu sürüm YTMT-STUDIO masaüstü uygulamasının açılışını kimlik doğrulamasına bağlar. Mevcut YTMT BOT `/api/stats` ve `/api/events/stream` uçlarını değiştirmez. Bu uçların uygulama dışından da korunması istenirse botun `server.js` dosyasına ayrıca bearer-token doğrulaması eklenmelidir; bu paket bot sunucusunu değiştirmez.