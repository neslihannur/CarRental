using CarRental.Data;
using Microsoft.AspNetCore.Html;

namespace CarRental.Services;

/// Görünümlerde tekrar eden küçük parçalar.
public static class Ui
{
    public static HtmlString Icon(string name, string? cls = null) =>
        new($"<svg class=\"i{(cls is null ? "" : " " + cls)}\" aria-hidden=\"true\"><use href=\"#i-{name}\"/></svg>");

    public static string StatusClass(ReservationStatus s) => s switch
    {
        ReservationStatus.Confirmed => "st-confirmed",
        ReservationStatus.Active => "st-active",
        ReservationStatus.Completed => "st-completed",
        _ => "st-cancelled",
    };

    public static string CarUrl(Car car, Trip trip) => $"/arac/{car.Slug}?{trip.Query}";
    public static string BookUrl(Car car, Trip trip) => $"/rezervasyon/{car.Slug}?{trip.Query}";

    public static string BranchName(string code) => Catalog.FindBranch(code)?.Name ?? code;
}
