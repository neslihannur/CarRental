namespace CarRental.Models;

public record Car(
    int Id,
    string Brand,
    string Model,
    string Category,
    string Image,
    string Transmission,
    string Fuel,
    int Seats,
    int Year,
    decimal DailyPrice,
    string Tagline);

public static class Fleet
{
    public static readonly string[] Categories = ["Sedan", "SUV", "Spor", "Arazi"];

    public static readonly string[] Locations =
    [
        "İstanbul Havalimanı", "Sabiha Gökçen Havalimanı", "İstanbul - Kadıköy",
        "İstanbul - Levent", "Ankara Esenboğa", "İzmir Adnan Menderes", "Antalya Havalimanı",
    ];

    public static readonly IReadOnlyList<Car> Cars =
    [
        new(1, "BMW", "5 Serisi 520i", "Sedan", "item-large5.jpg", "Otomatik", "Benzin", 5, 2024, 4_250, "İş seyahatleri için konforlu ve sessiz."),
        new(2, "BMW", "2 Serisi Coupé", "Sedan", "item-large4.jpg", "Otomatik", "Benzin", 4, 2023, 3_400, "Şehir içinde çevik, uzun yolda keyifli."),
        new(3, "BMW", "M5 Competition", "Spor", "car2.png", "Otomatik", "Benzin", 5, 2024, 9_800, "625 beygir, aile sedanı kılığında."),
        new(4, "Mercedes-Benz", "G 400d", "Arazi", "item-large2.jpg", "Otomatik", "Dizel", 5, 2024, 12_500, "Her zeminde efsane, her yerde dikkat çekici."),
        new(5, "Mercedes-AMG", "GT", "Spor", "item-large8.jpg", "Otomatik", "Benzin", 2, 2023, 14_000, "Özel günler için iki kişilik saf performans."),
        new(6, "Porsche", "911 Carrera", "Spor", "item-large1.jpg", "Otomatik", "Benzin", 4, 2022, 15_500, "Zamansız tasarım, unutulmaz bir sürüş."),
        new(7, "Ferrari", "458 Speciale", "Spor", "item-large9.jpg", "Otomatik", "Benzin", 2, 2021, 32_000, "Hafta sonunu efsaneye çevirin."),
        new(8, "Land Rover", "Defender 110", "Arazi", "item-large3.jpg", "Otomatik", "Dizel", 7, 2024, 8_900, "Kamp, kayak ve keşif için hazır."),
        new(9, "Ford", "Expedition", "SUV", "car3.png", "Otomatik", "Benzin", 8, 2023, 6_200, "Kalabalık aileler için geniş ve güvenli."),
    ];

    public static Car? Find(int id) => Cars.FirstOrDefault(c => c.Id == id);
}
