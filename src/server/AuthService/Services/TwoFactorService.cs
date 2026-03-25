using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OtpNet;
using QRCoder;

namespace AuthService.Services
{
    /// <summary>
    /// Two-Factor Authentication service using TOTP (Time-based One-Time Password)
    /// </summary>
    public class TwoFactorService
    {
        private readonly ILogger<TwoFactorService> _logger;
        private const int SecretKeyLength = 20; // 160 bits
        private const int Digits = 6;
        private const int VerificationWindow = 1; // Allow 1 step before/after

        public TwoFactorService(ILogger<TwoFactorService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Generates a new TOTP secret key for a user
        /// </summary>
        public string GenerateSecretKey()
        {
            var key = KeyGeneration.GenerateRandomKey(SecretKeyLength);
            return Base32Encoding.ToString(key);
        }

        /// <summary>
        /// Generates a QR code URI for authenticator apps
        /// </summary>
        public string GenerateQrCodeUri(string email, string secretKey, string issuer = "LocalTelegram")
        {
            return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}" +
                   $"?secret={secretKey}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}";
        }

        /// <summary>
        /// Generates a QR code image as base64 string
        /// </summary>
        public string GenerateQrCodeImage(string qrCodeUri)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrCodeUri, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(20);
            return Convert.ToBase64String(qrCodeBytes);
        }

        /// <summary>
        /// Verifies a TOTP code against the secret key
        /// </summary>
        public bool VerifyCode(string secretKey, string code)
        {
            if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(code))
            {
                return false;
            }

            if (code.Length != Digits || !long.TryParse(code, out _))
            {
                return false;
            }

            try
            {
                var key = Base32Encoding.ToBytes(secretKey);
                var totp = new Totp(key, step: 30, totpSize: Digits);
                
                var verificationWindow = new VerificationWindow(VerificationWindow);
                return totp.VerifyTotp(code, out _, verificationWindow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify TOTP code");
                return false;
            }
        }

        /// <summary>
        /// Generates recovery codes for backup access
        /// </summary>
        public List<string> GenerateRecoveryCodes(int count = 8)
        {
            var codes = new List<string>();
            using var rng = RandomNumberGenerator.Create();

            for (var i = 0; i < count; i++)
            {
                var bytes = new byte[4];
                rng.GetBytes(bytes);
                var code = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                // Format as xxxx-xxxx
                codes.Add($"{code.Substring(0, 4)}-{code.Substring(4, 4)}");
            }

            return codes;
        }

        /// <summary>
        /// Hashes a recovery code for storage
        /// </summary>
        public string HashRecoveryCode(string code)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(code));
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Verifies a recovery code against a hash
        /// </summary>
        public bool VerifyRecoveryCode(string code, string hash)
        {
            var computedHash = HashRecoveryCode(code);
            return computedHash == hash;
        }

        /// <summary>
        /// Gets the current TOTP code (for testing purposes)
        /// </summary>
        public string GetCurrentCode(string secretKey)
        {
            var key = Base32Encoding.ToBytes(secretKey);
            var totp = new Totp(key, step: 30, totpSize: Digits);
            return totp.ComputeTotp();
        }

        /// <summary>
        /// Gets the remaining seconds until the current code expires
        /// </summary>
        public int GetRemainingSeconds()
        {
            return 30 - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30);
        }
    }

    /// <summary>
    /// Two-factor authentication setup result
    /// </summary>
    public class TwoFactorSetupResult
    {
        public string SecretKey { get; set; } = string.Empty;
        public string QrCodeUri { get; set; } = string.Empty;
        public string QrCodeImageBase64 { get; set; } = string.Empty;
        public List<string> RecoveryCodes { get; set; } = new();
    }

    /// <summary>
    /// Two-factor authentication status
    /// </summary>
    public class TwoFactorStatus
    {
        public bool IsEnabled { get; set; }
        public DateTime? EnabledAt { get; set; }
        public int RemainingRecoveryCodes { get; set; }
    }
}
