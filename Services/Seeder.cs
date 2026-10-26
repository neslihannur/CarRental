using CarRental.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Services;

/// İlk açılışta filo, yönetici hesabı ve (açıksa) örnek rezervasyonlar.
public static class Seeder
{
    public static async Task RunAsync(IServiceProvider sp, IConfiguration config, ILogger log)
    {
        var db = sp.GetRequiredService<AppDb>();

        if (!await db.Cars.AnyAsync())
        {
            db.Cars.AddRange(Fleet());
            await db.SaveChangesAsync();
            log.LogInformation("Filo oluşturuldu");
        }

        if (!await db.AdminUsers.AnyAsync() && config["Admin:Email"] is { Length: > 0 } email && config["Admin:Password"] is { Length: > 0 } password)
        {
            var user = new AdminUser { Email = email.Trim().ToLowerInvariant() };
            user.PasswordHash = new PasswordHasher<AdminUser>().HashPassword(user, password);
            db.AdminUsers.Add(user);
            await db.SaveChangesAsync();
            log.LogInformation("Yönetici hesabı açıldı: {Email}", user.Email);
        }

        if (config.GetValue("Demo:SeedReservations", false) && !await db.Reservations.AnyAsync())
        {
            await SeedHistoryAsync(db);
            log.LogInformation("Örnek rezervasyonlar oluşturuldu");
        }
    }

    static readonly string[] FirstNames = ["Elif", "Mehmet", "Zeynep", "Can", "Ayşe", "Burak", "Deniz", "Emre", "Selin", "Mert", "Ece", "Kerem", "Defne", "Onur", "Gizem", "Barış", "İrem", "Kaan", "Melis", "Tolga", "Nazlı", "Serkan", "Pınar", "Arda"];
    static readonly string[] LastNames = ["Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Aydın", "Öztürk", "Arslan", "Doğan", "Koç", "Kurt", "Aksoy", "Polat", "Erdem", "Güneş", "Tekin", "Bulut"];

    static string Ascii(string s) => s.ToLower(Catalog.Tr)
        .Replace('ı', 'i').Replace('ş', 's').Replace('ç', 'c').Replace('ğ', 'g').Replace('ö', 'o').Replace('ü', 'u');

    /// Son 45 gün ve önümüzdeki 30 gün için tutarlı (aynı aracı çift kiralamayan) örnek veri.
    static async Task SeedHistoryAsync(AppDb db)
    {
        var cars = await db.Cars.OrderBy(c => c.SortOrder).ToListAsync();
        var rnd = new Random(1923);
        var list = new List<Reservation>();
        for (var attempt = 0; attempt < 260 && list.Count < 48; attempt++)
        {
            // Vitrin dolu görünmesin: önümüzdeki 6 güne az rezervasyon düşsün.
            var day = Clock.Today.AddDays(rnd.Next(-45, 31));
            if (day >= Clock.Today.AddDays(-3) && day <= Clock.Today.AddDays(6) && rnd.NextDouble() < 0.75) continue;
            TryAdd(list, cars, rnd, day, createdNow: false);
        }
        Advance(list);
        db.Reservations.AddRange(list);
        await db.SaveChangesAsync();
    }

    /// Örnek veri yaşlandıkça geleceğe birkaç yeni rezervasyon ekler; panel hep canlı görünür.
    public static async Task TopUpDemoAsync(AppDb db, Availability availability, ILogger log)
    {
        var now = Clock.Now;
        var upcoming = await db.Reservations.CountAsync(r => r.IsDemo && r.PickupAt > now && r.Status != ReservationStatus.Cancelled);
        if (upcoming >= 18) return;
        var lastDemo = await db.Reservations.Where(r => r.IsDemo).MaxAsync(r => (DateTime?)r.CreatedAt);
        if (lastDemo > DateTime.UtcNow.AddHours(-3)) return;

        var cars = await db.Cars.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync();
        var existing = await db.Reservations
            .Where(r => r.ReturnAt > now && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Active))
            .ToListAsync();
        var rnd = new Random();
        var added = new List<Reservation>();
        for (var i = 0; i < 12 && added.Count < 2; i++)
        {
            var pool = existing.Concat(added).ToList();
            if (TryAdd(pool, cars, rnd, Clock.Today.AddDays(rnd.Next(2, 30)), createdNow: true) is { } r) added.Add(r);
        }
        if (added.Count == 0) return;
        db.Reservations.AddRange(added);
        await db.SaveChangesAsync();
        log.LogInformation("{Count} örnek rezervasyon eklendi", added.Count);
    }

    static Reservation? TryAdd(List<Reservation> list, List<Car> cars, Random rnd, DateOnly day, bool createdNow)
    {
        // Uygun fiyatlı araçlar daha sık kiralanır.
        var weights = cars.Select(c => 1.0 / Math.Sqrt((double)c.DailyPrice) * c.Units).ToArray();
        var pick = rnd.NextDouble() * weights.Sum();
        var car = cars[^1];
        for (var i = 0; i < cars.Count; i++) { pick -= weights[i]; if (pick <= 0) { car = cars[i]; break; } }

        int[] lengths = [1, 2, 2, 3, 3, 3, 4, 4, 5, 6, 7, 7, 10, 14];
        var days = lengths[rnd.Next(lengths.Length)];
        var pickupAt = day.ToDateTime(TimeOnly.Parse(Catalog.Times[rnd.Next(2, Catalog.Times.Length - 4)]));
        var returnAt = pickupAt.AddDays(days).AddMinutes(rnd.Next(0, 3) * 30 - 30);

        var busy = list.Count(r => r.CarId == car.Id && r.Status != ReservationStatus.Cancelled
            && r.PickupAt < returnAt.AddHours(Catalog.TurnaroundHours) && r.ReturnAt > pickupAt.AddHours(-Catalog.TurnaroundHours));
        if (busy >= car.Units) return null;

        var branch = Catalog.Branches[rnd.Next(Catalog.Branches.Count)];
        var returnBranch = rnd.NextDouble() < 0.82 ? branch
            : Catalog.Branches.Where(b => b.City == branch.City && b != branch).DefaultIfEmpty(Catalog.Branches[rnd.Next(Catalog.Branches.Count)]).First();
        var extras = Catalog.Extras.Where(_ => rnd.NextDouble() < 0.28).Select(e => e.Code).ToList();
        var trip = new Trip(branch.Code, returnBranch.Code, pickupAt, returnAt);
        var q = Pricing.Calculate(car, trip, extras);

        var first = FirstNames[rnd.Next(FirstNames.Length)];
        var last = LastNames[rnd.Next(LastNames.Length)];
        var created = createdNow
            ? DateTime.UtcNow.AddMinutes(-rnd.Next(1, 90))
            : DateTime.SpecifyKind(pickupAt.AddHours(-3).AddDays(-rnd.Next(1, 21)).AddMinutes(-rnd.Next(0, 600)), DateTimeKind.Utc);
        if (created > DateTime.UtcNow) created = DateTime.UtcNow.AddMinutes(-rnd.Next(5, 2000));

        var r = new Reservation
        {
            Code = Reservations.NewCode(),
            AccessToken = Reservations.NewToken(),
            CarId = car.Id,
            PickupBranch = trip.PickupBranch,
            ReturnBranch = trip.ReturnBranch,
            PickupAt = pickupAt,
            ReturnAt = returnAt,
            Days = q.Days,
            DailyPrice = q.DailyPrice,
            BaseTotal = q.BaseTotal,
            Discount = q.Discount,
            ExtrasTotal = q.ExtrasTotal,
            OneWayFee = q.OneWayFee,
            Total = q.Total,
            Deposit = q.Deposit,
            Extras = extras,
            FullName = $"{first} {last}",
            Email = $"{Ascii(first)}.{Ascii(last)}@example.com",
            Phone = $"05{rnd.Next(30, 56)} {rnd.Next(100, 999)} {rnd.Next(10, 99)} {rnd.Next(10, 99)}",
            BirthDate = new DateOnly(rnd.Next(1965, 2000), rnd.Next(1, 13), rnd.Next(1, 28)),
            LicenseYear = rnd.Next(1990, 2018),
            FlightNo = branch.IsAirport && rnd.NextDouble() < 0.5 ? $"TK{rnd.Next(1000, 2999)}" : null,
            Status = ReservationStatus.Confirmed,
            CreatedAt = created,
            IsDemo = true,
        };
        r.Events.Add(new ReservationEvent { At = created, Title = "Rezervasyon oluşturuldu", By = "Müşteri", Note = "Web sitesi üzerinden" });
        r.Events.Add(new ReservationEvent { At = created, Title = "Onaylandı", By = "Sistem", Note = "Araç müsait; otomatik onay" });
        if (rnd.NextDouble() < 0.08)
        {
            r.Status = ReservationStatus.Cancelled;
            r.CancelledAt = created.AddHours(rnd.Next(2, 48));
            if (r.CancelledAt > DateTime.UtcNow) r.CancelledAt = DateTime.UtcNow;
            r.Events.Add(new ReservationEvent { At = r.CancelledAt.Value, Title = "İptal edildi", By = "Müşteri", Note = "Planlar değişti" });
        }
        list.Add(r);
        return r;
    }

    /// Geçmişteki örnekleri zamana göre teslim edildi/iade alındı durumuna getirir.
    static void Advance(List<Reservation> list)
    {
        var now = Clock.Now;
        foreach (var r in list.Where(r => r.Status == ReservationStatus.Confirmed && r.PickupAt <= now))
        {
            r.Status = ReservationStatus.Active;
            r.Events.Add(new ReservationEvent { At = DateTime.SpecifyKind(r.PickupAt.AddHours(-3).AddMinutes(12), DateTimeKind.Utc), Title = "Araç teslim edildi", By = "Şube ekibi", Note = Catalog.FindBranch(r.PickupBranch)?.Name });
            if (r.ReturnAt <= now)
            {
                r.Status = ReservationStatus.Completed;
                r.Events.Add(new ReservationEvent { At = DateTime.SpecifyKind(r.ReturnAt.AddHours(-3).AddMinutes(-20), DateTimeKind.Utc), Title = "Araç iade alındı", By = "Şube ekibi", Note = Catalog.FindBranch(r.ReturnBranch)?.Name });
            }
        }
    }

    static List<Car> Fleet() =>
    [
        new()
        {
            Slug = "bmw-520i", Brand = "BMW", Model = "5 Serisi 520i", Category = "Sedan", Image = "item-large5.jpg",
            Transmission = "Otomatik", Fuel = "Benzin", Seats = 5, Doors = 4, Luggage = 3, Year = 2024, Horsepower = 208,
            DailyPrice = 4_250, Deposit = 15_000, MinDriverAge = 23, MinLicenseYears = 2, DailyKmLimit = 400, Units = 3, SortOrder = 1,
            Tagline = "İş seyahatleri için konforlu ve sessiz.",
            Description = "Toplantıdan toplantıya koşarken bile dinlenmiş hissettiren bir iş sedanı. Geniş arka diz mesafesi, sessiz kabini ve 530 litrelik bagajıyla şehirlerarası yolculuklarda da ilk tercih.",
            Features = ["Adaptif hız sabitleyici", "Apple CarPlay / Android Auto", "Isıtmalı koltuklar", "Park asistanı", "Geri görüş kamerası", "Ambiyans aydınlatma"],
        },
        new()
        {
            Slug = "bmw-m5-competition", Brand = "BMW", Model = "M5 Competition", Category = "Sedan", Image = "car2.png",
            Transmission = "Otomatik", Fuel = "Benzin", Seats = 5, Doors = 4, Luggage = 3, Year = 2024, Horsepower = 625,
            DailyPrice = 9_800, Deposit = 45_000, MinDriverAge = 27, MinLicenseYears = 5, DailyKmLimit = 300, Units = 1, SortOrder = 2,
            Tagline = "625 beygir, aile sedanı kılığında.",
            Description = "Dört kapı, beş koltuk ve 3,3 saniyede 0-100. Hafta içi ailenin aracı, hafta sonu pistin yıldızı. M xDrive sayesinde her koşulda dengeli.",
            Features = ["M xDrive dört çeker", "M Sport egzoz", "Harman Kardon ses sistemi", "Head-up ekran", "Karbon seramik fren", "Adaptif süspansiyon"],
        },
        new()
        {
            Slug = "bmw-220i-coupe", Brand = "BMW", Model = "2 Serisi Coupé", Category = "Spor", Image = "item-large4.jpg",
            Transmission = "Otomatik", Fuel = "Benzin", Seats = 4, Doors = 2, Luggage = 2, Year = 2023, Horsepower = 184,
            DailyPrice = 3_400, Deposit = 12_000, MinDriverAge = 21, MinLicenseYears = 2, DailyKmLimit = 400, Units = 2, SortOrder = 3,
            Tagline = "Şehir içinde çevik, uzun yolda keyifli.",
            Description = "Arkadan itişli şasisi ve kompakt ölçüleriyle sürüş keyfini uygun fiyata getiren coupé. Hafta sonu kaçamakları için ideal.",
            Features = ["Arkadan itiş", "Sport koltuklar", "Apple CarPlay", "LED farlar", "Park sensörleri"],
        },
        new()
        {
            Slug = "mercedes-amg-gt", Brand = "Mercedes-AMG", Model = "GT R", Category = "Spor", Image = "item-large8.jpg",
            Transmission = "Otomatik", Fuel = "Benzin", Seats = 2, Doors = 2, Luggage = 1, Year = 2023, Horsepower = 585,
            DailyPrice = 14_000, Deposit = 60_000, MinDriverAge = 27, MinLicenseYears = 5, DailyKmLimit = 250, Units = 1, SortOrder = 4,
            Tagline = "Özel günler için iki kişilik saf performans.",
            Description = "Yeşil cehennemden ilham alan GT R; aktif aerodinamik, arka aks yönlendirmesi ve V8 biturbo motoruyla yol aracından çok bir pist silahı.",
            Features = ["4.0 V8 biturbo", "Arka aks yönlendirme", "AMG Track Pace", "Burmester ses sistemi", "Performans koltukları"],
        },
        new()
        {
            Slug = "porsche-911-carrera-964", Brand = "Porsche", Model = "911 Carrera (964)", Category = "Spor", Image = "item-large1.jpg",
            Transmission = "Manuel", Fuel = "Benzin", Seats = 4, Doors = 2, Luggage = 1, Year = 1991, Horsepower = 250,
            DailyPrice = 15_500, Deposit = 75_000, MinDriverAge = 28, MinLicenseYears = 7, DailyKmLimit = 200, Units = 1, SortOrder = 5,
            Tagline = "Zamansız tasarım, unutulmaz bir sürüş.",
            Description = "Tamamen restore edilmiş, orijinal parçalarla korunmuş bir klasik. Hidrolik direksiyonsuz ilk hissiyat, havayla soğutmalı boxer sesi… Çekimler ve özel günler için.",
            Features = ["Havayla soğutmalı boxer motor", "Orijinal restorasyon", "Manuel 5 ileri", "Koleksiyon aracı sigortası", "Teslimde kısa sürüş eğitimi"],
        },
        new()
        {
            Slug = "ferrari-458-speciale", Brand = "Ferrari", Model = "458 Speciale", Category = "Spor", Image = "item-large9.jpg",
            Transmission = "Otomatik", Fuel = "Benzin", Seats = 2, Doors = 2, Luggage = 1, Year = 2021, Horsepower = 605,
            DailyPrice = 32_000, Deposit = 150_000, MinDriverAge = 30, MinLicenseYears = 8, DailyKmLimit = 200, Units = 1, SortOrder = 6,
            Tagline = "Hafta sonunu efsaneye çevirin.",
            Description = "Atmosferik V8'in 9.000 devirdeki çığlığı. Speciale, Ferrari'nin son saf emişli orta motorlu V8'i ve bir sürücü otomobilinin tanımı.",
            Features = ["4.5 V8 atmosferik", "Yarış tipi koltuklar", "Side Slip Control", "Karbon iç trim", "Kişiye özel teslim"],
        },
        new()
        {
            Slug = "mercedes-g-400d", Brand = "Mercedes-Benz", Model = "G 400d", Category = "SUV", Image = "item-large2.jpg",
            Transmission = "Otomatik", Fuel = "Dizel", Seats = 5, Doors = 5, Luggage = 4, Year = 2024, Horsepower = 330,
            DailyPrice = 12_500, Deposit = 50_000, MinDriverAge = 27, MinLicenseYears = 5, DailyKmLimit = 300, Units = 1, SortOrder = 7,
            Tagline = "Her zeminde efsane, her yerde dikkat çekici.",
            Description = "Üç diferansiyel kilidi ve ikonik kutu tasarımıyla G-Serisi, şehirde lüks bir SUV, arazide ise durdurulamaz bir arazi aracı.",
            Features = ["3 diferansiyel kilidi", "Burmester ses sistemi", "360° kamera", "Masajlı koltuklar", "Widescreen kokpit"],
        },
        new()
        {
            Slug = "ford-expedition", Brand = "Ford", Model = "Expedition", Category = "SUV", Image = "car3.png",
            Transmission = "Otomatik", Fuel = "Benzin", Seats = 8, Doors = 5, Luggage = 5, Year = 2023, Horsepower = 400,
            DailyPrice = 6_200, Deposit = 25_000, MinDriverAge = 25, MinLicenseYears = 3, DailyKmLimit = 400, Units = 2, SortOrder = 8,
            Tagline = "Kalabalık aileler için geniş ve güvenli.",
            Description = "Üç sıra koltuk, sekiz kişi ve herkesin bavulu. Havalimanı transferlerinden uzun aile tatillerine kadar alan sıkıntısı yok.",
            Features = ["8 koltuk", "Üç bölgeli klima", "Arka eğlence ekranları", "Römork paketi", "Adaptif hız sabitleyici"],
        },
        new()
        {
            Slug = "land-rover-defender-110", Brand = "Land Rover", Model = "Defender 110", Category = "Arazi", Image = "item-large3.jpg",
            Transmission = "Manuel", Fuel = "Dizel", Seats = 7, Doors = 5, Luggage = 4, Year = 2015, Horsepower = 122,
            DailyPrice = 6_900, Deposit = 30_000, MinDriverAge = 25, MinLicenseYears = 3, DailyKmLimit = 300, Units = 2, SortOrder = 9,
            Tagline = "Kamp, kayak ve keşif için hazır.",
            Description = "Son nesil klasik Defender: tavan çadırına hazır bagaj rafı, çamur lastikleri ve basitliğin güveni. Asfaltın bittiği yerde başlar.",
            Features = ["Sürekli 4x4", "Tavan bagaj rafı", "Çamur lastikleri", "Çeki demiri", "Kamp paketi (isteğe bağlı)"],
        },
        new()
        {
            Slug = "suzuki-samurai", Brand = "Suzuki", Model = "Samurai", Category = "Arazi", Image = "item-large6.jpg",
            Transmission = "Manuel", Fuel = "Benzin", Seats = 4, Doors = 2, Luggage = 1, Year = 1997, Horsepower = 70,
            DailyPrice = 2_400, Deposit = 8_000, MinDriverAge = 21, MinLicenseYears = 2, DailyKmLimit = 250, Units = 2, SortOrder = 10,
            Tagline = "Küçük, hafif, her yere sığar.",
            Description = "Yaz tatili, sahil yolları ve dar köy yolları için eğlenceli bir klasik. Tavanı açılır, 4x4 vitesiyle kumsalda bile durmaz.",
            Features = ["Yarı zamanlı 4x4", "Açılır tavan", "Hafif gövde", "Klasik araç sigortası"],
        },
    ];
}
