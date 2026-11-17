using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Application.Common.Interfaces.External;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HospitalCore.Infrastructure.ExternalServices;

public class VneidOptions
{
    public const string SectionName = "Vneid";

    public string Issuer { get; set; } = "http://localhost:5001";
    public string? InternalBaseUrl { get; set; }
    public string ClientId { get; set; } = "neuroscan-client";
    public string ClientSecret { get; set; } = "neuroscan-secret-key-123456";
    public int JwksCacheDurationMinutes { get; set; } = 15;
}

public class VneidAuthClient : IVneidAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly VneidOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VneidAuthClient> _logger;

    public VneidAuthClient(
        HttpClient httpClient,
        IOptions<VneidOptions> options,
        IMemoryCache cache,
        ILogger<VneidAuthClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<VneidTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken ct = default)
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code_verifier"] = codeVerifier
        };

        var response = await _httpClient.PostAsync("/oauth/token", new FormUrlEncodedContent(formData), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("VNeID token exchange failed with status {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new PermissionDeniedException("VNeID authorization code exchange failed.", "OIDC_TOKEN_EXCHANGE_FAILED");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<VneidTokenResponse>(cancellationToken: ct);
        if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new PermissionDeniedException("Empty token response received from VNeID.", "OIDC_TOKEN_EXCHANGE_FAILED");
        }

        return tokenResponse;
    }

    public async Task<VneidClaimsDto> ValidateIdTokenAsync(
        string idToken,
        string expectedNonce,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new PermissionDeniedException("Missing VNeID ID token.", "INVALID_ID_TOKEN");
        }

        if (string.IsNullOrWhiteSpace(expectedNonce))
        {
            throw new PermissionDeniedException("Missing expected OIDC nonce.", "INVALID_OIDC_NONCE");
        }

        var jwks = await GetJwksAsync(forceRefresh: false, ct);
        ClaimsPrincipal principal;
        try
        {
            principal = ValidateWithJwks(idToken, jwks);
        }
        catch (SecurityTokenException ex) when (IsKeyResolutionOrSignatureFailure(ex))
        {
            _logger.LogInformation("Refreshing VNeID JWKS after initial ID token key/signature verification failure.");
            jwks = await GetJwksAsync(forceRefresh: true, ct);
            try
            {
                principal = ValidateWithJwks(idToken, jwks);
            }
            catch (SecurityTokenException retryEx)
            {
                _logger.LogWarning(retryEx, "VNeID ID token signature verification failed after JWKS refresh.");
                throw new PermissionDeniedException("Invalid VNeID ID token signature.", "INVALID_ID_TOKEN");
            }
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            _logger.LogWarning(ex, "VNeID ID token validation failed.");
            throw new PermissionDeniedException("Invalid or expired VNeID ID token.", "INVALID_ID_TOKEN");
        }

        var nonce = principal.FindFirst("nonce")?.Value;
        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new PermissionDeniedException("VNeID ID token nonce mismatch.", "INVALID_OIDC_NONCE");
        }

        var tokenUse = principal.FindFirst("token_use")?.Value;
        if (!string.Equals(tokenUse, "id_token", StringComparison.Ordinal))
        {
            throw new PermissionDeniedException("VNeID token_use claim must equal id_token.", "INVALID_ID_TOKEN");
        }

        var cccd = principal.FindFirst("cccd")?.Value ?? principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(cccd))
        {
            throw new PermissionDeniedException("VNeID ID token missing citizen identifier.", "INVALID_ID_TOKEN");
        }

        var roles = principal.FindAll("roles")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        return new VneidClaimsDto
        {
            Cccd = cccd.Trim(),
            Name = principal.FindFirst("name")?.Value?.Trim() ?? "Unknown",
            Cchn = principal.FindFirst("cchn")?.Value?.Trim(),
            FacilityId = principal.FindFirst("facility_id")?.Value?.Trim(),
            Roles = roles,
            BhytNumber = principal.FindFirst("bhyt_number")?.Value?.Trim(),
            Birthdate = principal.FindFirst("birthdate")?.Value?.Trim(),
            Gender = principal.FindFirst("gender")?.Value?.Trim()
        };
    }

    private ClaimsPrincipal ValidateWithJwks(string idToken, JsonWebKeySet jwks)
    {
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer.TrimEnd('/'),
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwks.GetSigningKeys()
        };

        return handler.ValidateToken(idToken, validationParameters, out _);
    }

    private async Task<JsonWebKeySet> GetJwksAsync(bool forceRefresh, CancellationToken ct)
    {
        var cacheKey = $"vneid:jwks:{_options.Issuer.TrimEnd('/')}:{_options.ClientId}";
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out JsonWebKeySet? cached) && cached != null)
        {
            return cached;
        }

        var response = await _httpClient.GetAsync("/.well-known/jwks.json", ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new PermissionDeniedException("Unable to retrieve VNeID signing keys.", "OIDC_JWKS_UNAVAILABLE");
        }

        var jwksJson = await response.Content.ReadAsStringAsync(ct);
        var jwks = JsonWebKeySet.Create(jwksJson);
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _options.JwksCacheDurationMinutes));
        _cache.Set(cacheKey, jwks, ttl);
        return jwks;
    }

    private static bool IsKeyResolutionOrSignatureFailure(SecurityTokenException ex)
    {
        return ex is SecurityTokenInvalidSignatureException
            or SecurityTokenSignatureKeyNotFoundException
            or SecurityTokenInvalidSigningKeyException;
    }
}
