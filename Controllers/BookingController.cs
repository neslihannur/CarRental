using System.Security.Cryptography;
using System.Text;
using CarRental.Data;
using CarRental.Models;
using CarRental.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Controllers;

public class BookingController(AppDb db, Availability availability, Reservations reservations) : Controller
{
    [HttpGet("/rezervasyon/{slug}")]
    public async Task<IActionResult> Book(string slug, [FromQuery] TripFields t, [FromQuery] List<string> ek)
    {
        var car = await db.Cars.FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive);
        if (car is null) return NotFound();
        var trip = t.ToTrip();
        var form = new BookingForm { Ek = ek };
        CopyTrip(TripFields.From(trip), form);
        var available = await availability.IsAvailableAsync(car, trip);
        return View(new BookingPage
        {
            Car = car, Form = form, Quote = Pricing.Calculate(car, trip, ek),
            Error = available ? null : "Seçtiğiniz tarihlerde bu araç dolu. Tarihleri değiştirin ya da benzer bir araç seçin.",
        });
    }

    [HttpPost("/rezervasyon/{slug}"), EnableRateLimiting("booking")]
    public async Task<IActionResult> Book(string slug, BookingForm form)
    {
        var car = await db.Cars.FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive);
        if (car is null) return NotFound();
        if (!string.IsNullOrEmpty(form.Website)) return Redirect("/");

        var tripError = form.Validate(out var trip);
        if (tripError is not null) ModelState.AddModelError("", tripError);

        if (form.BirthDate is { } birth)
        {
            var pickupDay = DateOnly.FromDateTime(trip.PickupAt);
            var age = pickupDay.Year - birth.Year - (pickupDay < birth.AddYears(pickupDay.Year - birth.Year) ? 1 : 0);
            if (age < car.MinDriverAge)
                ModelState.AddModelError(nameof(form.BirthDate), $"Bu araç için sürücü en az {car.MinDriverAge} yaşında olmalı.");
            if (form.LicenseYear is { } ly)
            {
                if (ly > Clock.Today.Year || ly < birth.Year + 17)
                    ModelState.AddModelError(nameof(form.LicenseYear), "Ehliyet yılı geçerli görünmüyor.");
                else if (trip.PickupAt.Year - ly < car.MinLicenseYears)
                    ModelState.AddModelError(nameof(form.LicenseYear), $"Bu araç için en az {car.MinLicenseYears} yıllık ehliyet gerekiyor.");
            }
        }

        Reservation? r = null;
        if (ModelState.IsValid)
        {
            r = await reservations.CreateAsync(new BookingRequest(car, trip, form.Ek, form.FullName, form.Email, form.Phone,
                form.BirthDate!.Value, form.LicenseYear!.Value, form.FlightNo, form.Notes, HttpContext.Connection.RemoteIpAddress?.ToString()));
            if (r is null) ModelState.AddModelError("", "Üzgünüz, siz formu doldururken bu araç seçtiğiniz tarihler için kiralandı. Lütfen tarihleri değiştirin.");
        }

        if (r is null)
            return View(new BookingPage
            {
                Car = car, Form = form, Quote = Pricing.Calculate(car, tripError is null ? trip : form.ToTrip(), form.Ek),
            });

        return Redirect($"/rezervasyonum/{r.Code}?t={r.AccessToken}&yeni=1");
    }

    [HttpGet("/rezervasyonum")]
    public IActionResult Lookup() => View();

    [HttpPost("/rezervasyonum"), EnableRateLimiting("lookup")]
    public async Task<IActionResult> Lookup(string? kod, string? eposta)
    {
        var code = (kod ?? "").Trim().ToUpperInvariant();
        if (code.Length == 6 && !code.StartsWith("GC")) code = "GC-" + code;
        if (code.StartsWith("GC") && !code.StartsWith("GC-") && code.Length == 8) code = "GC-" + code[2..];
        var email = (eposta ?? "").Trim().ToLowerInvariant();
        var r = await db.Reservations.FirstOrDefaultAsync(x => x.Code == code && x.Email == email);
        if (r is null)
        {
            ViewData["Error"] = "Bu kod ve e-posta ile bir rezervasyon bulamadık. Onay e-postanızdaki bilgileri kontrol edin.";
            ViewData["Kod"] = kod;
            ViewData["Eposta"] = eposta;
            return View();
        }
        return Redirect($"/rezervasyonum/{r.Code}?t={r.AccessToken}");
    }

    [HttpGet("/rezervasyonum/{code}")]
    public async Task<IActionResult> Manage(string code, string? t, bool yeni = false)
    {
        var r = await FindAsync(code, t);
        if (r is null) return Redirect("/rezervasyonum");
        ViewData["New"] = yeni;
        return View(r);
    }

    [HttpPost("/rezervasyonum/{code}/iptal")]
    public async Task<IActionResult> Cancel(string code, string? t, string? reason)
    {
        var r = await FindAsync(code, t);
        if (r is null) return Redirect("/rezervasyonum");
        if (Reservations.CanCustomerCancel(r))
        {
            await reservations.CancelAsync(r, "Müşteri", reason, byCustomer: true);
            TempData["Flash"] = "Rezervasyonunuz iptal edildi. Onay e-postası yolda.";
        }
        return Redirect($"/rezervasyonum/{r.Code}?t={r.AccessToken}");
    }

    [HttpGet("/rezervasyonum/{code}/takvim.ics")]
    public async Task<IActionResult> Calendar(string code, string? t)
    {
        var r = await FindAsync(code, t);
        if (r is null) return NotFound();
        var url = $"{Request.Scheme}://{Request.Host}/rezervasyonum/{r.Code}?t={r.AccessToken}";
        return File(Encoding.UTF8.GetBytes(Ics.ForReservation(r, r.Car, url)), "text/calendar", $"gocar-{r.Code}.ics");
    }

    async Task<Reservation?> FindAsync(string code, string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var r = await db.Reservations.Include(x => x.Car).Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Code == code.ToUpperInvariant());
        if (r is null) return null;
        // Sabit süreli karşılaştırma: anahtar tahmin edilemesin.
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(r.AccessToken), Encoding.UTF8.GetBytes(token)) ? r : null;
    }

    static void CopyTrip(TripFields from, TripFields to)
    {
        to.Sube = from.Sube; to.DonusSube = from.DonusSube;
        to.Alis = from.Alis; to.AlisSaat = from.AlisSaat;
        to.Donus = from.Donus; to.DonusSaat = from.DonusSaat;
    }
}
