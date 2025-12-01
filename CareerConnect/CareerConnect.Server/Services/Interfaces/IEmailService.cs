namespace CareerConnect.Server.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendVerificationCodeAsync(string email, string code, string verificationType);
    }
}