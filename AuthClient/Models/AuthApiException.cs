namespace YTMT_STUDIO.AuthClient.Models;

public sealed class AuthApiException : Exception
{
    public int StatusCode { get; }
    public bool IsUnauthorized => StatusCode is 401 or 403;

    public AuthApiException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}