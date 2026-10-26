using CarRental.Models;
using CarRental.Services;
using CarRental.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Controllers;

public class FleetController(AppDb db, Availability availability) : Controller
{
    [HttpGet("/filo")]
    public async Task<IActionResult> Index([FromQuery] TripFields t, string? tip, string? vites, string? yakit, int? koltuk, string? sirala)
    {
        var trip = t.ToTrip();
        var all = await db.Cars.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync();
        var full = await availability.FullCarIdsAsync(trip);

        IEnumerable<Car> cars = all;
        if (Catalog.Categories.Contains(tip)) cars = cars.Where(c => c.Category == tip);
        if (vites is "Otomatik" or "Manuel") cars = cars.Where(c => c.Transmission == vites);
        if (yakit is "Benzin" or "Dizel") cars = cars.Where(c => c.Fuel == yakit);
        if (koltuk is > 0) cars = cars.Where(c => c.Seats >= koltuk);

        var cards = cars.Select(c => new CarCard(c, Pricing.Calculate(c, trip, []), !full.Contains(c.Id)));
        sirala = sirala is "artan" or "azalan" or "guc" ? sirala : "onerilen";
        cards = sirala switch
        {
            "artan" => cards.OrderBy(c => c.Car.DailyPrice),
            "azalan" => cards.OrderByDescending(c => c.Car.DailyPrice),
            "guc" => cards.OrderByDescending(c => c.Car.Horsepower),
            _ => cards.OrderByDescending(c => c.Available).ThenBy(c => c.Car.SortOrder),
        };

        return View(new FleetPage
        {
            Trip = trip, Cars = cards.ToList(), Category = tip, Transmission = vites, Fuel = yakit, Seats = koltuk,
            Sort = sirala, TotalCount = all.Count,
        });
    }

    [HttpGet("/arac/{slug}")]
    public async Task<IActionResult> Detail(string slug, [FromQuery] TripFields t)
    {
        var car = await db.Cars.FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive);
        if (car is null) return NotFound();
        var trip = t.ToTrip();
        var available = await availability.IsAvailableAsync(car, trip);
        var full = await availability.FullCarIdsAsync(trip);
        var similar = await db.Cars.Where(c => c.IsActive && c.Id != car.Id && c.Category == car.Category).OrderBy(c => c.SortOrder).Take(3).ToListAsync();
        if (similar.Count < 3)
            similar.AddRange(await db.Cars.Where(c => c.IsActive && c.Id != car.Id && c.Category != car.Category)
                .OrderBy(c => Math.Abs(c.DailyPrice - car.DailyPrice)).Take(3 - similar.Count).ToListAsync());

        return View(new CarPage
        {
            Car = car,
            Trip = trip,
            Quote = Pricing.Calculate(car, trip, []),
            Available = available,
            NextFree = available ? null : await availability.NextFreeAsync(car, trip),
            FullDays = await availability.FullDaysAsync(car),
            Similar = similar.Select(c => new CarCard(c, Pricing.Calculate(c, trip, []), !full.Contains(c.Id))).ToList(),
        });
    }

    /// Canlı fiyat hesabı: arama çubuğu, detay ve rezervasyon sayfası kullanır.
    [HttpGet("/api/teklif"), EnableRateLimiting("quote")]
    public async Task<IActionResult> Quote(string arac, [FromQuery] TripFields t, [FromQuery] List<string> ek)
    {
        var car = await db.Cars.FirstOrDefaultAsync(c => c.Slug == arac && c.IsActive);
        if (car is null) return NotFound();
        var error = t.Validate(out var trip);
        if (error is not null) return Json(new { ok = false, error });

        var q = Pricing.Calculate(car, trip, ek);
        var available = await availability.IsAvailableAsync(car, trip);
        var next = available ? null : await availability.NextFreeAsync(car, trip);
        var age = car.MinDriverAge;
        return Json(new
        {
            ok = true,
            available,
            nextFree = next is { } n ? new { label = Catalog.DateTimeShort(n), query = new Trip(trip.PickupBranch, trip.ReturnBranch, n, n + (trip.ReturnAt - trip.PickupAt)).Query } : null,
            days = q.Days,
            daily = Catalog.Money(q.DailyPrice),
            baseTotal = Catalog.Money(q.BaseTotal),
            discountRate = (int)(q.DiscountRate * 100),
            discount = q.Discount > 0 ? "−" + Catalog.Money(q.Discount) : null,
            extras = q.Extras.Select(e => new { label = e.Label, amount = Catalog.Money(e.Amount) }),
            oneWay = q.OneWayFee > 0 ? Catalog.Money(q.OneWayFee) : null,
            total = Catalog.Money(q.Total),
            perDay = Catalog.Money(Math.Round(q.Total / q.Days)),
            deposit = Catalog.Money(q.Deposit),
            pickup = Catalog.DateTimeShort(trip.PickupAt),
            @return = Catalog.DateTimeShort(trip.ReturnAt),
            query = trip.Query,
            minAge = age,
        });
    }
}
