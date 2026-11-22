# Changelog — YTMT-STUDIO v1.4

## Added

- Koyu temalı WinForms login formu.
- GitHub Private Repository üzerinden önceden tanımlı hesap doğrulama.
- Signup/register akışı yok.
- Render Authentication API (Express 5) ve PostgreSQL oturum deposu.
- Argon2id parola hash doğrulaması ve gizli parola hash üretme aracı.
- 15 dakikalık access token ve 30 günlük sabit refresh-session süresi.
- Windows DPAPI CurrentUser ile yerel refresh token şifreleme.
- Açılışta kayıtlı oturumu geri yükleme, 2 dakikada bir oturum heartbeat'i.
- Hesap kapatma ve sessionVersion ile tüm mevcut oturumları iptal etme.
- Giriş/yenileme hız sınırı ve sır içermeyen güvenlik olayları.
- Sidebar altına aktif kullanıcı ve çıkış düğmesi.

## Preserved

- Mevcut dashboard, API stats ve SSE monitoring kodları.
- WebView2 terminal, komut sayfası, ayarlar ve mevcut bot API adresleme mantığı.

## Notes

- Botun kendi `/api/stats` ve `/api/events/stream` endpoints'leri değiştirilmedi.
- C# build bu ortamda .NET SDK yokluğu nedeniyle doğrulanamadı.