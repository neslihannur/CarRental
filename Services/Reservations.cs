using System.Security.Cryptography;
using CarRental.Data;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Services;

public record BookingRequest(
    Car Car, Trip Trip, IReadOnlyList<string> Extras,
    string FullName, string Email, string Phone, DateOnly BirthDate, int LicenseYear,
    string? FlightNo, string? Notes, string? Ip);

/// Rezervasyonun yaşam döngüsü: oluştur, teslim et, iade al, iptal et.
public class Reservations(AppDb db, Availability availability, Mailer mailer)
{
    const string CodeChars = "ABCDEFGHJKLMNPRSTUVYZ23456789";

    public static string NewCode() => "GC-" + RandomNumberGenerator.GetString(CodeChars, 6);
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    /// Müsaitliği araç satırını kilitleyerek kontrol eder; iki kişi aynı anda son aracı alamaz.
    /// Dönen değer null ise araç bu aralıkta dolu.
    public async Task<Reservation?> CreateAsync(BookingRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Cars\" WHERE \"Id\" = {req.Car.Id} FOR UPDATE");
        if (!await availability.IsAvailableAsync(req.Car, req.Trip)) return null;

        var q = Pricing.Calculate(req.Car, req.Trip, req.Extras);
        var now = DateTime.UtcNow;
        var r = new Reservation
        {
            Code = await UniqueCodeAsync(),
            AccessToken = NewToken(),
            CarId = req.Car.Id,
            PickupBranch = req.Trip.PickupBranch,
            ReturnBranch = req.Trip.ReturnBranch,
            PickupAt = req.Trip.PickupAt,
            ReturnAt = req.Trip.ReturnAt,
            Days = q.Days,
            DailyPrice = q.DailyPrice,
            BaseTotal = q.BaseTotal,
            Discount = q.Discount,
            ExtrasTotal = q.ExtrasTotal,
            OneWayFee = q.OneWayFee,
            Total = q.Total,
            Deposit = q.Deposit,
            Extras = req.Extras.Where(e => Catalog.FindExtra(e) is not null).Distinct().ToList(),
            FullName = req.FullName.Trim(),
            Email = req.Email.Trim().ToLowerInvariant(),
            Phone = req.Phone.Trim(),
            BirthDate = req.BirthDate,
            LicenseYear = req.LicenseYear,
            FlightNo = string.IsNullOrWhiteSpace(req.FlightNo) ? null : req.FlightNo.Trim().ToUpperInvariant(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            Status = ReservationStatus.Confirmed,
            CreatedAt = now,
            Ip = req.Ip,
        };
        r.Events.Add(new ReservationEvent { At = now, Title = "Rezervasyon oluşturuldu", By = "Müşteri", Note = "Web sitesi üzerinden" });
        r.Events.Add(new ReservationEvent { At = now, Title = "Onaylandı", By = "Sistem", Note = "Araç müsait; otomatik onay" });
        db.Reservations.Add(r);
        await db.SaveChangesAsync();

        mailer.ReservationCreated(r, req.Car);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return r;
    }

    async Task<string> UniqueCodeAsync()
    {
        while (true)
        {
            var code = NewCode();
            if (!await db.Reservations.AnyAsync(r => r.Code == code)) return code;
        }
    }

    /// Müşteri teslim saatinden önce iptal edebilir (24 saatten azsa yine de izin verilir, ücret yok — ödeme teslimde).
    public static bool CanCustomerCancel(Reservation r) =>
        r.Status == ReservationStatus.Confirmed && r.PickupAt > Clock.Now;

    public async Task CancelAsync(Reservation r, string by, string? reason, bool byCustomer)
    {
        r.Status = ReservationStatus.Cancelled;
        r.CancelledAt = DateTime.UtcNow;
        r.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        r.Events.Add(new ReservationEvent { At = DateTime.UtcNow, Title = "İptal edildi", By = by, Note = r.CancelReason });
        mailer.ReservationCancelled(r, r.Car, byCustomer);
        await db.SaveChangesAsync();
    }

    public async Task PickUpAsync(Reservation r, string by, bool notify = true)
    {
        r.Status = ReservationStatus.Active;
        r.Events.Add(new ReservationEvent { At = DateTime.UtcNow, Title = "Araç teslim edildi", By = by, Note = Catalog.FindBranch(r.PickupBranch)?.Name });
        if (notify) mailer.PickedUp(r, r.Car);
        await db.SaveChangesAsync();
    }

    public async Task CompleteAsync(Reservation r, string by, bool notify = true)
    {
        r.Status = ReservationStatus.Completed;
        r.Events.Add(new ReservationEvent { At = DateTime.UtcNow, Title = "Araç iade alındı", By = by, Note = Catalog.FindBranch(r.ReturnBranch)?.Name });
        if (notify) mailer.Completed(r, r.Car);
        await db.SaveChangesAsync();
    }
}

/// Saatlik işler: yarın teslim alınacaklara hatırlatma, örnek verinin zamanla ilerlemesi.
public class HousekeepingWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<HousekeepingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stop).ContinueWith(_ => { });
        while (!stop.IsCancellationRequested)
        {
            try { await RunAsync(); }
            catch (Exception ex) when (!stop.IsCancellationRequested) { log.LogError(ex, "Saatlik işler çalışamadı"); }
            await Task.Delay(TimeSpan.FromMinutes(30), stop).ContinueWith(_ => { });
        }
    }

    async Task RunAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var mailer = scope.ServiceProvider.GetRequiredService<Mailer>();
        var reservations = scope.ServiceProvider.GetRequiredService<Reservations>();
        var now = Clock.Now;

        var dueReminders = await db.Reservations.Include(r => r.Car)
            .Where(r => !r.IsDemo && r.Status == ReservationStatus.Confirmed && r.ReminderSentAt == null
                && r.PickupAt > now && r.PickupAt <= now.AddHours(24)
                && r.CreatedAt < DateTime.UtcNow.AddHours(-2))
            .ToListAsync();
        foreach (var r in dueReminders)
        {
            mailer.Reminder(r, r.Car);
            r.ReminderSentAt = DateTime.UtcNow;
            r.Events.Add(new ReservationEvent { At = DateTime.UtcNow, Title = "Hatırlatma e-postası gönderildi", By = "Sistem" });
        }
        await db.SaveChangesAsync();

        // Örnek rezervasyonlar: teslim saati gelince kirada, iade saati geçince tamamlandı.
        var demo = await db.Reservations.Include(r => r.Car).Include(r => r.Events)
            .Where(r => r.IsDemo && ((r.Status == ReservationStatus.Confirmed && r.PickupAt <= now)
                || (r.Status == ReservationStatus.Active && r.ReturnAt <= now)))
            .ToListAsync();
        foreach (var r in demo)
        {
            if (r.Status == ReservationStatus.Confirmed) await reservations.PickUpAsync(r, "Şube ekibi", notify: false);
            if (r.Status == ReservationStatus.Active && r.ReturnAt <= now) await reservations.CompleteAsync(r, "Şube ekibi", notify: false);
        }

        if (config.GetValue("Demo:SeedReservations", false))
            await Seeder.TopUpDemoAsync(db, availability: scope.ServiceProvider.GetRequiredService<Availability>(), log);
    }
}
