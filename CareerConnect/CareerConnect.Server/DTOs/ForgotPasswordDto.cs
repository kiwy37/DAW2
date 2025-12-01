using System.ComponentModel.DataAnnotations;

namespace CareerConnect.Server.DTOs
{
    // ==================== Forgot Password DTOs ====================
    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "Email-ul este obligatoriu")]
        [EmailAddress(ErrorMessage = "Format email invalid")]
        public string Email { get; set; } = string.Empty;
    }
}
