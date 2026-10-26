using System.Security.Claims;
using CarRental.Data;
using CarRental.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Controllers.Admin;

public record DayStat(DateOnly Day, decimal Revenue, int Count);

public class DashboardModel
{
    public required List<Reservation> PickupsToday { get; init; }
    public required List<Reservation> ReturnsToday { get; init; }
    public required List<Reservation> Latest { get; init; }
    public required List<DayStat> Last30 { get; init; }
    public int ActiveCount { get; init; }
    public int UpcomingCount { get; init; }
    public decimal MonthRevenue { get; init; }
    public decimal PrevMonthRevenue { get; init; }
    public int Occupancy { get; init; }
    public int UnreadMessages { get; init; }
    public int FailedEmails { get; init; }
    public required List<(string Name, int Count, decimal Revenue)> TopCars { get; init; }
}

/// /yonetim: rezervasyonlar, filo, mesajlar ve e-posta kaydı. "demo" rolü salt okunur ve kişisel verileri maskeli görür.
[Route("yonetim"), Authorize]
public class AdminController(AppDb db, Reservations reservations, Mailer mailer, IConfiguration config) : Controller
{
    static readonly PasswordHasher<AdminUser> Hasher = new();

    bool IsDemo => User.IsInRole("demo");
    string Actor => IsDemo ? "Demo" : User.FindFirstValue(ClaimTypes.Email) ?? "Yönetici";

    // ---------- Giriş ----------

    [HttpGet("giris"), AllowAnonymous]
    public IActionResult Login(string? returnUrl) => View("Login", returnUrl);

    [HttpPost("giris"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(string? email, string? password, string? returnUrl, bool demo = false)
    {
        Claim[] claims;
        if (demo)
        {
            if (!config.GetValue("Demo:Enabled", false)) return Redirect("/yonetim/giris");
            claims = [new(ClaimTypes.NameIdentifier, "0"), new(ClaimTypes.Email, "demo"), new(ClaimTypes.Role, "demo")];
        }
        else
        {
            var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Email == (email ?? "").Trim().ToLower());
            var result = user is null ? PasswordVerificationResult.Failed : Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
            if (user is null || result == PasswordVerificationResult.Failed)
            {
                ViewData["Error"] = "E-posta ya da şifre hatalı.";
                return View("Login", returnUrl);
            }
            if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = Hasher.HashPassword(user, password!);
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            claims = [new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Email, user.Email), new(ClaimTypes.Role, "admin")];
        }
        await HttpContext.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = !demo });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/yonetim");
    }

    [HttpPost("cikis")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return Redirect("/yonetim/giris");
    }

    // ---------- Pano ----------

    [HttpGet("")]
    public async Task<IActionResult> Dashboard()
    {
        var now = Clock.Now;
        var today = Clock.Today.ToDateTime(TimeOnly.MinValue);
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var prevMonthStart = monthStart.AddMonths(-1);
        var live = new[] { ReservationStatus.Confirmed, ReservationStatus.Active, ReservationStatus.Completed };

        var since = DateTime.SpecifyKind(today.AddDays(-29).AddHours(-3), DateTimeKind.Utc);
        var recent = await db.Reservations.Where(r => r.CreatedAt >= since && r.Status != ReservationStatus.Cancelled)
            .Select(r => new { r.CreatedAt, r.Total }).ToListAsync();
        var last30 = Enumerable.Range(0, 30).Select(i => DateOnly.FromDateTime(today.AddDays(-29 + i))).Select(d =>
        {
            var items = recent.Where(r => DateOnly.FromDateTime(r.CreatedAt.AddHours(3)) == d).ToList();
            return new DayStat(d, items.Sum(x => x.Total), items.Count);
        }).ToList();

        var units = await db.Cars.Where(c => c.IsActive).SumAsync(c => c.Units);
        var busyNow = await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Active
            || (r.Status == ReservationStatus.Confirmed && r.PickupAt <= now && r.ReturnAt > now));

        var top = await db.Reservations.Where(r => r.PickupAt >= prevMonthStart && live.Contains(r.Status))
            .GroupBy(r => r.Car.Brand + " " + r.Car.Model)
            .Select(g => new { g.Key, Count = g.Count(), Revenue = g.Sum(x => x.Total) })
            .OrderByDescending(g => g.Revenue).Take(5).ToListAsync();

        return View("Dashboard", new DashboardModel
        {
            PickupsToday = await db.Reservations.Include(r => r.Car)
                .Where(r => r.PickupAt >= today && r.PickupAt < tomorrow && r.Status != ReservationStatus.Cancelled)
                .OrderBy(r => r.PickupAt).ToListAsync(),
            ReturnsToday = await db.Reservations.Include(r => r.Car)
                .Where(r => r.ReturnAt >= today && r.ReturnAt < tomorrow && r.Status != ReservationStatus.Cancelled)
                .OrderBy(r => r.ReturnAt).ToListAsync(),
            Latest = await db.Reservations.Include(r => r.Car).OrderByDescending(r => r.CreatedAt).Take(8).ToListAsync(),
            Last30 = last30,
            ActiveCount = await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Active),
            UpcomingCount = await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Confirmed && r.PickupAt > now),
            MonthRevenue = await db.Reservations.Where(r => r.PickupAt >= monthStart && r.PickupAt < monthStart.AddMonths(1) && live.Contains(r.Status)).SumAsync(r => r.Total),
            PrevMonthRevenue = await db.Reservations.Where(r => r.PickupAt >= prevMonthStart && r.PickupAt < monthStart && live.Contains(r.Status)).SumAsync(r => r.Total),
            Occupancy = units == 0 ? 0 : (int)Math.Round(100.0 * Math.Min(busyNow, units) / units),
            UnreadMessages = await db.ContactMessages.CountAsync(m => !m.IsRead),
            FailedEmails = await db.EmailMessages.CountAsync(m => m.Status == EmailStatus.Failed),
            TopCars = top.Select(t => (t.Key, t.Count, t.Revenue)).ToList(),
        });
    }

    // ---------- Rezervasyonlar ----------

    [HttpGet("rezervasyonlar")]
    public async Task<IActionResult> Reservations(string? durum, string? q, string? zaman, int sayfa = 1)
    {
        var query = db.Reservations.Include(r => r.Car).AsQueryable();
        if (Enum.TryParse<ReservationStatus>(durum, out var st)) query = query.Where(r => r.Status == st);
        var now = Clock.Now;
        if (zaman == "gelecek") query = query.Where(r => r.PickupAt > now);
        if (zaman == "gecmis") query = query.Where(r => r.ReturnAt < now);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(r => r.Code.ToLower().Contains(term) || r.FullName.ToLower().Contains(term)
                || r.Email.Contains(term) || r.Phone.Contains(term) || (r.Car.Brand + " " + r.Car.Model).ToLower().Contains(term));
        }
        const int size = 25;
        var total = await query.CountAsync();
        sayfa = Math.Clamp(sayfa, 1, Math.Max(1, (total + size - 1) / size));
        var list = await query.OrderByDescending(r => r.CreatedAt).Skip((sayfa - 1) * size).Take(size).ToListAsync();
        ViewData["Durum"] = durum; ViewData["Q"] = q; ViewData["Zaman"] = zaman;
        ViewData["Sayfa"] = sayfa; ViewData["SayfaSayisi"] = Math.Max(1, (total + size - 1) / size); ViewData["Toplam"] = total;
        ViewData["Counts"] = await db.Reservations.GroupBy(r => r.Status).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);
        return View("Reservations", list);
    }

    [HttpGet("rezervasyonlar/{id:int}")]
    public async Task<IActionResult> Reservation(int id)
    {
        var r = await db.Reservations.Include(x => x.Car).Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();
        ViewData["Emails"] = await db.EmailMessages.Where(m => m.ReservationId == id).OrderByDescending(m => m.Id).ToListAsync();
        return View("Reservation", r);
    }

    [HttpPost("rezervasyonlar/{id:int}/{act}"), Authorize(Roles = "admin")]
    public async Task<IActionResult> ReservationAction(int id, string act, string? reason, string? note)
    {
        var r = await db.Reservations.Include(x => x.Car).Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();
        switch (act)
        {
            case "teslim" when r.Status == ReservationStatus.Confirmed:
                await reservations.PickUpAsync(r, Actor); TempData["Flash"] = "Araç teslim edildi olarak işaretlendi."; break;
            case "iade" when r.Status == ReservationStatus.Active:
                await reservations.CompleteAsync(r, Actor); TempData["Flash"] = "Araç iade alındı; kiralama tamamlandı."; break;
            case "iptal" when r.Status == ReservationStatus.Confirmed:
                await reservations.CancelAsync(r, Actor, reason, byCustomer: false); TempData["Flash"] = "Rezervasyon iptal edildi; müşteriye bilgi verildi."; break;
            case "tekrar-gonder":
                mailer.ResendConfirmation(r, r.Car);
                r.Events.Add(new ReservationEvent { At = DateTime.UtcNow, Title = "Onay e-postası yeniden gönderildi", By = Actor });
                await db.SaveChangesAsync(); TempData["Flash"] = "Onay e-postası kuyruğa alındı."; break;
            case "not":
                r.AdminNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
                await db.SaveChangesAsync(); TempData["Flash"] = "Not kaydedildi."; break;
        }
        return Redirect($"/yonetim/rezervasyonlar/{id}");
    }

    // ---------- Filo ----------

    [HttpGet("filo")]
    public async Task<IActionResult> Fleet()
    {
        var now = Clock.Now;
        ViewData["Busy"] = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Active || (r.Status == ReservationStatus.Confirmed && r.PickupAt <= now && r.ReturnAt > now))
            .GroupBy(r => r.CarId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        ViewData["Upcoming"] = await db.Reservations.Where(r => r.Status == ReservationStatus.Confirmed && r.PickupAt > now)
            .GroupBy(r => r.CarId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        return View("Fleet", await db.Cars.OrderBy(c => c.SortOrder).ToListAsync());
    }

    [HttpGet("filo/{id:int}")]
    public async Task<IActionResult> EditCar(int id) =>
        await db.Cars.FindAsync(id) is { } car ? View("EditCar", car) : NotFound();

    [HttpPost("filo/{id:int}"), Authorize(Roles = "admin")]
    public async Task<IActionResult> EditCar(int id, decimal dailyPrice, decimal deposit, int units, int dailyKmLimit,
        int minDriverAge, int minLicenseYears, string? tagline, string? description, string? features, bool isActive)
    {
        var car = await db.Cars.FindAsync(id);
        if (car is null) return NotFound();
        if (dailyPrice < 100 || units < 0 || units > 50 || deposit < 0)
        {
            ViewData["Error"] = "Fiyat en az 100 ₺, adet 0-50 arası olmalı.";
            return View("EditCar", car);
        }
        car.DailyPrice = dailyPrice; car.Deposit = deposit; car.Units = units; car.DailyKmLimit = dailyKmLimit;
        car.MinDriverAge = minDriverAge; car.MinLicenseYears = minLicenseYears;
        car.Tagline = tagline?.Trim() ?? ""; car.Description = description?.Trim() ?? "";
        car.Features = (features ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        car.IsActive = isActive;
        await db.SaveChangesAsync();
        TempData["Flash"] = $"{car.Name} güncellendi.";
        return Redirect("/yonetim/filo");
    }

    // ---------- Mesajlar ----------

    [HttpGet("mesajlar")]
    public async Task<IActionResult> Messages(string? tur) =>
        View("Messages", await db.ContactMessages.Where(m => tur == null || m.Kind == tur).OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync());

    [HttpGet("mesajlar/{id:int}")]
    public async Task<IActionResult> Message(int id)
    {
        var m = await db.ContactMessages.FindAsync(id);
        if (m is null) return NotFound();
        if (!m.IsRead && !IsDemo) { m.IsRead = true; await db.SaveChangesAsync(); }
        return View("Message", m);
    }

    // ---------- E-postalar ----------

    [HttpGet("epostalar")]
    public async Task<IActionResult> Emails(string? durum)
    {
        var q = db.EmailMessages.AsQueryable();
        if (Enum.TryParse<EmailStatus>(durum, out var st)) q = q.Where(m => m.Status == st);
        ViewData["Durum"] = durum;
        ViewData["SmtpReady"] = !string.IsNullOrEmpty(config["Smtp:Host"]) || !string.IsNullOrEmpty(config["Smtp:PickupDirectory"]);
        return View("Emails", await q.OrderByDescending(m => m.Id).Take(150)
            .Select(m => new EmailMessage { Id = m.Id, To = m.To, Subject = m.Subject, Kind = m.Kind, Status = m.Status, Attempts = m.Attempts, LastError = m.LastError, CreatedAt = m.CreatedAt, SentAt = m.SentAt, ReservationId = m.ReservationId })
            .ToListAsync());
    }

    /// E-posta önizlemesi (iframe içinde gösterilir).
    [HttpGet("epostalar/{id:int}/onizleme")]
    public async Task<IActionResult> EmailPreview(int id)
    {
        var m = await db.EmailMessages.FindAsync(id);
        if (m is null) return NotFound();
        var html = IsDemo ? Mask.EmailBody(m.Html) : m.Html;
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src * data:; style-src 'unsafe-inline'";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost("epostalar/{id:int}/tekrar"), Authorize(Roles = "admin")]
    public async Task<IActionResult> RetryEmail(int id)
    {
        var m = await db.EmailMessages.FindAsync(id);
        if (m is null) return NotFound();
        m.Status = EmailStatus.Pending; m.Attempts = 0; m.NextAttemptAt = DateTime.UtcNow; m.LastError = null;
        await db.SaveChangesAsync();
        TempData["Flash"] = "E-posta tekrar kuyruğa alındı.";
        return Redirect("/yonetim/epostalar");
    }

    [HttpPost("epostalar/deneme"), Authorize(Roles = "admin")]
    public async Task<IActionResult> TestEmail()
    {
        var to = config["Site:NotifyEmail"] ?? User.FindFirstValue(ClaimTypes.Email)!;
        mailer.Test(to);
        await db.SaveChangesAsync();
        TempData["Flash"] = $"Deneme e-postası {to} adresine kuyruğa alındı.";
        return Redirect("/yonetim/epostalar");
    }

    // ---------- Şifre ----------

    [HttpGet("sifre"), Authorize(Roles = "admin")]
    public IActionResult Password() => View("Password");

    [HttpPost("sifre"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Password(string current, string next, string confirm)
    {
        var user = await db.AdminUsers.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (user is null) return Redirect("/yonetim/giris");
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, current ?? "") == PasswordVerificationResult.Failed)
            ViewData["Error"] = "Mevcut şifre hatalı.";
        else if ((next ?? "").Length < 10)
            ViewData["Error"] = "Yeni şifre en az 10 karakter olmalı.";
        else if (next != confirm)
            ViewData["Error"] = "Yeni şifreler aynı değil.";
        else
        {
            user.PasswordHash = Hasher.HashPassword(user, next!);
            await db.SaveChangesAsync();
            TempData["Flash"] = "Şifre değiştirildi.";
            return Redirect("/yonetim/sifre");
        }
        return View("Password");
    }
}

/// Demo hesabında gerçek ziyaretçi verisi gösterilmez.
public static class Mask
{
    public static string Name(string full) => string.Join(' ', full.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select((p, i) => i == 0 ? p : p[0] + "."));

    public static string Email(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" : email[0] + new string('•', Math.Min(6, at - 1)) + email[at..];
    }

    public static string Phone(string phone)
    {
        var digits = phone.Where(char.IsDigit).ToArray();
        return digits.Length < 4 ? "•••" : $"{new string(digits[..4])} ••• •• {new string(digits[^2..])}";
    }

    public static string EmailBody(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", m => Email(m.Value));
}
