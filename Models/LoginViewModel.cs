namespace CarRental.Models
{
    public class LoginViewModel
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class CaptchaResponse
    {
        public bool Success { get; set; }
        public double Score { get; set; }
    }
}