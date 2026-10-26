using System.ComponentModel.DataAnnotations;
using CarRental.Data;
using CarRental.Services;

namespace CarRental.Models;

/// Arama çubuğu ve rezervasyon formundaki ortak yolculuk alanları (sorgu dizesi adlarıyla).
public class TripFields
{
    public string? Sube { get; set; }
    public string? DonusSube { get; set; }
    public DateOnly? Alis { get; set; }
    public string? AlisSaat { get; set; }
    public DateOnly? Donus { get; set; }
    public string? DonusSaat { get; set; }

    public Trip ToTrip() => Trip.From(Sube, DonusSube, Alis, AlisSaat, Donus, DonusSaat);

    /// Kullanıcının girdiği değerleri düzeltmeden doğrular (POST için).
    public string? Validate(out Trip trip)
    {
        trip = Trip.Default();
        if (Catalog.FindBranch(Sube) is null || Catalog.FindBranch(DonusSube) is null) return "Lütfen alış ve iade şubesini seçin.";
        if (Alis is null || Donus is null) return "Lütfen alış ve iade tarihlerini seçin.";
        if (!Catalog.Times.Contains(AlisSaat) || !Catalog.Times.Contains(DonusSaat)) return "Lütfen geçerli bir saat seçin.";
        var pickup = Alis.Value.ToDateTime(TimeOnly.Parse(AlisSaat!));
        var ret = Donus.Value.ToDateTime(TimeOnly.Parse(DonusSaat!));
        if (pickup < Clock.Now.AddHours(2)) return "Alış zamanı en erken 2 saat sonrası olabilir.";
        if (ret <= pickup.AddHours(2)) return "İade zamanı alıştan sonra olmalı.";
        if (Pricing.DaysBetween(pickup, ret) > Catalog.MaxDays) return $"Online rezervasyon en fazla {Catalog.MaxDays} gün olabilir; daha uzun süreler için kurumsal teklif alın.";
        trip = new Trip(Sube!, DonusSube!, pickup, ret);
        return null;
    }

    public static TripFields From(Trip t) => new()
    {
        Sube = t.PickupBranch, DonusSube = t.ReturnBranch,
        Alis = DateOnly.FromDateTime(t.PickupAt), AlisSaat = t.PickupAt.ToString("HH:mm"),
        Donus = DateOnly.FromDateTime(t.ReturnAt), DonusSaat = t.ReturnAt.ToString("HH:mm"),
    };
}

public class BookingForm : TripFields
{
    public List<string> Ek { get; set; } = [];

    [Required(ErrorMessage = "Ad soyad gerekli."), StringLength(80, MinimumLength = 3, ErrorMessage = "Ad soyad 3-80 karakter olmalı.")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "E-posta gerekli."), EmailAddress(ErrorMessage = "Geçerli bir e-posta girin."), StringLength(120)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Telefon gerekli."), RegularExpression(@"^[0-9 +()\-]{10,20}$", ErrorMessage = "Geçerli bir telefon girin.")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Doğum tarihi gerekli.")]
    public DateOnly? BirthDate { get; set; }

    [Required(ErrorMessage = "Ehliyet yılı gerekli."), Range(1950, 2100, ErrorMessage = "Geçerli bir yıl girin.")]
    public int? LicenseYear { get; set; }

    [StringLength(12)]
    public string? FlightNo { get; set; }

    [StringLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Notes { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Devam etmek için kiralama koşullarını onaylayın.")]
    public bool AcceptTerms { get; set; }

    /// Bot tuzağı; insanlar bu alanı görmez.
    public string? Website { get; set; }
}

public class ContactForm
{
    [Required(ErrorMessage = "Adınızı yazın."), StringLength(80, MinimumLength = 2, ErrorMessage = "Ad 2-80 karakter olmalı.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "E-posta gerekli."), EmailAddress(ErrorMessage = "Geçerli bir e-posta girin."), StringLength(120)]
    public string Email { get; set; } = "";

    [RegularExpression(@"^[0-9 +()\-]{10,20}$", ErrorMessage = "Geçerli bir telefon girin.")]
    public string? Phone { get; set; }

    [StringLength(100)]
    public string? Subject { get; set; }

    [StringLength(100)]
    public string? Company { get; set; }

    [StringLength(40)]
    public string? FleetSize { get; set; }

    [Required(ErrorMessage = "Mesajınızı yazın."), StringLength(2000, MinimumLength = 10, ErrorMessage = "Mesaj 10-2000 karakter olmalı.")]
    public string Message { get; set; } = "";

    [Range(typeof(bool), "true", "true", ErrorMessage = "Aydınlatma metnini onaylayın.")]
    public bool AcceptPrivacy { get; set; }

    public string? Website { get; set; }
}

public record CarCard(Car Car, Quote Quote, bool Available);

public class FleetPage
{
    public required Trip Trip { get; init; }
    public required List<CarCard> Cars { get; init; }
    public string? Category { get; init; }
    public string? Transmission { get; init; }
    public string? Fuel { get; init; }
    public int? Seats { get; init; }
    public string Sort { get; init; } = "onerilen";
    public int TotalCount { get; init; }
}

public class CarPage
{
    public required Car Car { get; init; }
    public required Trip Trip { get; init; }
    public required Quote Quote { get; init; }
    public required bool Available { get; init; }
    public DateTime? NextFree { get; init; }
    public required List<DateOnly> FullDays { get; init; }
    public required List<CarCard> Similar { get; init; }
}

public class BookingPage
{
    public required Car Car { get; init; }
    public required BookingForm Form { get; init; }
    public required Quote Quote { get; init; }
    public string? Error { get; init; }
}

public class HomePage
{
    public required Trip Trip { get; init; }
    public required List<CarCard> Featured { get; init; }
    public int Vehicles { get; init; }
    public int Rentals { get; init; }
}
