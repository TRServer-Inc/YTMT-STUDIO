# Build / Validation Report — YTMT-STUDIO v1.4 Auth

## Çalıştırılan kontroller

| Kontrol | Sonuç |
|---|---|
| Node `src/server.js` syntax | PASS (`node --check`) |
| Node `scripts/hash-password.mjs` syntax | PASS (`node --check`) |
| `package.json` JSON parse | PASS |
| `auth/accounts.example.json` JSON parse | PASS |
| `auth.config.json` JSON parse | PASS |
| C# delimiter/static file review | PASS (compiler yerine geçmez) |
| .NET build | NOT RUN — bu ortamda `dotnet` bulunmuyor |
| Render deploy + DB connection | NOT RUN — kullanıcıya ait deployment yok |
| GitHub Contents API integration | NOT RUN — gerçek GitHub secret yok |
| End-to-end login | NOT RUN — backend secrets/deployment gerekli |

## Manuel Windows doğrulaması

1. Visual Studio 2026 + .NET 10 Windows Desktop SDK ile `.csproj` aç.
2. NuGet restore ardından Rebuild çalıştır.
3. `auth.config.json` içindeki URL'nin HTTPS ve Render servisinin tam adresi olduğunu doğrula.
4. Private GitHub dosyasına geçerli Argon2id hash'li, `enabled: true` bir hesap koy.
5. Doğru şifreyle login; yanlış şifreyle generic hata dene.
6. Uygulamayı kapatıp aç; DPAPI kayıtlı session ile ana pencere açılmalı.
7. Çıkış yap; yeniden açınca login formu gelmeli.
8. GitHub'da hesabı `enabled: false` yap; cache süresi ve en geç bir sonraki auth heartbeat sonrasında uygulama oturumu kapatmalı.
9. `sessionVersion` artır; önceki refresh session yenilenememeli.
10. `Beni hatırla` açıkken session expiry 30 gün, kapalıyken 12 saat olmalı.

## Dikkat

Bu rapor, yalnızca işaretlenmiş statik kontrollerin gerçekten yapıldığını ifade eder. C# derleme veya canlı servis testi yapılmış gibi yorumlanmamalıdır.