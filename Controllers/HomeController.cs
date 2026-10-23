using CarRental.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace CarRental.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [HttpGet("/")]
        public IActionResult Index(string? tip, string? konum, DateOnly? alis, DateOnly? donus)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var pickup = alis is { } a && a >= today ? a : today.AddDays(1);
            var ret = donus is { } d && d > pickup ? d : pickup.AddDays(3);

            var cars = Fleet.Cars.AsEnumerable();
            if (!string.IsNullOrEmpty(tip) && Fleet.Categories.Contains(tip))
                cars = cars.Where(c => c.Category == tip);

            return View(new HomeViewModel
            {
                Cars = cars.ToList(),
                Category = tip,
                Location = konum,
                PickupDate = pickup,
                ReturnDate = ret,
                Filtered = tip is not null || alis is not null,
            });
        }

        [HttpPost("/rezervasyon")]
        [ValidateAntiForgeryToken]
        public IActionResult Reserve(ReservationViewModel model)
        {
            var car = Fleet.Find(model.CarId);
            var today = DateOnly.FromDateTime(DateTime.Today);

            if (!string.IsNullOrEmpty(model.Website))
                return Redirect("/");
            if (car is null)
                ModelState.AddModelError(nameof(model.CarId), "Araç bulunamadı.");
            if (model.PickupDate < today)
                ModelState.AddModelError(nameof(model.PickupDate), "Alış tarihi geçmişte olamaz.");
            if (model.ReturnDate <= model.PickupDate)
                ModelState.AddModelError(nameof(model.ReturnDate), "Dönüş tarihi alıştan sonra olmalı.");

            if (!ModelState.IsValid || car is null)
            {
                TempData["ReservationError"] = string.Join(" ", ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
                return Redirect("/#filo");
            }

            var days = model.ReturnDate!.Value.DayNumber - model.PickupDate!.Value.DayNumber;
            var code = "GC" + Random.Shared.Next(100000, 999999);
            _logger.LogInformation("Rezervasyon {Code}: {Car}, {Days} gün", code, car.Model, days);

            TempData["ReservationCode"] = code;
            TempData["ReservationCar"] = $"{car.Brand} {car.Model}";
            TempData["ReservationSummary"] =
                $"{model.PickupLocation} · {model.PickupDate:dd.MM.yyyy} – {model.ReturnDate:dd.MM.yyyy} · {days} gün";
            TempData["ReservationTotal"] = (car.DailyPrice * days).ToString("N0", Tr) + " ₺";
            TempData["ReservationName"] = model.FullName;
            return Redirect("/#rezervasyon");
        }

        [HttpGet("/gizlilik")]
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        static readonly System.Globalization.CultureInfo Tr = new("tr-TR");
    }
}
