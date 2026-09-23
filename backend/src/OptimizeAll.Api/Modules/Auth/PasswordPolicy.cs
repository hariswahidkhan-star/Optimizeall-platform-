using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Auth;

/// <summary>
/// NIST 800-63B style policy: length over complexity, reject very common passwords and passwords
/// containing the email's local part.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password123", "password1234", "1234567890", "12345678910", "qwertyuiop", "qwerty12345", "iloveyou123",
        "admin12345", "welcome123", "letmein123", "passw0rd123", "abc1234567", "0987654321", "1q2w3e4r5t",
        "football123", "monkey12345", "sunshine123", "princess123", "password!1", "optimizeall",
    };

    public static void Validate(string password, string email)
    {
        var errors = new List<string>();
        if (password.Length < MinLength) errors.Add($"Use at least {MinLength} characters.");
        if (Common.Contains(password)) errors.Add("This password is too common.");
        if (password.Distinct().Count() < 4) errors.Add("Use a less repetitive password.");
        var local = email.Split('@')[0];
        if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            errors.Add("Don't include your email address in your password.");

        if (errors.Count > 0)
            throw new DomainException("auth.weak_password", "Choose a stronger password.", DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["password"] = errors.ToArray() });
    }
}
