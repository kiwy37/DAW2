using CareerConnect.Server.DTOs;
using CareerConnect.Server.Helpers;
using CareerConnect.Server.Models;
using CareerConnect.Server.Repositories;
using CareerConnect.Server.Services.Interfaces;
using Google.Apis.Auth;
using System.Text.Json;

namespace CareerConnect.Server.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IVerificationService _verificationService;
        private readonly JwtHelper _jwtHelper;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public AuthService(
            IUserRepository userRepository,
            IVerificationService verificationService,
            JwtHelper jwtHelper,
            IConfiguration configuration,
            ILogger<AuthService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _userRepository = userRepository;
            _verificationService = verificationService;
            _jwtHelper = jwtHelper;
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        // ==================== LOGIN ====================

        public async Task<PendingVerificationDto> InitiateLoginAsync(LoginDto loginDto, string? ipAddress = null)
        {
            var user = await _userRepository.GetByEmailAsync(loginDto.Email);

            if (user == null)
                throw new UnauthorizedAccessException("Incorrect email or password");

            if (user.Password == null)
                throw new UnauthorizedAccessException("This account is associated with a social provider. Please log in through that provider.");

            if (!BCrypt.Net.BCrypt.Verify(loginDto.Password, user.Password))
                throw new UnauthorizedAccessException("Incorrect email or password");

            await _verificationService.GenerateAndSendCodeAsync(loginDto.Email, "Login", ipAddress);

            _logger.LogInformation("Login initiated for {Email}", loginDto.Email);

            return new PendingVerificationDto
            {
                Email = loginDto.Email,
                Message = "A verification code has been sent to your email. Please enter the code to continue.",
                RequiresVerification = true
            };
        }

        public async Task<AuthResponseDto> CompleteLoginAsync(VerifyCodeDto verifyCodeDto)
        {
            var isValid = await _verificationService.ValidateCodeAsync(
                verifyCodeDto.Email,
                verifyCodeDto.Code,
                "Login");

            if (!isValid)
                throw new UnauthorizedAccessException("Invalid verification code");

            var user = await _userRepository.GetByEmailAsync(verifyCodeDto.Email);

            if (user == null)
                throw new UnauthorizedAccessException("User not found");

            var token = _jwtHelper.GenerateToken(user);

            _logger.LogInformation("Login completed for {Email}", verifyCodeDto.Email);

            return new AuthResponseDto
            {
                Token = token,
                User = MapToUserDto(user)
            };
        }

        // ==================== REGISTER ====================

        public async Task<PendingVerificationDto> InitiateRegisterAsync(CreateUserDto createUserDto, string? ipAddress = null)
        {
            if (await _userRepository.EmailExistsAsync(createUserDto.Email))
                throw new InvalidOperationException("Email is already registered");

            await _verificationService.GenerateAndSendCodeAsync(createUserDto.Email, "Register", ipAddress);

            _logger.LogInformation("Registration initiated for {Email}", createUserDto.Email);

            return new PendingVerificationDto
            {
                Email = createUserDto.Email,
                Message = "A verification code has been sent to your email. Please enter the code to complete your registration.",
                RequiresVerification = true
            };
        }

        public async Task<AuthResponseDto> FinalizeRegisterWithVerificationAsync(CreateUserWithCodeDto createUserDto)
        {
            var isValid = await _verificationService.ValidateCodeAsync(
                createUserDto.Email,
                createUserDto.Code,
                "Register");

            if (!isValid)
                throw new UnauthorizedAccessException("Invalid verification code");

            if (await _userRepository.EmailExistsAsync(createUserDto.Email))
                throw new InvalidOperationException("Email is already registered");

            var user = new User
            {
                Email = createUserDto.Email,
                Password = BCrypt.Net.BCrypt.HashPassword(createUserDto.Password),
                LastName = createUserDto.LastName,
                FirstName = createUserDto.FirstName,
                Phone = createUserDto.Phone,
                BirthDate = createUserDto.BirthDate,
                RoleId = createUserDto.RoleId,
                CreatedAt = DateTime.UtcNow
            };

            user = await _userRepository.CreateAsync(user);
            user = await _userRepository.GetByIdAsync(user.Id);

            var token = _jwtHelper.GenerateToken(user!);

            _logger.LogInformation("Registration completed for {Email}", createUserDto.Email);

            return new AuthResponseDto
            {
                Token = token,
                User = MapToUserDto(user!)
            };
        }

        // ==================== FORGOT PASSWORD ====================

        public async Task<PendingVerificationDto> InitiateForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto, string? ipAddress = null)
        {
            var user = await _userRepository.GetByEmailAsync(forgotPasswordDto.Email);

            if (user == null)
                throw new KeyNotFoundException("No account associated with this email");

            if (user.Password == null)
                throw new InvalidOperationException("This account uses social authentication and has no password. Please log in through the social provider.");

            await _verificationService.GenerateAndSendCodeAsync(forgotPasswordDto.Email, "ResetPassword", ipAddress);

            _logger.LogInformation("Password reset initiated for {Email}", forgotPasswordDto.Email);

            return new PendingVerificationDto
            {
                Email = forgotPasswordDto.Email,
                Message = "A verification code has been sent to your email for password reset.",
                RequiresVerification = true
            };
        }

        public async Task<bool> VerifyResetCodeAsync(VerifyResetCodeDto verifyResetCodeDto)
        {
            var isValid = await _verificationService.ValidateCodeAsync(
                verifyResetCodeDto.Email,
                verifyResetCodeDto.Code,
                "ResetPassword");

            if (!isValid)
                throw new UnauthorizedAccessException("Invalid or expired verification code");

            _logger.LogInformation("Reset code verified for {Email}", verifyResetCodeDto.Email);

            return true;
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
        {
            var isValid = await _verificationService.ValidateCodeAsync(
                resetPasswordDto.Email,
                resetPasswordDto.Code,
                "ResetPassword");

            if (!isValid)
                throw new UnauthorizedAccessException("Invalid or expired verification code");

            var user = await _userRepository.GetByEmailAsync(resetPasswordDto.Email);

            if (user == null)
                throw new KeyNotFoundException("User not found");

            user.Password = BCrypt.Net.BCrypt.HashPassword(resetPasswordDto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user);

            _logger.LogInformation("Password reset completed for {Email}", resetPasswordDto.Email);

            return true;
        }

        // ==================== GOOGLE LOGIN ====================

        public async Task<AuthResponseDto> GoogleLoginAsync(GoogleLoginDto googleLoginDto)
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _configuration["Authentication:Google:ClientId"] }
            };

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(googleLoginDto.IdToken, settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google token validation failed");
                throw new UnauthorizedAccessException("Invalid Google token");
            }

            var user = await GetOrCreateGoogleUser(payload);
            var token = _jwtHelper.GenerateToken(user);

            _logger.LogInformation("Google login completed for {Email}", payload.Email);

            return new AuthResponseDto
            {
                Token = token,
                User = MapToUserDto(user)
            };
        }

        private async Task<User> GetOrCreateGoogleUser(GoogleJsonWebSignature.Payload payload)
        {
            var user = await _userRepository.GetByGoogleIdAsync(payload.Subject);

            if (user == null)
            {
                user = await _userRepository.GetByEmailAsync(payload.Email);

                if (user != null)
                {
                    user.GoogleId = payload.Subject;
                    await _userRepository.UpdateAsync(user);
                }
                else
                {
                    user = new User
                    {
                        Email = payload.Email,
                        GoogleId = payload.Subject,
                        LastName = payload.FamilyName ?? "",
                        FirstName = payload.GivenName ?? "",
                        RoleId = 2,
                        BirthDate = DateTime.UtcNow.AddYears(-18),
                        CreatedAt = DateTime.UtcNow
                    };

                    user = await _userRepository.CreateAsync(user);
                    user = await _userRepository.GetByIdAsync(user.Id);
                }
            }

            return user!;
        }

        // ==================== LINKEDIN LOGIN ====================

        public async Task<AuthResponseDto> LinkedInLoginAsync(LinkedInLoginDto linkedInLoginDto)
        {
            try
            {
                _logger.LogInformation("LinkedIn login attempt started");

                var accessToken = await ExchangeLinkedInCodeForToken(linkedInLoginDto.Code);
                var profileData = await FetchLinkedInUserProfile(accessToken);
                var user = await GetOrCreateLinkedInUser(profileData);
                var token = _jwtHelper.GenerateToken(user);

                _logger.LogInformation("LinkedIn login completed for {Email}", user.Email);

                return new AuthResponseDto
                {
                    Token = token,
                    User = MapToUserDto(user)
                };
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LinkedIn login failed");
                throw new UnauthorizedAccessException("An unexpected error occurred during LinkedIn authentication.", ex);
            }
        }

        private async Task<string> ExchangeLinkedInCodeForToken(string code)
        {
            var clientId = _configuration["Authentication:LinkedIn:ClientId"];
            var clientSecret = _configuration["Authentication:LinkedIn:ClientSecret"];
            var redirectUri = _configuration["Authentication:LinkedIn:RedirectUri"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("LinkedIn credentials missing in configuration");
                throw new InvalidOperationException("LinkedIn authentication is not configured properly");
            }

            var httpClient = _httpClientFactory.CreateClient();

            var tokenRequestData = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "redirect_uri", redirectUri }
            };

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.linkedin.com/oauth/v2/accessToken")
            {
                Content = new FormUrlEncodedContent(tokenRequestData)
            };

            tokenRequest.Headers.Add("Accept", "application/json");

            var tokenResponse = await httpClient.SendAsync(tokenRequest);
            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();

            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("LinkedIn token exchange failed: {Content}", tokenContent);
                throw new UnauthorizedAccessException($"Failed to get access token from LinkedIn. Status: {tokenResponse.StatusCode}");
            }

            var tokenData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tokenContent);
            if (tokenData == null || !tokenData.ContainsKey("access_token"))
            {
                _logger.LogError("No access_token in LinkedIn response");
                throw new UnauthorizedAccessException("Invalid token response from LinkedIn");
            }

            return tokenData["access_token"].GetString()!;
        }

        private async Task<LinkedInUserInfo> FetchLinkedInUserProfile(string accessToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
            profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var profileResponse = await httpClient.SendAsync(profileRequest);
            var profileContent = await profileResponse.Content.ReadAsStringAsync();

            if (!profileResponse.IsSuccessStatusCode)
            {
                _logger.LogError("LinkedIn profile request failed ({StatusCode}): {Content}",
                    profileResponse.StatusCode, profileContent);
                throw new UnauthorizedAccessException($"Failed to get user profile from LinkedIn. Status: {profileResponse.StatusCode}");
            }

            var profileData = JsonSerializer.Deserialize<LinkedInUserInfo>(profileContent);
            if (profileData == null)
            {
                _logger.LogError("Failed to deserialize LinkedIn profile data");
                throw new UnauthorizedAccessException("Failed to parse LinkedIn profile data");
            }

            return profileData;
        }

        private async Task<User> GetOrCreateLinkedInUser(LinkedInUserInfo profileData)
        {
            var email = string.IsNullOrEmpty(profileData.Email)
                ? $"linkedin_{profileData.Sub}@careerconnect.temp"
                : profileData.Email;

            var user = await _userRepository.GetByLinkedInIdAsync(profileData.Sub);

            if (user == null)
            {
                user = await _userRepository.GetByEmailAsync(email);

                if (user != null)
                {
                    user.LinkedInId = profileData.Sub;
                    await _userRepository.UpdateAsync(user);
                }
                else
                {
                    user = new User
                    {
                        Email = email,
                        LinkedInId = profileData.Sub,
                        LastName = profileData.FamilyName ?? "User",
                        FirstName = profileData.GivenName ?? "LinkedIn",
                        RoleId = 2,
                        BirthDate = DateTime.UtcNow.AddYears(-18),
                        CreatedAt = DateTime.UtcNow
                    };

                    user = await _userRepository.CreateAsync(user);
                    user = await _userRepository.GetByIdAsync(user.Id);
                }
            }

            return user!;
        }

        // ==================== SOCIAL LOGIN ====================

        public async Task<AuthResponseDto> SocialLoginAsync(SocialLoginDto socialLoginDto)
        {
            User? user = socialLoginDto.Provider switch
            {
                "Facebook" => await HandleFacebookLoginAsync(socialLoginDto),
                "Twitter" => await HandleTwitterLoginAsync(socialLoginDto),
                "LinkedIn" => await HandleLinkedInLoginAsync(socialLoginDto),
                _ => throw new InvalidOperationException("Unknown provider")
            };

            var token = _jwtHelper.GenerateToken(user);

            _logger.LogInformation("{Provider} login completed for {Email}", socialLoginDto.Provider, user.Email);

            return new AuthResponseDto
            {
                Token = token,
                User = MapToUserDto(user)
            };
        }

        private async Task<User> HandleFacebookLoginAsync(SocialLoginDto dto)
        {
            try
            {
                if (!string.IsNullOrEmpty(dto.Email) &&
                    !string.IsNullOrEmpty(dto.FirstName) &&
                    !string.IsNullOrEmpty(dto.LastName) &&
                    !string.IsNullOrEmpty(dto.ProviderId))
                {
                    var user = await _userRepository.GetByFacebookIdAsync(dto.ProviderId);

                    if (user == null)
                    {
                        user = await _userRepository.GetByEmailAsync(dto.Email);

                        if (user != null)
                        {
                            user.FacebookId = dto.ProviderId;
                            await _userRepository.UpdateAsync(user);
                        }
                        else
                        {
                            user = new User
                            {
                                Email = dto.Email,
                                FacebookId = dto.ProviderId,
                                LastName = dto.LastName,
                                FirstName = dto.FirstName,
                                RoleId = 2,
                                BirthDate = DateTime.UtcNow.AddYears(-18),
                                CreatedAt = DateTime.UtcNow
                            };

                            user = await _userRepository.CreateAsync(user);
                            user = await _userRepository.GetByIdAsync(user.Id);
                        }
                    }

                    return user!;
                }

                var httpClient = _httpClientFactory.CreateClient();
                var response = await httpClient.GetAsync(
                    $"https://graph.facebook.com/me?access_token={dto.AccessToken}&fields=id,email,first_name,last_name"
                );

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Facebook API error: {Error}", errorContent);
                    throw new UnauthorizedAccessException("Invalid Facebook token");
                }

                var userData = JsonSerializer.Deserialize<FacebookUserData>(
                    await response.Content.ReadAsStreamAsync()
                );

                if (userData == null || string.IsNullOrEmpty(userData.Email))
                {
                    throw new UnauthorizedAccessException("Could not retrieve data from Facebook");
                }

                var existingUser = await _userRepository.GetByFacebookIdAsync(userData.Id);

                if (existingUser == null)
                {
                    existingUser = await _userRepository.GetByEmailAsync(userData.Email);

                    if (existingUser != null)
                    {
                        existingUser.FacebookId = userData.Id;
                        await _userRepository.UpdateAsync(existingUser);
                    }
                    else
                    {
                        existingUser = new User
                        {
                            Email = userData.Email,
                            FacebookId = userData.Id,
                            LastName = userData.Last_Name,
                            FirstName = userData.First_Name,
                            RoleId = 2,
                            BirthDate = DateTime.UtcNow.AddYears(-18),
                            CreatedAt = DateTime.UtcNow
                        };

                        existingUser = await _userRepository.CreateAsync(existingUser);
                        existingUser = await _userRepository.GetByIdAsync(existingUser.Id);
                    }
                }

                return existingUser!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleFacebookLoginAsync");
                throw;
            }
        }

        private async Task<User> HandleTwitterLoginAsync(SocialLoginDto dto)
        {
            var user = await _userRepository.GetByEmailAsync(dto.Email!);

            if (user == null)
            {
                user = new User
                {
                    Email = dto.Email!,
                    TwitterId = dto.ProviderId,
                    LastName = dto.LastName ?? "",
                    FirstName = dto.FirstName ?? "",
                    RoleId = 2,
                    BirthDate = DateTime.UtcNow.AddYears(-18),
                    CreatedAt = DateTime.UtcNow
                };

                user = await _userRepository.CreateAsync(user);
            }
            else if (string.IsNullOrEmpty(user.TwitterId))
            {
                user.TwitterId = dto.ProviderId;
                await _userRepository.UpdateAsync(user);
            }

            return user;
        }

        private async Task<User> HandleLinkedInLoginAsync(SocialLoginDto dto)
        {
            try
            {
                if (!string.IsNullOrEmpty(dto.Email) &&
                    !string.IsNullOrEmpty(dto.FirstName) &&
                    !string.IsNullOrEmpty(dto.LastName) &&
                    !string.IsNullOrEmpty(dto.ProviderId))
                {
                    var user = await _userRepository.GetByLinkedInIdAsync(dto.ProviderId);

                    if (user == null)
                    {
                        user = await _userRepository.GetByEmailAsync(dto.Email);

                        if (user != null)
                        {
                            user.LinkedInId = dto.ProviderId;
                            await _userRepository.UpdateAsync(user);
                        }
                        else
                        {
                            user = new User
                            {
                                Email = dto.Email,
                                LinkedInId = dto.ProviderId,
                                LastName = dto.LastName ?? "User",
                                FirstName = dto.FirstName ?? "LinkedIn",
                                RoleId = 2,
                                BirthDate = DateTime.UtcNow.AddYears(-18),
                                CreatedAt = DateTime.UtcNow
                            };

                            user = await _userRepository.CreateAsync(user);
                            user = await _userRepository.GetByIdAsync(user.Id);
                        }
                    }

                    return user!;
                }

                throw new InvalidOperationException("LinkedIn user data is required");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleLinkedInLoginAsync");
                throw new UnauthorizedAccessException("LinkedIn authentication failed: " + ex.Message);
            }
        }

        // ==================== RESEND CODE ====================

        public async Task ResendVerificationCodeAsync(ResendCodeDto resendCodeDto, string? ipAddress = null)
        {
            await _verificationService.GenerateAndSendCodeAsync(
                resendCodeDto.Email,
                resendCodeDto.VerificationType,
                ipAddress);

            _logger.LogInformation("Verification code resent for {Email}", resendCodeDto.Email);
        }

        // ==================== HELPER METHODS ====================

        private UserDto MapToUserDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                LastName = user.LastName,
                FirstName = user.FirstName,
                Phone = user.Phone,
                BirthDate = user.BirthDate,
                RoleName = user.Role.Name,
                CreatedAt = user.CreatedAt
            };
        }
    }
}