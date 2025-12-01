using System.ComponentModel.DataAnnotations;

namespace CareerConnect.Server.DTOs
{
    public class LinkedInLoginDto
    {
        [Required(ErrorMessage = "Authorization code is required")]
        public string Code { get; set; } = string.Empty;
    }
}