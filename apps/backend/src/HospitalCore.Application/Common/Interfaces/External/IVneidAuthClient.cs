using System.Text.Json.Serialization;

namespace HospitalCore.Application.Common.Interfaces.External;

public record VneidTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
}

public record VneidClaimsDto
{
    public string Cccd { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Cchn { get; init; }
    public string? FacilityId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public string? BhytNumber { get; init; }
    public string? Birthdate { get; init; }
    public string? Gender { get; init; }
}

public interface IVneidAuthClient
{
    Task<VneidTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken ct = default);

    Task<VneidClaimsDto> ValidateIdTokenAsync(
        string idToken,
        string expectedNonce,
        CancellationToken ct = default);
}
