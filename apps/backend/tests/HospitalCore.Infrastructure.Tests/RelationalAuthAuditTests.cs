using System.Net;
using System.Net.Http.Json;
using HospitalCore.Api.Endpoints;
using HospitalCore.Application.Common.Interfaces.External;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Domain.Entities;
using HospitalCore.Domain.Enums;
using HospitalCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HospitalCore.Infrastructure.Tests;

public class RelationalAuthAuditTests
{
    [Fact]
    public async Task FirstTimePractitionerLogin_WithRelationalForeignKeys_CommitsHospitalUserAuditLogAndSession()
    {
        var trackingSessionManager = new TrackingSessionManager();
        await using var factory = new SqliteApiFactory(
            new StubVneidClient(
                cccd: "079095009001",
                name: "BS. NGUYEN THI LAN",
                cchn: "BYT-REL-001",
                facilityId: "01001"),
            trackingSessionManager);
        await factory.InitializeDatabaseAsync();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginResponse = await client.GetAsync("/api/auth/login?returnUrl=/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var state = ExtractQueryParam(loginResponse.Headers.Location?.ToString() ?? string.Empty, "state");
        Assert.False(string.IsNullOrWhiteSpace(state));

        var exchangeResponse = await client.PostAsJsonAsync(
            "/api/auth/exchange-session",
            new ExchangeSessionRequest("auth-code-rel-1", state));

        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);
        var exchangeBody = await exchangeResponse.Content.ReadFromJsonAsync<ExchangeSessionResponse>();
        Assert.NotNull(exchangeBody);
        Assert.Equal("01001", exchangeBody!.MaCskcb);
        Assert.Equal("DOCTOR_USER", exchangeBody.Role);
        Assert.Equal(1, trackingSessionManager.SaveCount);

        var meResponse = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var hospital = await db.Hospitals.SingleAsync(h => h.MaCskcb == "01001");
        var user = await db.Users.SingleAsync(u => u.Cccd == "079095009001");
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "LOGIN_SUCCESS");

        Assert.Equal(hospital.Id, user.HospitalId);
        Assert.Equal(user.Id, audit.UserId);
    }

    [Fact]
    public async Task SuspendedUserLogin_WithRelationalForeignKeys_RollsBackAndPersistsLoginDeniedWithoutSession()
    {
        var trackingSessionManager = new TrackingSessionManager();
        await using var factory = new SqliteApiFactory(
            new StubVneidClient(
                cccd: "079095009002",
                name: "BS. TRAN VAN HUNG",
                cchn: "BYT-REL-002",
                facilityId: "01002"),
            trackingSessionManager);
        await factory.InitializeDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var hospital = new Hospital("01002", "Benh vien Huu nghi Viet Duc", "Ha Noi", isActive: true);
        var suspendedUser = new User("079095009002", "BS. TRAN VAN HUNG", UserRole.DOCTOR_USER, hospital.Id, "BYT-REL-002", isActive: false);
        seedDb.Hospitals.Add(hospital);
        seedDb.Users.Add(suspendedUser);
        await seedDb.SaveChangesAsync();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginResponse = await client.GetAsync("/api/auth/login");
        var state = ExtractQueryParam(loginResponse.Headers.Location?.ToString() ?? string.Empty, "state");

        var exchangeResponse = await client.PostAsJsonAsync(
            "/api/auth/exchange-session",
            new ExchangeSessionRequest("auth-code-rel-2", state));

        Assert.Equal(HttpStatusCode.Forbidden, exchangeResponse.StatusCode);
        Assert.Equal(0, trackingSessionManager.SaveCount);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "LOGIN_DENIED");
        Assert.Equal(suspendedUser.Id, audit.UserId);
        Assert.Contains("USER_SUSPENDED", audit.PayloadSnapshot);
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == "LOGIN_SUCCESS");
    }

    [Fact]
    public async Task DeniedCitizenWithoutCchn_WithRelationalForeignKeys_PersistsLoginDeniedAndCreatesNoDomainRows()
    {
        var trackingSessionManager = new TrackingSessionManager();
        await using var factory = new SqliteApiFactory(
            new StubVneidClient(
                cccd: "079095009003",
                name: "NGUYEN VAN CITIZEN",
                cchn: null,
                facilityId: null),
            trackingSessionManager);
        await factory.InitializeDatabaseAsync();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginResponse = await client.GetAsync("/api/auth/login");
        var state = ExtractQueryParam(loginResponse.Headers.Location?.ToString() ?? string.Empty, "state");

        var exchangeResponse = await client.PostAsJsonAsync(
            "/api/auth/exchange-session",
            new ExchangeSessionRequest("auth-code-rel-3", state));

        Assert.Equal(HttpStatusCode.Forbidden, exchangeResponse.StatusCode);
        Assert.Equal(0, trackingSessionManager.SaveCount);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        Assert.Empty(await db.Hospitals.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "LOGIN_DENIED");
        Assert.Null(audit.UserId);
        Assert.Contains("NO_CCHN_PERMISSION", audit.PayloadSnapshot);
    }

    [Fact]
    public async Task RecordFailureAsync_WithMissingUserIdAndRelationalForeignKeys_FallsBackToNullWithoutFkViolation()
    {
        await using var factory = new SqliteApiFactory(new StubVneidClient("079095009004", "Test", null, null));
        await factory.InitializeDatabaseAsync();

        var missingUserId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await auditService.RecordFailureAsync(
                missingUserId,
                "LOGIN_DENIED",
                "Auth",
                "079095009004",
                new { code = "UNKNOWN_USER" });
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var audit = await db.AuditLogs.SingleAsync();
        Assert.Null(audit.UserId);
        Assert.Contains(missingUserId.ToString(), audit.PayloadSnapshot);
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

    private sealed class SqliteApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"neuroscan-rel-{Guid.NewGuid():N}.db");
        private readonly IVneidAuthClient _vneidClient;
        private readonly TrackingSessionManager? _trackingSessionManager;

        public SqliteApiFactory(IVneidAuthClient vneidClient, TrackingSessionManager? trackingSessionManager = null)
        {
            _vneidClient = vneidClient;
            _trackingSessionManager = trackingSessionManager;
        }

        private string ConnectionString => $"Data Source={_dbPath};Foreign Keys=True;Pooling=False";

        public async Task InitializeDatabaseAsync()
        {
            await using var conn = new SqliteConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            await cmd.ExecuteNonQueryAsync();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Redis:ConnectionString", "");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<HospitalDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<HospitalDbContext>>();
                services.RemoveAll<HospitalDbContext>();
                services.AddDbContext<HospitalDbContext>(o => o.UseSqlite(ConnectionString));

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
                services.RemoveAll<IVneidAuthClient>();
                services.RemoveAll<IMockGovCitizenClient>();
                services.AddSingleton(_vneidClient);
                services.AddSingleton<IMockGovCitizenClient>(new StubMockGovClient());

                if (_trackingSessionManager != null)
                {
                    services.RemoveAll<ISessionManager>();
                    services.AddSingleton<ISessionManager>(_trackingSessionManager);
                }
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            SqliteConnection.ClearAllPools();
            DeleteIfExists(_dbPath);
            DeleteIfExists(_dbPath + "-wal");
            DeleteIfExists(_dbPath + "-shm");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class StubVneidClient(string cccd, string name, string? cchn, string? facilityId) : IVneidAuthClient
    {
        public Task<VneidTokenResponse> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
        {
            return Task.FromResult(new VneidTokenResponse
            {
                AccessToken = "at-rel",
                IdToken = "idt-rel",
                ExpiresIn = 3600
            });
        }

        public Task<VneidClaimsDto> ValidateIdTokenAsync(string idToken, string expectedNonce, CancellationToken ct = default)
        {
            return Task.FromResult(new VneidClaimsDto
            {
                Cccd = cccd,
                Name = name,
                Cchn = cchn,
                FacilityId = facilityId
            });
        }
    }

    private sealed class StubMockGovClient : IMockGovCitizenClient
    {
        public Task<CitizenVerificationDto?> GetCitizenByCccdAsync(string cccd, CancellationToken ct = default) =>
            Task.FromResult<CitizenVerificationDto?>(null);

        public Task<PractitionerVerificationDto?> GetPractitionerByCchnAsync(string cchn, CancellationToken ct = default) =>
            Task.FromResult<PractitionerVerificationDto?>(new PractitionerVerificationDto
            {
                MaCchn = cchn,
                HoVaTen = "BS. TEST",
                MaCskcbCongTac = cchn == "BYT-REL-001" ? "01001" : "01002",
                TrangThai = "Active"
            });

        public Task<FacilityVerificationDto?> GetFacilityByCodeAsync(string maBv, CancellationToken ct = default) =>
            Task.FromResult<FacilityVerificationDto?>(new FacilityVerificationDto
            {
                MaBv = maBv,
                TenBv = "Benh vien Bach Mai",
                DiaChi = "78 Giai Phong, Ha Noi"
            });
    }

    private sealed class TrackingSessionManager : ISessionManager
    {
        private readonly Dictionary<string, UserSession> _sessions = new();
        public int SaveCount { get; private set; }

        public Task<UserSession?> GetSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_sessions.GetValueOrDefault(sessionId));

        public Task SaveSessionAsync(UserSession session, TimeSpan lifetime, CancellationToken ct = default)
        {
            SaveCount++;
            _sessions[session.SessionId] = session;
            return Task.CompletedTask;
        }

        public Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
        {
            _sessions.Remove(sessionId);
            return Task.CompletedTask;
        }
    }
}
