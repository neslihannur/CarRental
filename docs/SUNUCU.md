# GoCar (CarRental) — sunucu

**Şifre, anahtar veya token bu dosyaya yazılmaz.**

| | |
|---|---|
| Sunucu | biora-1 `65.21.182.105`, Coolify → proje **Portfolio** / production |
| Uygulama | Dockerfile (depo kökü, dal `master`), konteyner portu **8080** |
| Adres | https://carrental.neslihannur.com.tr (Plesk DNS: `carrental` A → 65.21.182.105) |
| Veritabanı | Sunucudaki PostgreSQL, veritabanı ve kullanıcı `carrental`; bağlantı `/root/.config/carrental/db.env` |

## Ortam değişkenleri (Coolify)

| Ad | Açıklama |
|---|---|
| `DATABASE_URL` | `/root/.config/carrental/db.env` içindeki değer |
| `Admin__Email`, `Admin__Password` | İlk yönetici hesabı (yalnızca hiç yönetici yokken kullanılır) |
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password` | E-posta gönderimi (Gmail/Workspace: `smtp.gmail.com`, 587, uygulama şifresi) |
| `Smtp__FromEmail`, `Smtp__FromName` | Gönderen (boşsa `Smtp__User`) |
| `Site__NotifyEmail` | Yeni rezervasyon/mesaj bildirimlerinin gideceği adres |
| `Demo__Enabled` | `true` ise giriş ekranında salt okunur demo hesabı |
| `Demo__SeedReservations` | `true` ise örnek rezervasyonlar oluşturulur ve zamanla yenilenir |

## Nasıl çalışır

- Rezervasyonlar araç satırı kilitlenerek oluşturulur; aynı araç aynı saatlere iki kez verilemez (adet + 2 saat temizlik payı).
- E-postalar önce `EmailMessages` tablosuna yazılır, `OutboxWorker` 10 sn'de bir gönderir, hata olursa 5 kez dener. Panel → E-postalar.
- `HousekeepingWorker` 30 dk'da bir: yarın teslim alınacaklara hatırlatma, örnek verinin durumunu ilerletme.
- Yönetim: `/yonetim`. Şifre unutulursa `DELETE FROM "AdminUsers"` ve Coolify'da Admin değişkenleriyle yeniden başlat.
