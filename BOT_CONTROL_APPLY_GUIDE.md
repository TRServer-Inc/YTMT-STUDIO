# YTMT-STUDIO 1.5 — Bot Yönetimi güncellemesi

Bu paket, kayıtlı YTMT-STUDIO .NET 10 / WinForms v1.4 kaynak koduna uygulanacak bir **yama paketidir**; bağımsız tam proje değildir.

## Dosyaları uygulama

1. Mevcut YTMT-STUDIO klasörünün bir kopyasını al.
2. Paketteki `MainForm.cs` ve `Models/AppSettings.cs` dosyalarını mevcut dosyaların üzerine koy.
3. `MainForm.BotControl.cs` dosyasını proje köküne ekle.
4. `Services/RenderControlService.cs` ve `Services/RenderCredentialStore.cs` dosyalarını `Services` klasörüne ekle.
5. `auth.config.json`, AuthClient, AuthService, LoginForm ve diğer kimlik doğrulama dosyalarını değiştirme.
6. Visual Studio 2026'da `YTMT-STUDIO.csproj` dosyasını açıp `Build > Rebuild Solution` çalıştır.

SDK tarzı .NET projesinde yeni `.cs` dosyaları otomatik derlemeye dahil edilir; `.csproj` dosyasına ayrıca `Compile Include` ekleme.

## Render bağlantısı

1. Render panelinde YTMT BOT'un **web service** sayfasını aç.
2. Service ID değerini kopyala. `srv-` ile başlar.
3. Render hesabında API anahtarı oluştur. Hesapta scoped key seçeneği varsa yalnızca ilgili hizmet ve gerekli okuma / resume / suspend / restart izinleriyle sınırla.
4. YTMT-STUDIO'yu `owner` rolüne atanmış hesapla aç, **Bot Yönetimi** sayfasına gir.
5. Service ID ile Render API anahtarını yazıp **Ayarları Kaydet** seçeneğine bas.
6. **Durumu Yenile** ile bağlantıyı dene.

API anahtarını sohbete gönderme, kaynak koda veya GitHub'a koyma. Program anahtarı Windows DPAPI kullanarak yalnızca o Windows kullanıcısının Local AppData alanında şifreler. Bu anahtar GPT/OpenAI API anahtarı değildir; Render hesabına ait bir yönetim kimliğidir. Render API anahtarları hesap/workspace yetkileri sağlayabilir; mümkün olan en dar kapsamı kullan.

## Neler eklendi?

- Render hizmet durumunu sorgulama.
- Askıya alınmış hizmeti başlatma / resume.
- Render hizmetini durdurma / suspend.
- Render hizmetini yeniden başlatma / restart.
- Son 50 uygulama logunu elle yenileyerek görüntüleme.
- İşlemlerden önce onay penceresi.
- Render anahtarını DPAPI ile şifreli saklama ve uygulama içinden silme.
- Bot Yönetimi sayfasını AuthService tarafından gönderilen `owner` rolüyle sınırlandırma.

## Önemli davranış

Bu düğmeler yalnızca Discord.js istemcisini durdurmaz. Render web hizmetinin tamamını etkiler. **Durdur** seçeneği aynı hizmette çalışan botu ve HTTP API uçlarını askıya alır. Bu nedenle `https://ytmt-bot.onrender.com` adresindeki API'ler de erişilemez olur. **Başlat** hizmeti yeniden sürdürür. **Yeniden Başlat** mevcut kod/yapılandırmayla hizmet örneğini yeniden başlatır; yeni kod deploy etmez.

Log alanı canlı akış değildir; Render API'den son 50 uygulama kaydını düğmeye basıldığında alır. Render log erişimi, hizmetin log saklama süresi ve API anahtarı izinleri kapsamında çalışır.

## Derleme doğrulaması

Bu hazırlama ortamında .NET SDK/compiler bulunmadığından `dotnet build` çalıştırılamadı. Bu nedenle paket **Visual Studio 2026 üzerinde Rebuild Solution ile doğrulanmalıdır**. Derleme hatası olursa hata listesinin ekran görüntüsünü veya tam metnini gönder; mevcut kaynakla düzeltelim.