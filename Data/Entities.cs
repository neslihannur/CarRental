namespace CarRental.Data;

public class Car
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Category { get; set; } = "";
    public string Image { get; set; } = "";
    public string Transmission { get; set; } = "Otomatik";
    public string Fuel { get; set; } = "Benzin";
    public int Seats { get; set; }
    public int Doors { get; set; }
    public int Luggage { get; set; }
    public int Year { get; set; }
    public int Horsepower { get; set; }
    public decimal DailyPrice { get; set; }
    /// Teslimde kredi kartından alınan provizyon.
    public decimal Deposit { get; set; }
    public int MinDriverAge { get; set; }
    public int MinLicenseYears { get; set; }
    public int DailyKmLimit { get; set; }
    /// Aynı modelden filoda kaç araç var; müsaitlik buna göre hesaplanır.
    public int Units { get; set; } = 1;
    public string Tagline { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Features { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public string Name => $"{Brand} {Model}";
}

public enum ReservationStatus { Confirmed, Active, Completed, Cancelled }

public class Reservation
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    /// Yönetim bağlantısındaki gizli anahtar (e-postadaki "Rezervasyonu yönet" linki).
    public string AccessToken { get; set; } = "";
    public int CarId { get; set; }
    public Car Car { get; set; } = null!;

    public string PickupBranch { get; set; } = "";
    public string ReturnBranch { get; set; } = "";
    /// Türkiye saatiyle (UTC+3) yerel zaman.
    public DateTime PickupAt { get; set; }
    public DateTime ReturnAt { get; set; }
    public int Days { get; set; }

    public decimal DailyPrice { get; set; }
    public decimal BaseTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal ExtrasTotal { get; set; }
    public decimal OneWayFee { get; set; }
    public decimal Total { get; set; }
    public decimal Deposit { get; set; }
    public List<string> Extras { get; set; } = [];

    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public int LicenseYear { get; set; }
    public string? FlightNo { get; set; }
    public string? Notes { get; set; }
    public string? AdminNote { get; set; }

    public ReservationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public string? Ip { get; set; }
    /// Örnek veri: e-posta gönderilmez, durumu zamanla kendiliğinden ilerler.
    public bool IsDemo { get; set; }

    public List<ReservationEvent> Events { get; set; } = [];
}

public class ReservationEvent
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public DateTime At { get; set; }
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public string By { get; set; } = "";
}

public class ContactMessage
{
    public int Id { get; set; }
    /// "iletisim" ya da "kurumsal"
    public string Kind { get; set; } = "iletisim";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? FleetSize { get; set; }
    public string? Subject { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public enum EmailStatus { Pending, Sent, Failed, Skipped }

/// Giden kutusu: e-postalar önce buraya yazılır, arka plan servisi gönderir ve tekrar dener.
public class EmailMessage
{
    public int Id { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Html { get; set; } = "";
    public string Kind { get; set; } = "";
    public int? ReservationId { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentContent { get; set; }
    public EmailStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
}

public class AdminUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime? LastLoginAt { get; set; }
}
