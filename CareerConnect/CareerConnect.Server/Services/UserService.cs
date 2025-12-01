using CareerConnect.Server.Models;
using CareerConnect.Server.Repositories;
using CareerConnect.Server.Services.Interfaces;

namespace CareerConnect.Server.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<IEnumerable<UserDto>> GetAllUsersAsync()
        {
            var users = await _userRepository.GetAllAsync();
            return users.Select(MapToUserDto);
        }

        public async Task<UserDto> GetUserByIdAsync(int id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                throw new KeyNotFoundException($"User with ID {id} was not found");

            return MapToUserDto(user);
        }

        public async Task<UserDto> CreateUserAsync(CreateUserDto createUserDto)
        {
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

            return MapToUserDto(user!);
        }

        public async Task<UserDto> UpdateUserAsync(int id, UpdateUserDto updateUserDto)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                throw new KeyNotFoundException($"User with ID {id} was not found");

            if (updateUserDto.Email != null && updateUserDto.Email != user.Email)
            {
                if (await _userRepository.EmailExistsAsync(updateUserDto.Email))
                    throw new InvalidOperationException("Email is already in use");
                user.Email = updateUserDto.Email;
            }

            if (updateUserDto.LastName != null) user.LastName = updateUserDto.LastName;
            if (updateUserDto.FirstName != null) user.FirstName = updateUserDto.FirstName;
            if (updateUserDto.Phone != null) user.Phone = updateUserDto.Phone;
            if (updateUserDto.BirthDate.HasValue) user.BirthDate = updateUserDto.BirthDate.Value;
            if (updateUserDto.RoleId.HasValue) user.RoleId = updateUserDto.RoleId.Value;

            user = await _userRepository.UpdateAsync(user);
            user = await _userRepository.GetByIdAsync(user.Id);

            return MapToUserDto(user!);
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            return await _userRepository.DeleteAsync(id);
        }

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