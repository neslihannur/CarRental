using System.Text;
using CarRental.Data;
using CarRental.Models;
using CarRental.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Controllers;

public class HomeController(AppDb db, Availability availability, Mailer mailer, IConfiguration config) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var trip = Trip.Default();
        var cars = await db.Cars.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync();
        var full = await availability.FullCarIdsAsync(trip);
        return View(new HomePage
        {
            Trip = trip,
            Featured = cars.Select(c => new CarCard(c, Pricing.Calculate(c, trip, []), !full.Contains(c.Id))).ToList(),
            Vehicles = cars.Sum(c => c.Units),
            Rentals = await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Completed),
        });
    }

    [HttpGet("/kosullar")]
    public IActionResult Terms() => View();

    [HttpGet("/gizlilik")]
    public IActionResult Privacy() => View();

    [HttpGet("/iletisim")]
    public IActionResult Contact() => View(new ContactForm());

    [HttpPost("/iletisim"), EnableRateLimiting("contact")]
    public Task<IActionResult> Contact(ContactForm form) => SaveMessageAsync(form, "iletisim", "Contact");

    [HttpGet("/kurumsal")]
    public IActionResult Corporate() => View(new ContactForm());

    [HttpPost("/kurumsal"), EnableRateLimiting("contact")]
    public Task<IActionResult> Corporate(ContactForm form) => SaveMessageAsync(form, "kurumsal", "Corporate");

    async Task<IActionResult> SaveMessageAsync(ContactForm form, string kind, string view)
    {
        if (!string.IsNullOrEmpty(form.Website)) return Redirect($"/{kind}?gonderildi=1");
        if (kind == "kurumsal" && string.IsNullOrWhiteSpace(form.Company))
            ModelState.AddModelError(nameof(form.Company), "Şirket adını yazın.");
        if (!ModelState.IsValid) return View(view, form);

        var m = new ContactMessage
        {
            Kind = kind,
            Name = form.Name.Trim(),
            Email = form.Email.Trim().ToLowerInvariant(),
            Phone = form.Phone?.Trim(),
            Company = form.Company?.Trim(),
            FleetSize = form.FleetSize,
            Subject = form.Subject?.Trim(),
            Message = form.Message.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        db.ContactMessages.Add(m);
        await db.SaveChangesAsync();
        mailer.ContactReceived(m);
        await db.SaveChangesAsync();
        return Redirect($"/{kind}?gonderildi=1#form");
    }

    [HttpGet("/hata")]
    public IActionResult Error(int? kod)
    {
        Response.StatusCode = kod ?? 500;
        return View(kod ?? 500);
    }

    [HttpGet("/robots.txt")]
    public IActionResult Robots() =>
        Content($"User-agent: *\nDisallow: /yonetim\nDisallow: /rezervasyonum/\nSitemap: {BaseUrl}/sitemap.xml\n", "text/plain");

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var urls = new List<string> { "/", "/filo", "/kurumsal", "/iletisim", "/kosullar", "/gizlilik", "/rezervasyonum" };
        urls.AddRange(await db.Cars.Where(c => c.IsActive).OrderBy(c => c.SortOrder).Select(c => "/arac/" + c.Slug).ToListAsync());
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var u in urls) sb.Append($"  <url><loc>{BaseUrl}{u}</loc></url>\n");
        sb.Append("</urlset>\n");
        return Content(sb.ToString(), "application/xml");
    }

    string BaseUrl => (config["Site:BaseUrl"] ?? "").TrimEnd('/');
}
