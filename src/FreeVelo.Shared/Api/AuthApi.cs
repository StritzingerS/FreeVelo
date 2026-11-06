namespace FreeVelo.Shared.Api
{
    // REST messages between the app and the server (JSON, camelCase).
    // Public fields instead of properties, so Unity's JsonUtility can read and write them too.

    public static class ApiRoutes
    {
        public const string Register = "/api/v1/auth/register";
        public const string ResendVerification = "/api/v1/auth/verify/resend";
        public const string Login = "/api/v1/auth/login";
        public const string Refresh = "/api/v1/auth/refresh";
        public const string Logout = "/api/v1/auth/logout";
        public const string ForgotPassword = "/api/v1/auth/password/forgot";
        public const string Me = "/api/v1/me";
        public const string ChangePassword = "/api/v1/me/password";
        public const string DeleteAccount = "/api/v1/me/delete";
    }

    /// <summary>Machine-readable error codes in <see cref="ApiError.error"/>.</summary>
    public static class ApiErrors
    {
        public const string InvalidEmail = "invalid_email";
        public const string InvalidDisplayName = "invalid_display_name";
        public const string WeakPassword = "weak_password";
        public const string InvalidCredentials = "invalid_credentials";
        public const string EmailNotVerified = "email_not_verified";
        public const string InvalidToken = "invalid_token";
        public const string TooManyRequests = "too_many_requests";
    }

    public static class AccountRules
    {
        public const int MinPasswordLength = 10;
        public const int MaxPasswordLength = 256;
        public const int MinDisplayNameLength = 2;
        public const int MaxDisplayNameLength = 32;
    }

    public class ApiError
    {
        public string error = "";
    }

    public class RegisterRequest
    {
        public string email = "";
        public string displayName = "";
        public string password = "";

        /// <summary>"de" or "en": language of the emails.</summary>
        public string language = "en";
    }

    public class EmailRequest
    {
        public string email = "";
        public string language = "en";
    }

    public class LoginRequest
    {
        public string email = "";
        public string password = "";
    }

    public class RefreshRequest
    {
        public string refreshToken = "";
    }

    public class SessionResponse
    {
        public string accessToken = "";
        public string refreshToken = "";

        /// <summary>Access token lifetime in seconds.</summary>
        public int expiresIn;

        public AccountResponse account = new AccountResponse();
    }

    public class AccountResponse
    {
        public string id = "";
        public string email = "";
        public string displayName = "";
    }

    public class UpdateAccountRequest
    {
        public string displayName = "";
    }

    public class ChangePasswordRequest
    {
        public string currentPassword = "";
        public string newPassword = "";
    }

    public class DeleteAccountRequest
    {
        public string password = "";
    }
}
