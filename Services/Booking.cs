using CarRental.Data;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Services;

public record QuoteLine(string Label, decimal Amount);

public record Quote(
    int Days,
    decimal DailyPrice,
    decimal BaseTotal,
    decimal DiscountRate,
    decimal Discount,
    IReadOnlyList<QuoteLine> Extras,
    decimal ExtrasTotal,
    decimal OneWayFee,
    decimal Total,
    decimal Deposit);

/// Arama/rezervasyon girdisi: şubeler, tarih ve saatler.
public record Trip(string PickupBranch, string ReturnBranch, DateTime PickupAt, DateTime ReturnAt)
{
    public static Trip Default()
    {
        var day = Clock.Today.AddDays(1).ToDateTime(new TimeOnly(10, 0));
        return new("ist", "ist", day, day.AddDays(3));
    }

    /// Sorgu dizesinden (?alis=2026-11-02&alisSaat=10:00&donus=...) yolculuk; eksik/hatalı alanlar varsayılana döner.
    public static Trip From(string? sube, string? donusSube, DateOnly? alis, string? alisSaat, DateOnly? donus, string? donusSaat)
    {
        var d = Default();
        var pickupBranch = Catalog.FindBranch(sube)?.Code ?? d.PickupBranch;
        var returnBranch = Catalog.FindBranch(donusSube)?.Code ?? pickupBranch;
        var pTime = TimeOnly.TryParse(alisSaat, out var pt) ? pt : new TimeOnly(10, 0);
        var rTime = TimeOnly.TryParse(donusSaat, out var rt) ? rt : pTime;
        var pickup = alis is { } a ? a.ToDateTime(pTime) : d.PickupAt;
        if (pickup < Clock.Now.AddHours(2)) pickup = d.PickupAt;
        var ret = donus is { } r ? r.ToDateTime(rTime) : pickup.AddDays(3);
        if (ret <= pickup.AddHours(1)) ret = pickup.AddDays(3);
        return new(pickupBranch, returnBranch, pickup, ret);
    }

    public int Days => Pricing.DaysBetween(PickupAt, ReturnAt);

    public string Query =>
        $"sube={PickupBranch}&donusSube={ReturnBranch}&alis={PickupAt:yyyy-MM-dd}&alisSaat={PickupAt:HH\\:mm}&donus={ReturnAt:yyyy-MM-dd}&donusSaat={ReturnAt:HH\\:mm}";
}

public static class Pricing
{
    /// 24 saatlik dilimler; 1 saate kadar gecikme ek gün sayılmaz.
    public static int DaysBetween(DateTime pickup, DateTime ret) =>
        Math.Max(1, (int)Math.Ceiling(((ret - pickup).TotalHours - 1) / 24));

    public static decimal OneWayFee(string pickupBranch, string returnBranch)
    {
        if (pickupBranch == returnBranch) return 0;
        var a = Catalog.FindBranch(pickupBranch);
        var b = Catalog.FindBranch(returnBranch);
        return a?.City == b?.City ? Catalog.SameCityOneWayFee : Catalog.OtherCityOneWayFee;
    }

    public static Quote Calculate(Car car, Trip trip, IEnumerable<string> extras)
    {
        var days = trip.Days;
        var baseTotal = car.DailyPrice * days;
        var rate = Catalog.DiscountRate(days);
        var discount = Math.Round(baseTotal * rate, 0);
        var lines = extras.Distinct().Select(Catalog.FindExtra).OfType<Extra>()
            .Select(e => new QuoteLine(e.PerDay ? $"{e.Name} ({days} gün)" : e.Name, e.PerDay ? e.Price * days : e.Price))
            .ToList();
        var extrasTotal = lines.Sum(l => l.Amount);
        var oneWay = OneWayFee(trip.PickupBranch, trip.ReturnBranch);
        return new(days, car.DailyPrice, baseTotal, rate, discount, lines, extrasTotal, oneWay,
            baseTotal - discount + extrasTotal + oneWay, car.Deposit);
    }
}

public class Availability(AppDb db)
{
    static readonly ReservationStatus[] Blocking = [ReservationStatus.Confirmed, ReservationStatus.Active];

    /// Bu tarih aralığında aracın kaç adedi dolu (temizlik payı dahil).
    public Task<int> BusyUnitsAsync(int carId, DateTime pickup, DateTime ret, int? excludeId = null)
    {
        var from = pickup.AddHours(-Catalog.TurnaroundHours);
        var to = ret.AddHours(Catalog.TurnaroundHours);
        return db.Reservations.CountAsync(r => r.CarId == carId && Blocking.Contains(r.Status)
            && r.PickupAt < to && r.ReturnAt > from && r.Id != (excludeId ?? 0));
    }

    public async Task<bool> IsAvailableAsync(Car car, Trip trip) =>
        await BusyUnitsAsync(car.Id, trip.PickupAt, trip.ReturnAt) < car.Units;

    /// Aralıkta tüm adetleri dolu olan araçların kimlikleri (filo sayfası için tek sorgu).
    public async Task<HashSet<int>> FullCarIdsAsync(Trip trip)
    {
        var from = trip.PickupAt.AddHours(-Catalog.TurnaroundHours);
        var to = trip.ReturnAt.AddHours(Catalog.TurnaroundHours);
        var busy = await db.Reservations
            .Where(r => Blocking.Contains(r.Status) && r.PickupAt < to && r.ReturnAt > from)
            .GroupBy(r => r.CarId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var units = await db.Cars.ToDictionaryAsync(c => c.Id, c => c.Units);
        return busy.Where(b => b.Count >= units.GetValueOrDefault(b.Key, 1)).Select(b => b.Key).ToHashSet();
    }

    /// Önümüzdeki günlerden tamamen dolu olanlar (detay sayfasındaki takvim için).
    public async Task<List<DateOnly>> FullDaysAsync(Car car, int horizonDays = 120)
    {
        var start = Clock.Today;
        var end = start.AddDays(horizonDays);
        var endAt = end.ToDateTime(TimeOnly.MinValue);
        var startAt = start.ToDateTime(TimeOnly.MinValue);
        var list = await db.Reservations
            .Where(r => r.CarId == car.Id && Blocking.Contains(r.Status) && r.PickupAt < endAt && r.ReturnAt > startAt)
            .Select(r => new { r.PickupAt, r.ReturnAt }).ToListAsync();

        var full = new List<DateOnly>();
        for (var d = start; d < end; d = d.AddDays(1))
        {
            // Gün ortasında (12:00) araç kullanımda mı? Kısmi günler alınabilir sayılır.
            var noon = d.ToDateTime(new TimeOnly(12, 0));
            if (list.Count(r => r.PickupAt <= noon && r.ReturnAt >= noon) >= car.Units) full.Add(d);
        }
        return full;
    }

    /// Dolu araç için bu tarihten sonraki ilk uygun başlangıç (aynı süreyle).
    public async Task<DateTime?> NextFreeAsync(Car car, Trip trip)
    {
        var length = trip.ReturnAt - trip.PickupAt;
        for (var i = 1; i <= 60; i++)
        {
            var p = trip.PickupAt.AddDays(i);
            if (await BusyUnitsAsync(car.Id, p, p + length) < car.Units) return p;
        }
        return null;
    }
}
