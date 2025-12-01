using System.ComponentModel.DataAnnotations;

namespace CareerConnect.Server.DTOs
{
    public class ResetPasswordDto
    {
        [Required(ErrorMessage = "Email-ul este obligatoriu")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Codul este obligatoriu")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Codul trebuie să conțină exact 6 cifre")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Parola nouă este obligatorie")]
        [MinLength(6, ErrorMessage = "Parola trebuie să conțină minim 6 caractere")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
