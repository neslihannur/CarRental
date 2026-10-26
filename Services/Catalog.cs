using System.Globalization;

namespace CarRental.Services;

public record Branch(string Code, string Name, string City, string Address, string Hours, bool IsAirport);

public record Extra(string Code, string Name, string Description, decimal Price, bool PerDay, string Icon);

/// Şubeler, ek hizmetler ve fiyat kuralları. Hepsi kodda; değişmesi nadir.
public static class Catalog
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly string[] Categories = ["Sedan", "SUV", "Spor", "Arazi"];

    public static readonly IReadOnlyList<Branch> Branches =
    [
        new("ist", "İstanbul Havalimanı", "İstanbul", "Dış Hatlar Geliş Katı, Kapı 14 karşısı", "7/24", true),
        new("saw", "Sabiha Gökçen Havalimanı", "İstanbul", "İç Hatlar Geliş, Araç Kiralama Ofisleri", "7/24", true),
        new("kdk", "Kadıköy", "İstanbul", "Caferağa Mah. Moda Cad. No: 42", "08:00 – 22:00", false),
        new("lvt", "Levent", "İstanbul", "Büyükdere Cad. No: 185, Kanyon karşısı", "08:00 – 22:00", false),
        new("esb", "Ankara Esenboğa", "Ankara", "Geliş Katı, Araç Kiralama Bölümü", "7/24", true),
        new("adb", "İzmir Adnan Menderes", "İzmir", "İç Hatlar Geliş, Otopark B girişi", "7/24", true),
        new("ayt", "Antalya Havalimanı", "Antalya", "Terminal 2 Geliş Katı", "7/24", true),
    ];

    public static Branch? FindBranch(string? code) => Branches.FirstOrDefault(b => b.Code == code);

    public static readonly IReadOnlyList<Extra> Extras =
    [
        new("koruma", "Tam Koruma Paketi", "Hasar muafiyeti sıfırlanır; lastik, cam ve far hasarları dahil.", 650, true, "shield"),
        new("surucu", "Ek Sürücü", "Bir sürücü daha aracı kullanabilir (aynı yaş ve ehliyet şartları).", 300, true, "users"),
        new("cocuk", "Çocuk Koltuğu", "0–36 kg, ISOFIX bağlantılı, temizlenmiş ve kontrol edilmiş.", 200, true, "baby"),
        new("hgs", "HGS Geçiş Paketi", "Köprü ve otoyol geçişleri için yüklü HGS; ceza riski olmadan.", 750, false, "road"),
        new("wifi", "Mobil Wi-Fi", "Sınırsız 4.5G cep modemi, 5 cihaza kadar.", 150, true, "wifi"),
    ];

    public static Extra? FindExtra(string code) => Extras.FirstOrDefault(e => e.Code == code);

    /// Gün ve saat seçenekleri
    public static readonly string[] Times = Enumerable.Range(16, 29).Select(i => $"{i / 2:00}:{(i % 2) * 30:00}").ToArray(); // 08:00 – 22:00

    public const int MaxDays = 90;
    public const decimal SameCityOneWayFee = 750;
    public const decimal OtherCityOneWayFee = 2_500;
    /// İki kiralama arasında temizlik ve kontrol payı (saat).
    public const int TurnaroundHours = 2;

    public static decimal DiscountRate(int days) => days >= 30 ? 0.20m : days >= 14 ? 0.15m : days >= 7 ? 0.10m : 0m;

    public static string Money(decimal value) => value.ToString("N0", Tr) + " ₺";

    public static string Date(DateTime d) => d.ToString("d MMMM yyyy, dddd", Tr);
    public static string DateShort(DateTime d) => d.ToString("d MMM yyyy", Tr);
    public static string DateTimeShort(DateTime d) => d.ToString("d MMM yyyy HH:mm", Tr);

    public static string StatusText(Data.ReservationStatus s) => s switch
    {
        Data.ReservationStatus.Confirmed => "Onaylandı",
        Data.ReservationStatus.Active => "Kirada",
        Data.ReservationStatus.Completed => "Tamamlandı",
        Data.ReservationStatus.Cancelled => "İptal edildi",
        _ => s.ToString(),
    };
}

/// Türkiye saati (UTC+3, yaz saati uygulaması yok).
public static class Clock
{
    public static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);
    public static DateOnly Today => DateOnly.FromDateTime(Now);
}
