using CareerConnect.Server.Models;

namespace CareerConnect.Server.Services.Interfaces
{
    public interface IVerificationService
    {
        Task<string> GenerateAndSendCodeAsync(string email, string verificationType, string? ipAddress = null);
        Task<bool> ValidateCodeAsync(string email, string code, string verificationType);
        Task CleanupExpiredCodesAsync();
    }
}