# YTMT-STUDIO rol sistemi kurulumu

## Roller ve sayfa izinleri

| Rol | Erişebildiği sayfalar |
|---|---|
| `owner` | Genel Bakış, Bot Yönetimi, Web Terminal, Komutlar, API İzleme, Ayarlar |
| `admin` | Genel Bakış, Web Terminal, Komutlar, API İzleme, Ayarlar |
| `user` | Genel Bakış, Komutlar, API İzleme |

`Bot Yönetimi` yalnızca `owner` rolüne açıktır. Kullanıcı arayüzündeki sayfa gizleme dışında `ShowPage` içinde de izin kontrolü yapılır. AuthService rolü login, refresh ve `/auth/me` cevaplarında gönderir. Rol değişikliği uygulamanın iki dakikalık oturum kontrolüyle alınır; mevcut oturumun erişimi buna göre güncellenir.

## GitHub hesap deposunda rol atama

Roller YTMT-STUDIO deposundaki örnek hesap dosyasına değil, Render AuthService'in okuduğu **private GitHub hesabı deposundaki** `auth/accounts.json` dosyasına yazılmalıdır.

Her hesap nesnesine `role` ekle. Parola hash'ini ve diğer mevcut alanları koru; şifreyi veya hash'i paylaşmana gerek yok.

Örnek YTMTDev kaydı (mevcut `id`, `passwordHash`, `displayName`, `sessionVersion` değerlerini kendi hesabındaki gibi koru):

```json
{
  "id": "MEVCUT_HESAP_ID",
  "username": "YTMTDev",
  "displayName": "YTMTDev",
  "passwordHash": "MEVCUT_ARGON2ID_HASH",
  "role": "owner",
  "enabled": true,
  "sessionVersion": 1
}
```

Diğer hesaplarda ihtiyaca göre `admin` veya `user` kullan. `owner` verdiğin her hesap Bot Yönetimi dâhil tam sayfa erişimi alır; bu nedenle owner rolünü yalnızca gerçekten tam yetkili olmasını istediğin hesaplara ver.

## Geriye dönük uyumluluk

- `role` alanı olmayan mevcut hesaplar `user` kabul edilir; bu, oturum açmayı engellemez.
- Desteklenmeyen bir rol (`superadmin` gibi) varsa AuthService hesap dosyasını reddeder. Yalnızca `owner`, `admin`, `user` kullan.
- Parola, Argon2id hash ve `enabled` alanlarını değiştirmek gerekmez.
- `sessionVersion` değerini sırf rol değişikliği için artırma. Artırırsan o hesaba ait aktif oturumlar iptal edilir ve yeniden giriş gerekir.

## Uygulama

1. YTMT-STUDIO proje klasörünü yedekle.
2. Bu paketteki dosyaları aynı göreli yollarla mevcut projenin üzerine kopyala.
3. Private GitHub `auth/accounts.json` dosyasına her hesap için uygun `role` değerini ekle.
4. AuthService Render servisini güncel `AuthService/src/server.js` ile deploy et.
5. Visual Studio 2026'da `YTMT-STUDIO.csproj` dosyasını aç ve **Build > Rebuild Solution** çalıştır.
6. Uygulamayı yeniden aç. Yeni oturum açma veya sonraki auth heartbeat rolü API'den alır.

Yeni `AuthRoles.cs` SDK tarzı .NET projesinde otomatik derlemeye katılır; `.csproj` içine ayrıca `Compile Include` ekleme.

## Güvenlik sınırı

Bu sürüm rolü AuthService'te hesap bazında doğrular ve masaüstü uygulamasının sayfa erişimini sınırlar. Render API anahtarı hâlâ Windows kullanıcısına ait DPAPI korumalı yerel depoda kullanılır; Render işlemleri AuthService üzerinden proxy edilmediği için bu, değiştirilemeyen bir sunucu tarafı güvenlik sınırı değildir. Gerçek sunucu tarafı Render yetkilendirmesi için Render anahtarını yalnızca sunucuda tutan ayrı bir proxy endpoint tasarlanmalıdır.