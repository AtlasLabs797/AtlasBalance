using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AtlasBalance.API.Services;

public static class TotpService
{
    private const int SecretBytes = 20;
    private const int Digits = 6;
    private const int PeriodSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret()
    {
        return Base32Encode(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    public static string BuildOtpAuthUri(string issuer, string account, string secret)
    {
        var escapedIssuer = Uri.EscapeDataString(issuer);
        var escapedAccount = Uri.EscapeDataString(account);
        return $"otpauth://totp/{escapedIssuer}:{escapedAccount}?secret={secret}&issuer={escapedIssuer}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    public static bool TryValidateCode(string secret, string code, DateTime utcNow, out long matchedStep)
    {
        matchedStep = 0;
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode.Length != Digits || normalizedCode.Any(ch => ch < '0' || ch > '9'))
        {
            return false;
        }

        byte[] secretBytes;
        try
        {
            secretBytes = Base32Decode(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var currentStep = ToTimeStep(utcNow);
        for (var offset = -1; offset <= 1; offset++)
        {
            var step = currentStep + offset;
            if (step < 0)
            {
                continue;
            }

            var expected = GenerateCode(secretBytes, step);
            if (FixedTimeEquals(expected, normalizedCode))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    public static string GenerateCode(string secret, DateTime utcNow)
    {
        return GenerateCode(Base32Decode(secret), ToTimeStep(utcNow));
    }

    private static long ToTimeStep(DateTime utcNow)
    {
        return new DateTimeOffset(utcNow.ToUniversalTime()).ToUnixTimeSeconds() / PeriodSeconds;
    }

    private static string GenerateCode(byte[] secret, long step)
    {
        var counter = IPAddress.HostToNetworkOrder(step);
        var counterBytes = BitConverter.GetBytes(counter);
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);
        var otp = binary % 1_000_000;
        return otp.ToString("D6");
    }

    private static string NormalizeCode(string code)
    {
        return new string(code.Where(char.IsDigit).ToArray());
    }

    private static string Base32Encode(byte[] data)
    {
        var output = new StringBuilder();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return output.ToString();
    }

    private static byte[] Base32Decode(string secret)
    {
        var normalized = secret
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .TrimEnd('=')
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new FormatException("Empty TOTP secret.");
        }

        var bytes = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var ch in normalized)
        {
            var value = Base32Alphabet.IndexOf(ch, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Invalid TOTP secret.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return bytes.ToArray();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
