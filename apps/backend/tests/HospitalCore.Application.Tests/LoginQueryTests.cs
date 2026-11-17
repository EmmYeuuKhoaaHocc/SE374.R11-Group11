using System.Security.Cryptography;
using System.Text;
using System.Web;
using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Features.Auth.Login;
using Xunit;

namespace HospitalCore.Application.Tests;

public class LoginQueryTests
{
    [Fact]
    public async Task Handle_ValidLocalReturnUrl_PersistsPkceTransactionAndBuildsAuthorizeUrl()
    {
        var store = new InMemoryOidcTransactionStore();
        var settings = new FakeOidcSettings(
            "https://vneid.example.local",
            "neuroscan-test",
            "secret",
            "http://localhost:3000");

        var handler = new LoginQueryHandler(store, settings);

        var result = await handler.Handle(
            new LoginQuery("/cases/123?tab=mri", null, "browser-bind-token-1"),
            CancellationToken.None);

        var uri = new Uri(result.RedirectUrl);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("vneid.example.local", uri.Host);
        Assert.Equal("/oauth/authorize", uri.AbsolutePath);

        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("neuroscan-test", query["client_id"]);
        Assert.Equal("http://localhost:3000/auth/callback", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(result.State, query["state"]);

        var saved = store.Peek(result.State);
        Assert.NotNull(saved);
        Assert.Equal("/cases/123?tab=mri", saved.ReturnUrl);
        Assert.Equal(query["nonce"], saved.Nonce);

        var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(saved.CodeVerifier)))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        Assert.Equal(expectedChallenge, query["code_challenge"]);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example")]
    public async Task Handle_ExternalOrProtocolRelativeReturnUrl_ThrowsValidationException(string badReturnUrl)
    {
        var store = new InMemoryOidcTransactionStore();
        var settings = new FakeOidcSettings("http://localhost:5001", "neuroscan-client", "secret", "http://localhost:3000");
        var handler = new LoginQueryHandler(store, settings);

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            handler.Handle(new LoginQuery(badReturnUrl, null, "bind-1"), CancellationToken.None));
    }

    private sealed class FakeOidcSettings(string issuer, string clientId, string clientSecret, string frontendUrl) : IOidcSettings
    {
        public string Issuer { get; } = issuer;
        public string ClientId { get; } = clientId;
        public string ClientSecret { get; } = clientSecret;
        public string FrontendUrl { get; } = frontendUrl;
    }

    private sealed class InMemoryOidcTransactionStore : IOidcTransactionStore
    {
        private readonly Dictionary<string, OidcTransaction> _items = new();

        public Task SaveAsync(string state, OidcTransaction transaction, TimeSpan lifetime, CancellationToken ct = default)
        {
            _items[state] = transaction;
            return Task.CompletedTask;
        }

        public Task<OidcTransaction?> ConsumeAsync(string state, string? bindingToken, CancellationToken ct = default)
        {
            return Task.FromResult(_items.Remove(state, out var tx) ? tx : null);
        }

        public OidcTransaction? Peek(string state) => _items.GetValueOrDefault(state);
    }
}
