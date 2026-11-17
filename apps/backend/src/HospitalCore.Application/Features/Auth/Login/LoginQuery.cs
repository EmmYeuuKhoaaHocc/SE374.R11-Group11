using System.Security.Cryptography;
using System.Text;
using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Common.Mediator;

namespace HospitalCore.Application.Features.Auth.Login;

public record LoginQuery(string? ReturnUrl = null, string? RedirectUri = null, string? BindingToken = null) : IRequest<LoginResponseDto>;

public record LoginResponseDto(string RedirectUrl, string State);

public class LoginQueryHandler : IRequestHandler<LoginQuery, LoginResponseDto>
{
    private const string VneidAuthEndpoint = "/oauth/authorize";
    private readonly IOidcTransactionStore _transactionStore;
    private readonly IOidcSettings _oidcSettings;

    public LoginQueryHandler(IOidcTransactionStore transactionStore, IOidcSettings oidcSettings)
    {
        _transactionStore = transactionStore;
        _oidcSettings = oidcSettings;
    }

    public async Task<LoginResponseDto> Handle(LoginQuery request, CancellationToken cancellationToken)
    {
        var normalizedReturnUrl = ValidateAndNormalizeReturnUrl(request.ReturnUrl);
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");

        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var codeVerifier = Base64UrlEncode(verifierBytes);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(challengeBytes);

        var issuer = _oidcSettings.Issuer.TrimEnd('/');
        var clientId = _oidcSettings.ClientId;
        var redirectUri = string.IsNullOrWhiteSpace(request.RedirectUri)
            ? $"{_oidcSettings.FrontendUrl.TrimEnd('/')}/auth/callback"
            : request.RedirectUri;

        var bindingHash = ComputeBindingHash(request.BindingToken);

        await _transactionStore.SaveAsync(
            state,
            new OidcTransaction
            {
                Nonce = nonce,
                CodeVerifier = codeVerifier,
                RedirectUri = redirectUri,
                ReturnUrl = normalizedReturnUrl,
                BindingHash = bindingHash
            },
            TimeSpan.FromMinutes(10),
            cancellationToken);

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile cccd bhyt",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var queryString = string.Join("&", queryParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var redirectUrl = $"{issuer}{VneidAuthEndpoint}?{queryString}";

        return new LoginResponseDto(redirectUrl, state);
    }

    public static string ComputeBindingHash(string? bindingToken)
    {
        if (string.IsNullOrWhiteSpace(bindingToken))
        {
            return string.Empty;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(bindingToken));
        return Convert.ToHexString(hash);
    }

    private static string ValidateAndNormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var trimmed = returnUrl.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//") || trimmed.StartsWith("/\\"))
        {
            throw new ApplicationValidationException("ReturnUrl", "ReturnUrl must be a local relative path starting with '/'.");
        }

        return trimmed;
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
