using System.ComponentModel.DataAnnotations;

namespace CareerConnect.Server.DTOs
{
    public class SocialLoginDto
    {
        [Required]
        public string Provider { get; set; } = string.Empty; // "Google", "Facebook", "LinkedIn"

        [Required]
        public string AccessToken { get; set; } = string.Empty;

        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? ProviderId { get; set; }
    }
}
