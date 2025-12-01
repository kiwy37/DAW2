using CareerConnect.Server.DTOs;
using CareerConnect.Server.Models;
using CareerConnect.Server.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login/initiate")]
        public async Task<ActionResult<PendingVerificationDto>> InitiateLogin([FromBody] LoginDto loginDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var response = await _authService.InitiateLoginAsync(loginDto, ipAddress);
            return Ok(response);
        }

        [HttpPost("login/complete")]
        public async Task<ActionResult<AuthResponseDto>> CompleteLogin([FromBody] VerifyCodeDto verifyCodeDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _authService.CompleteLoginAsync(verifyCodeDto);
            return Ok(response);
        }

        [HttpPost("register/initiate")]
        public async Task<ActionResult<PendingVerificationDto>> InitiateRegister([FromBody] CreateUserDto createUserDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var response = await _authService.InitiateRegisterAsync(createUserDto, ipAddress);
            return Ok(response);
        }

        [HttpPost("register/finalize")]
        public async Task<ActionResult<AuthResponseDto>> FinalizeRegister([FromBody] CreateUserWithCodeDto createUserWithCodeDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _authService.FinalizeRegisterWithVerificationAsync(createUserWithCodeDto);
            return CreatedAtAction(nameof(FinalizeRegister), new { id = response.User.Id }, response);
        }

        [HttpPost("resend-code")]
        public async Task<IActionResult> ResendCode([FromBody] ResendCodeDto resendCodeDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _authService.ResendVerificationCodeAsync(resendCodeDto, ipAddress);

            return Ok(new { message = "Verification code has been resent successfully" });
        }

        [HttpPost("google-login")]
        public async Task<ActionResult<AuthResponseDto>> GoogleLogin([FromBody] GoogleLoginDto googleLoginDto)
        {
            try
            {
                _logger.LogInformation("Google login request received");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for Google login");
                    return BadRequest(ModelState);
                }

                if (string.IsNullOrEmpty(googleLoginDto.IdToken))
                {
                    _logger.LogWarning("Empty IdToken received");
                    return BadRequest(new { error = "IdToken is required" });
                }

                _logger.LogInformation($"Processing Google login with token length: {googleLoginDto.IdToken.Length}");

                var response = await _authService.GoogleLoginAsync(googleLoginDto);

                _logger.LogInformation("Google login successful");
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized Google login attempt");
                return Unauthorized(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation during Google login");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Google login");
                return StatusCode(500, new { error = "An unexpected error occurred during Google login", details = ex.Message });
            }
        }

        [HttpPost("linkedin-login")]
        public async Task<ActionResult<AuthResponseDto>> LinkedInLogin([FromBody] LinkedInLoginDto linkedInLoginDto)
        {
            try
            {
                _logger.LogInformation("LinkedIn login request received");

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for LinkedIn login");
                    return BadRequest(ModelState);
                }

                var response = await _authService.LinkedInLoginAsync(linkedInLoginDto);

                _logger.LogInformation("LinkedIn login successful");
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized LinkedIn login attempt");
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during LinkedIn login");
                return StatusCode(500, new { error = "An error occurred during LinkedIn login", details = ex.Message });
            }
        }

        [HttpPost("social-login")]
        public async Task<ActionResult<AuthResponseDto>> SocialLogin([FromBody] SocialLoginDto dto)
        {
            try
            {
                var response = await _authService.SocialLoginAsync(dto);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized social login attempt");
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during social login");
                return StatusCode(500, new { error = "An error occurred during social login", details = ex.Message });
            }
        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult<PendingVerificationDto>> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var response = await _authService.InitiateForgotPasswordAsync(forgotPasswordDto, ipAddress);
            return Ok(response);
        }

        [HttpPost("verify-reset-code")]
        public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeDto verifyResetCodeDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var isValid = await _authService.VerifyResetCodeAsync(verifyResetCodeDto);
            return Ok(new { valid = isValid, message = "Code is valid" });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var success = await _authService.ResetPasswordAsync(resetPasswordDto);
            return Ok(new { success, message = "Password has been reset successfully" });
        }
    }
}