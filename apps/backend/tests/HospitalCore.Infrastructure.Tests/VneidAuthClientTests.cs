using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Infrastructure.ExternalServices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HospitalCore.Infrastructure.Tests;

public class VneidAuthClientTests
{
    private const string Issuer = "http://mock-vneid.local";
    private const string ClientId = "neuroscan-client";

    [Fact]
    public async Task ValidateIdTokenAsync_ValidRs256Token_ReturnsClaims()
    {
        using var rsa = RSA.Create(2048);
        var jwk = CreatePublicJwk(rsa, "kid-1");
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new { keys = new[] { jwk } }));
        var client = CreateClient(handler);

        var token = SignIdToken(rsa, "kid-1", "nonce-123", "id_token");
        var claims = await client.ValidateIdTokenAsync(token, "nonce-123");

        Assert.Equal("079095001122", claims.Cccd);
        Assert.Equal("Nguyen Van A", claims.Name);
        Assert.Equal("BYT-CCHN-001", claims.Cchn);
        Assert.Equal("79024", claims.FacilityId);
        Assert.Equal(1, handler.JwksFetchCount);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_KeyRotated_RefreshesJwksOnceAndSucceeds()
    {
        using var oldRsa = RSA.Create(2048);
        using var newRsa = RSA.Create(2048);
        var activeJwk = CreatePublicJwk(oldRsa, "kid-old");

        var handler = new StubHttpMessageHandler(_ => JsonResponse(new { keys = new[] { activeJwk } }));
        var client = CreateClient(handler);

        var firstToken = SignIdToken(oldRsa, "kid-old", "nonce-1", "id_token");
        await client.ValidateIdTokenAsync(firstToken, "nonce-1");

        activeJwk = CreatePublicJwk(newRsa, "kid-new");
        var rotatedToken = SignIdToken(newRsa, "kid-new", "nonce-2", "id_token");
        var claims = await client.ValidateIdTokenAsync(rotatedToken, "nonce-2");

        Assert.Equal("079095001122", claims.Cccd);
        Assert.Equal(2, handler.JwksFetchCount);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_NonceMismatch_ThrowsPermissionDenied()
    {
        using var rsa = RSA.Create(2048);
        var jwk = CreatePublicJwk(rsa, "kid-1");
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new { keys = new[] { jwk } }));
        var client = CreateClient(handler);

        var token = SignIdToken(rsa, "kid-1", "expected-nonce", "id_token");
        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => client.ValidateIdTokenAsync(token, "different-nonce"));

        Assert.Equal("INVALID_OIDC_NONCE", ex.Code);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_AccessTokenRejectedAsIdToken()
    {
        using var rsa = RSA.Create(2048);
        var jwk = CreatePublicJwk(rsa, "kid-1");
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new { keys = new[] { jwk } }));
        var client = CreateClient(handler);

        var accessToken = SignIdToken(rsa, "kid-1", "nonce-1", "access_token");
        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => client.ValidateIdTokenAsync(accessToken, "nonce-1"));

        Assert.Equal("INVALID_ID_TOKEN", ex.Code);
    }

    [Fact]
    public async Task ExchangeCodeAsync_PostsCodeVerifierAndReturnsTokens()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new
            {
                access_token = "at-1",
                id_token = "idt-1",
                token_type = "Bearer",
                expires_in = 3600
            });
        });

        var client = CreateClient(handler);
        var response = await client.ExchangeCodeAsync("code-1", "http://localhost:3000/auth/callback", "verifier-xyz");

        Assert.Equal("at-1", response.AccessToken);
        Assert.Equal("idt-1", response.IdToken);
        Assert.Contains("code_verifier=verifier-xyz", capturedBody);
        Assert.Contains("grant_type=authorization_code", capturedBody);
    }

    private static VneidAuthClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(Issuer)
        };
        var options = Options.Create(new VneidOptions
        {
            Issuer = Issuer,
            ClientId = ClientId,
            ClientSecret = "secret",
            JwksCacheDurationMinutes = 15
        });

        return new VneidAuthClient(
            httpClient,
            options,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<VneidAuthClient>.Instance);
    }

    private static object CreatePublicJwk(RSA rsa, string kid)
    {
        var key = new RsaSecurityKey(rsa) { KeyId = kid };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        return new
        {
            kty = jwk.Kty,
            use = "sig",
            kid,
            alg = SecurityAlgorithms.RsaSha256,
            n = jwk.N,
            e = jwk.E
        };
    }

    private static string SignIdToken(RSA rsa, string kid, string nonce, string tokenUse)
    {
        var securityKey = new RsaSecurityKey(rsa) { KeyId = kid };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim("sub", "079095001122"),
            new Claim("cccd", "079095001122"),
            new Claim("name", "Nguyen Van A"),
            new Claim("cchn", "BYT-CCHN-001"),
            new Claim("facility_id", "79024"),
            new Claim("roles", "Doctor"),
            new Claim("nonce", nonce),
            new Claim("token_use", tokenUse)
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: ClientId,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static HttpResponseMessage JsonResponse(object payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int JwksFetchCount { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/.well-known/jwks.json", StringComparison.Ordinal) == true)
            {
                JwksFetchCount++;
            }

            return Task.FromResult(_responder(request));
        }
    }
}
