using System.Security.Cryptography;
using System.Text;
using AuthService.Data;
using AuthService.Models;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Services;

public interface IAuthService
{
    Task<LoginResponse> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent);
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent);
    Task<LoginResponse> RefreshTokenAsync(string refreshToken);
    Task<bool> LogoutAsync(Guid sessionId);
    Task<bool> LogoutAllAsync(long userId);
    Task<User?> GetUserByIdAsync(long userId);
    Task<User?> GetUserByUsernameAsync(string username);
    Task<User?> UpdateUserAsync(long userId, UpdateUserRequest request);
    Task<bool> ChangePasswordAsync(long userId, ChangePasswordRequest request);
}

public class AuthService : IAuthService
{
    private readonly AuthDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IRedisService _redisService;
    private readonly ILogger<AuthService> _logger;
    private readonly IConfiguration _configuration;

    public AuthService(
        AuthDbContext context,
        ITokenService tokenService,
        IRedisService redisService,
        ILogger<AuthService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _tokenService = tokenService;
        _redisService = redisService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<LoginResponse> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent)
    {
        try
        {
            // Check if username already exists
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return new LoginResponse { Success = false, Error = "Username already exists" };
            }

            // Check if email already exists (if provided)
            if (!string.IsNullOrEmpty(request.Email) && 
                await _context.Users.AnyAsync(u => u.Email == request.Email))
            {
                return new LoginResponse { Success = false, Error = "Email already exists" };
            }

            // Check if phone already exists (if provided)
            if (!string.IsNullOrEmpty(request.Phone) && 
                await _context.Users.AnyAsync(u => u.Phone == request.Phone))
            {
                return new LoginResponse { Success = false, Error = "Phone already exists" };
            }

            // Create user
            var user = new User
            {
                Username = request.Username.ToLowerInvariant(),
                Email = request.Email?.ToLowerInvariant(),
                Phone = request.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FirstName = request.FirstName,
                LastName = request.LastName,
                Status = UserStatus.Online,
                LastSeenAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User registered: {Username}", user.Username);

            // Create session
            var (accessToken, refreshToken, expiresAt) = await CreateSessionAsync(
                user.Id, ipAddress, userAgent);

            return new LoginResponse
            {
                Success = true,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                User = UserDto.FromUser(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for {Username}", request.Username);
            return new LoginResponse { Success = false, Error = "Registration failed" };
        }
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username.ToLowerInvariant());

            if (user == null)
            {
                _logger.LogWarning("Login attempt with non-existent username: {Username}", request.Username);
                return new LoginResponse { Success = false, Error = "Invalid credentials" };
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login attempt for inactive user: {Username}", request.Username);
                return new LoginResponse { Success = false, Error = "Account is disabled" };
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Invalid password for user: {Username}", request.Username);
                return new LoginResponse { Success = false, Error = "Invalid credentials" };
            }

            // Update user status
            user.Status = UserStatus.Online;
            user.LastSeenAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Create session
            var (accessToken, refreshToken, expiresAt) = await CreateSessionAsync(
                user.Id, ipAddress, userAgent);

            // Register device if provided
            if (!string.IsNullOrEmpty(request.DeviceType))
            {
                await RegisterDeviceAsync(user.Id, request.DeviceInfo, request.DeviceType);
            }

            _logger.LogInformation("User logged in: {Username}", user.Username);

            return new LoginResponse
            {
                Success = true,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                User = UserDto.FromUser(user)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {Username}", request.Username);
            return new LoginResponse { Success = false, Error = "Login failed" };
        }
    }

    public async Task<LoginResponse> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var refreshTokenHash = HashToken(refreshToken);
            var session = await _context.Sessions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.RefreshTokenHash == refreshTokenHash);

            if (session == null || session.IsRevoked)
            {
                return new LoginResponse { Success = false, Error = "Invalid refresh token" };
            }

            if (session.RefreshExpiresAt < DateTime.UtcNow)
            {
                return new LoginResponse { Success = false, Error = "Refresh token expired" };
            }

            // Revoke old session
            session.IsRevoked = true;

            // Create new session
            var (accessToken, newRefreshToken, expiresAt) = await CreateSessionAsync(
                session.User.Id, session.IpAddress, session.UserAgent);

            await _context.SaveChangesAsync();

            return new LoginResponse
            {
                Success = true,
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpiresAt = expiresAt,
                User = UserDto.FromUser(session.User)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return new LoginResponse { Success = false, Error = "Token refresh failed" };
        }
    }

    public async Task<bool> LogoutAsync(Guid sessionId)
    {
        try
        {
            var session = await _context.Sessions.FindAsync(sessionId);
            if (session == null) return false;

            session.IsRevoked = true;
            await _context.SaveChangesAsync();

            // Invalidate cache
            await _redisService.InvalidateSessionAsync(sessionId);

            _logger.LogInformation("Session logged out: {SessionId}", sessionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return false;
        }
    }

    public async Task<bool> LogoutAllAsync(long userId)
    {
        try
        {
            var sessions = await _context.Sessions
                .Where(s => s.UserId == userId && !s.IsRevoked)
                .ToListAsync();

            foreach (var session in sessions)
            {
                session.IsRevoked = true;
                await _redisService.InvalidateSessionAsync(session.Id);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("All sessions logged out for user: {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout all");
            return false;
        }
    }

    public async Task<User?> GetUserByIdAsync(long userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username.ToLowerInvariant());
    }

    public async Task<User?> UpdateUserAsync(long userId, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return null;

        if (request.FirstName != null) user.FirstName = request.FirstName;
        if (request.LastName != null) user.LastName = request.LastName;
        if (request.Bio != null) user.Bio = request.Bio;
        
        if (request.Email != null)
        {
            if (await _context.Users.AnyAsync(u => u.Email == request.Email && u.Id != userId))
            {
                return null; // Email already in use
            }
            user.Email = request.Email;
        }

        if (request.Phone != null)
        {
            if (await _context.Users.AnyAsync(u => u.Phone == request.Phone && u.Id != userId))
            {
                return null; // Phone already in use
            }
            user.Phone = request.Phone;
        }

        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<bool> ChangePasswordAsync(long userId, ChangePasswordRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return false;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();

        // Invalidate all sessions except current
        // This forces re-login on other devices

        return true;
    }

    private async Task<(string accessToken, string refreshToken, DateTime expiresAt)> CreateSessionAsync(
        long userId, string? ipAddress, string? userAgent)
    {
        var expirationHours = _configuration.GetValue<int>("Jwt:ExpirationHours", 24);
        var expiresAt = DateTime.UtcNow.AddHours(expirationHours);
        var refreshExpiresAt = DateTime.UtcNow.AddDays(30);

        var accessToken = _tokenService.GenerateAccessToken(userId);
        var refreshToken = GenerateRefreshToken();

        var session = new Session
        {
            UserId = userId,
            TokenHash = HashToken(accessToken),
            RefreshTokenHash = HashToken(refreshToken),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = expiresAt,
            RefreshExpiresAt = refreshExpiresAt
        };

        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        // Cache session in Redis
        await _redisService.CacheSessionAsync(session.Id, userId, expiresAt);

        return (accessToken, refreshToken, expiresAt);
    }

    private async Task RegisterDeviceAsync(long userId, string? deviceInfo, string deviceType)
    {
        var device = new UserDevice
        {
            UserId = userId,
            DeviceInfo = deviceInfo,
            DeviceType = deviceType,
            LastUsedAt = DateTime.UtcNow
        };

        _context.UserDevices.Add(device);
        await _context.SaveChangesAsync();
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
