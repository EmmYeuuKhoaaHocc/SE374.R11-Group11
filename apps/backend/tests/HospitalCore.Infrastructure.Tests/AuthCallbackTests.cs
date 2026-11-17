using System.Net;
using System.Net.Http.Json;
using HospitalCore.Api.Endpoints;
using HospitalCore.Application.Common.Interfaces.External;
using HospitalCore.Domain.Entities;
using HospitalCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HospitalCore.Infrastructure.Tests;

public class AuthCallbackTests
{
    [Fact]
    public async Task LoginExchangeMeAndLogout_FullCookieFlowSucceeds()
    {
        await using var factory = new TestApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginResponse = await client.GetAsync("/api/auth/login?returnUrl=/worklist");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var location = loginResponse.Headers.Location?.ToString() ?? string.Empty;
        var state = ExtractQueryParam(location, "state");
        Assert.False(string.IsNullOrWhiteSpace(state));

        var exchangeResponse = await client.PostAsJsonAsync(
            "/api/auth/exchange-session",
            new ExchangeSessionRequest("auth-code-1", state));

        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);
        var exchangeBody = await exchangeResponse.Content.ReadFromJsonAsync<ExchangeSessionResponse>();
        Assert.NotNull(exchangeBody);
        Assert.Equal("79024", exchangeBody!.MaCskcb);
        Assert.Equal("Benh vien Cho Ray", exchangeBody.HospitalName);
        Assert.Equal("DOCTOR_USER", exchangeBody.Role);
        Assert.Equal("/worklist", exchangeBody.ReturnUrl);

        var meResponse = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var meAfterLogout = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);
    }

    [Fact]
    public async Task ExchangeSession_DeniedPractitioner_Returns403ProblemDetailsAndAuditsDenial()
    {
        await using var factory = new TestApiFactory(allowActivePractitioner: false);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginResponse = await client.GetAsync("/api/auth/login");
        var state = ExtractQueryParam(loginResponse.Headers.Location?.ToString() ?? string.Empty, "state");

        var exchangeResponse = await client.PostAsJsonAsync(
            "/api/auth/exchange-session",
            new ExchangeSessionRequest("auth-code-2", state));

        Assert.Equal(HttpStatusCode.Forbidden, exchangeResponse.StatusCode);
        var json = await exchangeResponse.Content.ReadAsStringAsync();
        Assert.Contains("NO_CCHN_PERMISSION", json);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "LOGIN_DENIED");
        Assert.Contains("NO_CCHN_PERMISSION", audit.PayloadSnapshot);
    }

    private static string? ExtractQueryParam(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");
        private readonly bool _allowActivePractitioner;

        public TestApiFactory(bool allowActivePractitioner = true)
        {
            _allowActivePractitioner = allowActivePractitioner;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Redis:ConnectionString", "");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<HospitalDbContext>>();
                services.RemoveAll<HospitalDbContext>();
                var inMemoryEfProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();
                services.AddDbContext<HospitalDbContext>(o =>
                    o.UseInMemoryDatabase(_dbName).UseInternalServiceProvider(inMemoryEfProvider));

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
                services.RemoveAll<IVneidAuthClient>();
                services.RemoveAll<IMockGovCitizenClient>();
                services.AddSingleton<IVneidAuthClient>(new FakeVneidClient(_allowActivePractitioner));
                services.AddSingleton<IMockGovCitizenClient>(new FakeMockGovClient());
            });
        }
    }

    private sealed class FakeVneidClient : IVneidAuthClient
    {
        private readonly bool _hasCchn;

        public FakeVneidClient(bool hasCchn)
        {
            _hasCchn = hasCchn;
        }

        public Task<VneidTokenResponse> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
        {
            return Task.FromResult(new VneidTokenResponse
            {
                AccessToken = "at-1",
                IdToken = "idt-1",
                ExpiresIn = 3600
            });
        }

        public Task<VneidClaimsDto> ValidateIdTokenAsync(string idToken, string expectedNonce, CancellationToken ct = default)
        {
            return Task.FromResult(new VneidClaimsDto
            {
                Cccd = "079095001122",
                Name = "Nguyen Van Integration",
                Cchn = _hasCchn ? "BYT-001" : null,
                FacilityId = "79024"
            });
        }
    }

    private sealed class FakeMockGovClient : IMockGovCitizenClient
    {
        public Task<CitizenVerificationDto?> GetCitizenByCccdAsync(string cccd, CancellationToken ct = default)
            => Task.FromResult<CitizenVerificationDto?>(null);

        public Task<PractitionerVerificationDto?> GetPractitionerByCchnAsync(string cchn, CancellationToken ct = default)
            => Task.FromResult<PractitionerVerificationDto?>(new PractitionerVerificationDto
            {
                MaCchn = cchn,
                HoVaTen = "Nguyen Van Integration",
                MaCskcbCongTac = "79024",
                TrangThai = "Active"
            });

        public Task<FacilityVerificationDto?> GetFacilityByCodeAsync(string maBv, CancellationToken ct = default)
            => Task.FromResult<FacilityVerificationDto?>(new FacilityVerificationDto
            {
                MaBv = maBv,
                TenBv = "Benh vien Cho Ray",
                DiaChi = "201B Nguyen Chi Thanh"
            });
    }
}
