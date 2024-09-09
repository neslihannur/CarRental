using CarRental.Models;
using Microsoft.AspNetCore.Mvc;
using RestSharp;
using System.Diagnostics;

namespace CarRental.Controllers
{
    public class HomeController : Controller
    {
        string connectionString = "Server=104.247.162.242\\MSSQLSERVER2019;Initial Catalog=nazlisun_CarRentalDb; User Id=nazlisun_CarRental;Password=Nazli.55?; TrustServerCertificate=True";

        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        public IActionResult Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model); // Model ge�ersizse formu tekrar g�ster
            }

            var captchaToken = Request.Form["g-recaptcha-response"]; // reCAPTCHA token'�n� al

            if (!VerifyCaptcha(captchaToken))
            {
                ViewBag.CaptchaError = true; // Hata mesaj�
                return View(model); // Hatal� giri�te formu tekrar g�ster
            }

            // Kullan�c� bilgilerini kontrol etme (�rne�in, veritaban� ile)
            // Kullan�c� do�rulamas� ba�ar�l�ysa
            // Giri� i�lemleri (�rne�in, oturum a�ma)

            return RedirectToAction("Index", "Home"); // Ba�ar�l� giri�te anasayfaya y�nlendir
        }
        public bool VerifyCaptcha(string captchaToken)
        {
            var client = new RestClient("https://www.google.com/recaptcha");
            var request = new RestRequest("api/siteverify", Method.Post);
            request.AddParameter("secret", "6Lc5BTwqAAAAAKgTlpPBF_dYw-Xt_ouAOI6rcuVj"); // Secret anahtar�n�z� buraya ekleyin
            request.AddParameter("response", captchaToken);

            var response = client.Execute<CaptchaResponse>(request);

            return response.Data.Success;
        }

    }
}
