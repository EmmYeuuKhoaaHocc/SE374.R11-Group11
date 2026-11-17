using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Application.Common.Interfaces.External;
using HospitalCore.Application.Common.Interfaces.Repositories;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Features.Auth.Callback;
using HospitalCore.Application.Features.Auth.Login;
using HospitalCore.Domain.Entities;
using HospitalCore.Domain.Enums;
using HospitalCore.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HospitalCore.Application.Tests;

public class CallbackCommandTests
{
    [Fact]
    public async Task Handle_InvalidOrMismatchedState_ThrowsPermissionDeniedAndAuditsDenial()
    {
        var txStore = new FakeOidcTransactionStore();
        await txStore.SaveAsync("good-state", new OidcTransaction
        {
            Nonce = "n1",
            CodeVerifier = "v1",
            RedirectUri = "http://localhost:3000/auth/callback",
            ReturnUrl = "/",
            BindingHash = LoginQueryHandler.ComputeBindingHash("good-bind")
        }, TimeSpan.FromMinutes(10));

        var audit = new FakeAuditService();
        var handler = CreateHandler(txStore: txStore, audit: audit);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.Handle(new CallbackCommand("code-1", "good-state", "wrong-bind"), CancellationToken.None));

        Assert.Equal("INVALID_OIDC_STATE", ex.Code);
        Assert.Contains(audit.Events, e => e.Action == "LOGIN_DENIED");
    }

    [Fact]
    public async Task Handle_CitizenWithoutCchn_ThrowsPermissionDeniedAndAuditsDenial()
    {
        var audit = new FakeAuditService();
        var handler = CreateHandler(
            claims: new VneidClaimsDto { Cccd = "001200012345", Name = "NGUYEN VAN AN", Cchn = null },
            audit: audit);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None));

        Assert.Equal("NO_CCHN_PERMISSION", ex.Code);
        Assert.Contains(audit.Events, e => e.Action == "LOGIN_DENIED");
    }

    [Fact]
    public async Task Handle_SuspendedPractitionerInByt_ThrowsPermissionDeniedAndAuditsDenial()
    {
        var audit = new FakeAuditService();
        var govClient = new FakeMockGovCitizenClient
        {
            PractitionerToReturn = new PractitionerVerificationDto
            {
                MaCchn = "009922/BYT-CCHN",
                Cccd = "001200077777",
                HoVaTen = "BS. PHAM HOANG LONG",
                MaCskcbCongTac = "01015",
                TrangThai = "Suspended"
            }
        };

        var handler = CreateHandler(
            claims: new VneidClaimsDto
            {
                Cccd = "001200077777",
                Name = "BS. PHAM HOANG LONG",
                Cchn = "009922/BYT-CCHN",
                FacilityId = "01015"
            },
            govClient: govClient,
            audit: audit);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None));

        Assert.Equal("PRACTITIONER_SUSPENDED", ex.Code);
        Assert.Contains(audit.Events, e => e.Action == "LOGIN_DENIED");
    }

    [Fact]
    public async Task Handle_ActivePractitionerWithNewHospital_AutoRegistersHospitalSavesHospitalNameAndAuditsSuccess()
    {
        var govClient = new FakeMockGovCitizenClient
        {
            PractitionerToReturn = new PractitionerVerificationDto
            {
                MaCchn = "008891/BYT-CCHN",
                Cccd = "001200088888",
                HoVaTen = "BS. LE THI MAI",
                MaCskcbCongTac = "01001",
                TrangThai = "Active"
            },
            FacilityToReturn = new FacilityVerificationDto
            {
                MaBv = "01001",
                TenBv = "Bệnh viện Bạch Mai",
                DiaChi = "78 Giải Phóng, Hà Nội"
            }
        };

        var hospitalRepo = new FakeHospitalRepository();
        var userRepo = new FakeUserRepository();
        var sessionManager = new FakeSessionManager();
        var audit = new FakeAuditService();

        var handler = CreateHandler(
            claims: new VneidClaimsDto
            {
                Cccd = "001200088888",
                Name = "BS. LE THI MAI",
                Cchn = "008891/BYT-CCHN",
                FacilityId = "01001"
            },
            govClient: govClient,
            hospitalRepo: hospitalRepo,
            userRepo: userRepo,
            sessionManager: sessionManager,
            audit: audit);

        var result = await handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None);

        Assert.Equal("Bệnh viện Bạch Mai", result.HospitalName);
        Assert.Equal("01001", result.MaCskcb);
        Assert.Equal("DOCTOR_USER", result.Role);
        Assert.Equal("/dashboard", result.ReturnUrl);

        var savedSession = sessionManager.LastSaved;
        Assert.NotNull(savedSession);
        Assert.Equal("Bệnh viện Bạch Mai", savedSession.HospitalName);
        Assert.Contains(audit.Events, e => e.Action == "LOGIN_SUCCESS");
    }

    [Fact]
    public async Task Handle_ExistingHospitalAdmin_PreservesDbAssignedRole()
    {
        var hospital = new Hospital("79011", "Bệnh viện Nhân dân 115", "TP. HCM", isActive: true);
        var existingAdmin = new User("001200055555", "BS. NGUYỄN VĂN ĐỨC", UserRole.HOSPITAL_ADMIN, hospital.Id, "003344/BYT-CCHN", isActive: true);

        var hospitalRepo = new FakeHospitalRepository();
        await hospitalRepo.AddAsync(hospital);
        var userRepo = new FakeUserRepository();
        await userRepo.AddAsync(existingAdmin);

        var govClient = new FakeMockGovCitizenClient
        {
            PractitionerToReturn = new PractitionerVerificationDto
            {
                MaCchn = "003344/BYT-CCHN",
                Cccd = "001200055555",
                HoVaTen = "BS. NGUYỄN VĂN ĐỨC",
                MaCskcbCongTac = "79011",
                TrangThai = "Active"
            }
        };

        var handler = CreateHandler(
            claims: new VneidClaimsDto
            {
                Cccd = "001200055555",
                Name = "BS. NGUYỄN VĂN ĐỨC",
                Cchn = "003344/BYT-CCHN",
                FacilityId = "79011"
            },
            govClient: govClient,
            hospitalRepo: hospitalRepo,
            userRepo: userRepo);

        var result = await handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None);

        Assert.Equal("HOSPITAL_ADMIN", result.Role);
    }

    [Fact]
    public async Task Handle_InactiveUserInDb_ThrowsPermissionDeniedAndAuditsDenial()
    {
        var hospital = new Hospital("79024", "Bệnh viện Chợ Rẫy", "TP. HCM", isActive: true);
        var suspendedUser = new User("001200099999", "BS. TRẦN MINH TÂM", UserRole.DOCTOR_USER, hospital.Id, "004512/BYT-CCHN", isActive: false);

        var hospitalRepo = new FakeHospitalRepository();
        await hospitalRepo.AddAsync(hospital);
        var userRepo = new FakeUserRepository();
        await userRepo.AddAsync(suspendedUser);

        var govClient = new FakeMockGovCitizenClient
        {
            PractitionerToReturn = new PractitionerVerificationDto
            {
                MaCchn = "004512/BYT-CCHN",
                Cccd = "001200099999",
                HoVaTen = "BS. TRẦN MINH TÂM",
                MaCskcbCongTac = "79024",
                TrangThai = "Active"
            }
        };

        var audit = new FakeAuditService();
        var handler = CreateHandler(
            claims: new VneidClaimsDto
            {
                Cccd = "001200099999",
                Name = "BS. TRẦN MINH TÂM",
                Cchn = "004512/BYT-CCHN",
                FacilityId = "79024"
            },
            govClient: govClient,
            hospitalRepo: hospitalRepo,
            userRepo: userRepo,
            audit: audit);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None));

        Assert.Equal("USER_SUSPENDED", ex.Code);
        Assert.Contains(audit.Events, e => e.Action == "LOGIN_DENIED");
    }

    [Fact]
    public async Task Handle_MissingFacilityCode_ThrowsHospitalNotActiveWithoutChoRayFallback()
    {
        var govClient = new FakeMockGovCitizenClient
        {
            PractitionerToReturn = new PractitionerVerificationDto
            {
                MaCchn = "004512/BYT-CCHN",
                Cccd = "001200099999",
                HoVaTen = "BS. TRẦN MINH TÂM",
                MaCskcbCongTac = "",
                TrangThai = "Active"
            }
        };

        var handler = CreateHandler(
            claims: new VneidClaimsDto
            {
                Cccd = "001200099999",
                Name = "BS. TRẦN MINH TÂM",
                Cchn = "004512/BYT-CCHN",
                FacilityId = null
            },
            govClient: govClient);

        await Assert.ThrowsAsync<HospitalNotActiveException>(() =>
            handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OidcValidationDenied_AuditsLoginDeniedAndRethrows()
    {
        var audit = new FakeAuditService();
        var handler = CreateHandler(
            vneidClient: new FailingVneidAuthClient("INVALID_OIDC_NONCE"),
            audit: audit);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None));

        Assert.Equal("INVALID_OIDC_NONCE", ex.Code);
        Assert.Contains(audit.Events, e => e.Action == "LOGIN_DENIED");
    }

    [Fact]
    public async Task Handle_CommitThrows_RollsBackAndDoesNotSaveSession()
    {
        var govClient = new FakeMockGovCitizenClient
        {
            PractitionerToReturn = new PractitionerVerificationDto
            {
                MaCchn = "008891/BYT-CCHN",
                Cccd = "001200088888",
                HoVaTen = "BS. LE THI MAI",
                MaCskcbCongTac = "01001",
                TrangThai = "Active"
            },
            FacilityToReturn = new FacilityVerificationDto
            {
                MaBv = "01001",
                TenBv = "Bệnh viện Bạch Mai",
                DiaChi = "78 Giải Phóng, Hà Nội"
            }
        };

        var sessionManager = new FakeSessionManager();
        var unitOfWork = new FakeUnitOfWork { ThrowOnCommit = true };

        var handler = CreateHandler(
            claims: new VneidClaimsDto
            {
                Cccd = "001200088888",
                Name = "BS. LE THI MAI",
                Cchn = "008891/BYT-CCHN",
                FacilityId = "01001"
            },
            govClient: govClient,
            sessionManager: sessionManager,
            unitOfWork: unitOfWork);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CallbackCommand("code-1", "state-1", "bind-1"), CancellationToken.None));

        Assert.True(unitOfWork.RolledBack);
        Assert.Null(sessionManager.LastSaved);
    }

    private static CallbackCommandHandler CreateHandler(
        VneidClaimsDto? claims = null,
        IVneidAuthClient? vneidClient = null,
        FakeOidcTransactionStore? txStore = null,
        FakeMockGovCitizenClient? govClient = null,
        FakeHospitalRepository? hospitalRepo = null,
        FakeUserRepository? userRepo = null,
        FakeSessionManager? sessionManager = null,
        FakeAuditService? audit = null,
        FakeUnitOfWork? unitOfWork = null)
    {
        if (txStore == null)
        {
            txStore = new FakeOidcTransactionStore();
            txStore.SaveAsync("state-1", new OidcTransaction
            {
                Nonce = "nonce-1",
                CodeVerifier = "verifier-1",
                RedirectUri = "http://localhost:3000/auth/callback",
                ReturnUrl = "/dashboard",
                BindingHash = LoginQueryHandler.ComputeBindingHash("bind-1")
            }, TimeSpan.FromMinutes(10)).GetAwaiter().GetResult();
        }

        return new CallbackCommandHandler(
            vneidClient ?? new FakeVneidAuthClient(claims ?? new VneidClaimsDto()),
            govClient ?? new FakeMockGovCitizenClient(),
            hospitalRepo ?? new FakeHospitalRepository(),
            userRepo ?? new FakeUserRepository(),
            txStore,
            sessionManager ?? new FakeSessionManager(),
            audit ?? new FakeAuditService(),
            unitOfWork ?? new FakeUnitOfWork(),
            NullLogger<CallbackCommandHandler>.Instance);
    }

    private sealed class FailingVneidAuthClient(string errorCode) : IVneidAuthClient
    {
        public Task<VneidTokenResponse> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
        {
            return Task.FromResult(new VneidTokenResponse
            {
                AccessToken = "access-1",
                IdToken = "id-1",
                ExpiresIn = 3600
            });
        }

        public Task<VneidClaimsDto> ValidateIdTokenAsync(string idToken, string expectedNonce, CancellationToken ct = default)
        {
            throw new PermissionDeniedException("Nonce mismatch.", errorCode);
        }
    }

    private sealed class FakeVneidAuthClient(VneidClaimsDto claims) : IVneidAuthClient
    {
        public Task<VneidTokenResponse> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
        {
            return Task.FromResult(new VneidTokenResponse
            {
                AccessToken = "access-1",
                IdToken = "id-1",
                ExpiresIn = 3600
            });
        }

        public Task<VneidClaimsDto> ValidateIdTokenAsync(string idToken, string expectedNonce, CancellationToken ct = default)
        {
            return Task.FromResult(claims);
        }
    }

    private sealed class FakeOidcTransactionStore : IOidcTransactionStore
    {
        private readonly Dictionary<string, OidcTransaction> _items = new();

        public Task SaveAsync(string state, OidcTransaction transaction, TimeSpan lifetime, CancellationToken ct = default)
        {
            _items[state] = transaction;
            return Task.CompletedTask;
        }

        public Task<OidcTransaction?> ConsumeAsync(string state, string? bindingToken, CancellationToken ct = default)
        {
            if (!_items.Remove(state, out var tx))
            {
                return Task.FromResult<OidcTransaction?>(null);
            }

            if (tx.BindingHash != LoginQueryHandler.ComputeBindingHash(bindingToken))
            {
                return Task.FromResult<OidcTransaction?>(null);
            }

            return Task.FromResult<OidcTransaction?>(tx);
        }
    }

    private sealed class FakeMockGovCitizenClient : IMockGovCitizenClient
    {
        public PractitionerVerificationDto? PractitionerToReturn { get; set; }
        public FacilityVerificationDto? FacilityToReturn { get; set; }

        public Task<CitizenVerificationDto?> GetCitizenByCccdAsync(string cccd, CancellationToken ct = default) =>
            Task.FromResult<CitizenVerificationDto?>(null);

        public Task<PractitionerVerificationDto?> GetPractitionerByCchnAsync(string cchn, CancellationToken ct = default) =>
            Task.FromResult(PractitionerToReturn);

        public Task<FacilityVerificationDto?> GetFacilityByCodeAsync(string maBv, CancellationToken ct = default) =>
            Task.FromResult(FacilityToReturn);
    }

    private sealed class FakeHospitalRepository : IHospitalRepository
    {
        private readonly List<Hospital> _hospitals = new();

        public Task<Hospital?> GetByIdAsync(Guid hospitalId, CancellationToken ct = default) =>
            Task.FromResult(_hospitals.FirstOrDefault(h => h.Id == hospitalId));

        public Task<Hospital?> GetByMaCskcbAsync(string maCskcb, CancellationToken ct = default) =>
            Task.FromResult(_hospitals.FirstOrDefault(h => h.MaCskcb == maCskcb));

        public Task<IReadOnlyList<Hospital>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Hospital>>(_hospitals);

        public Task AddAsync(Hospital hospital, CancellationToken ct = default)
        {
            _hospitals.Add(hospital);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Hospital hospital, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users = new();

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_users.FirstOrDefault(u => u.Id == userId));

        public Task<User?> GetByCccdAsync(string cccd, CancellationToken ct = default) =>
            Task.FromResult(_users.FirstOrDefault(u => u.Cccd == cccd));

        public Task AddAsync(User user, CancellationToken ct = default)
        {
            _users.Add(user);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        public UserSession? LastSaved { get; private set; }

        public Task<UserSession?> GetSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult<UserSession?>(null);

        public Task SaveSessionAsync(UserSession session, TimeSpan lifetime, CancellationToken ct = default)
        {
            LastSaved = session;
            return Task.CompletedTask;
        }

        public Task RemoveSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuditService : IAuditService
    {
        public List<(Guid? UserId, string Action, string ResourceType, string ResourceId)> Events { get; } = new();

        public Task RecordAsync(Guid? userId, string action, string resourceType, string resourceId, object? payload = null, CancellationToken ct = default)
        {
            Events.Add((userId, action, resourceType, resourceId));
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(Guid? userId, string action, string resourceType, string resourceId, object? payload = null, CancellationToken ct = default)
        {
            Events.Add((userId, action, resourceType, resourceId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool ThrowOnCommit { get; init; }
        public bool RolledBack { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnCommit)
            {
                throw new InvalidOperationException("Simulated DB commit failure.");
            }

            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RolledBack = true;
            return Task.CompletedTask;
        }
    }
}
