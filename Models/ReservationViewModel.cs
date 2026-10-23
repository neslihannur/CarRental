using System.ComponentModel.DataAnnotations;

namespace CarRental.Models;

public class ReservationViewModel
{
    [Required]
    public int CarId { get; set; }

    [Required(ErrorMessage = "Alış noktası seçin.")]
    public string PickupLocation { get; set; } = "";

    [Required(ErrorMessage = "Alış tarihi girin.")]
    public DateOnly? PickupDate { get; set; }

    [Required(ErrorMessage = "Dönüş tarihi girin.")]
    public DateOnly? ReturnDate { get; set; }

    [Required(ErrorMessage = "Adınızı yazın."), StringLength(80)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Telefon numaranızı yazın."), Phone, StringLength(20)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "E-posta adresinizi yazın."), EmailAddress, StringLength(120)]
    public string Email { get; set; } = "";

    /// Botlar için gizli alan; doluysa istek yok sayılır.
    public string? Website { get; set; }
}

public class HomeViewModel
{
    public required IReadOnlyList<Car> Cars { get; init; }
    public string? Category { get; init; }
    public string? Location { get; init; }
    public DateOnly PickupDate { get; init; }
    public DateOnly ReturnDate { get; init; }
    public bool Filtered { get; init; }
}
