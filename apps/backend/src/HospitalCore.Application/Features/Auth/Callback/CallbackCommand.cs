using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Application.Common.Interfaces.External;
using HospitalCore.Application.Common.Interfaces.Repositories;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Common.Mediator;
using HospitalCore.Domain.Entities;
using HospitalCore.Domain.Enums;
using HospitalCore.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace HospitalCore.Application.Features.Auth.Callback;

public record CallbackCommand(
    string Code,
    string? State,
    string? BindingToken = null,
    string? RedirectUri = null) : IRequest<SessionResultDto>;

public record SessionResultDto(
    string SessionId,
    Guid UserId,
    string FullName,
    string Role,
    string MaCskcb,
    string HospitalName,
    string? MaCchn,
    string ReturnUrl = "/");

public class CallbackCommandHandler : IRequestHandler<CallbackCommand, SessionResultDto>
{
    private readonly IVneidAuthClient _vneidAuthClient;
    private readonly IMockGovCitizenClient _mockGovClient;
    private readonly IHospitalRepository _hospitalRepository;
    private readonly IUserRepository _userRepository;
    private readonly IOidcTransactionStore _transactionStore;
    private readonly ISessionManager _sessionManager;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CallbackCommandHandler> _logger;

    public CallbackCommandHandler(
        IVneidAuthClient vneidAuthClient,
        IMockGovCitizenClient mockGovClient,
        IHospitalRepository hospitalRepository,
        IUserRepository userRepository,
        IOidcTransactionStore transactionStore,
        ISessionManager sessionManager,
        IAuditService auditService,
        IUnitOfWork unitOfWork,
        ILogger<CallbackCommandHandler> logger)
    {
        _vneidAuthClient = vneidAuthClient;
        _mockGovClient = mockGovClient;
        _hospitalRepository = hospitalRepository;
        _userRepository = userRepository;
        _transactionStore = transactionStore;
        _sessionManager = sessionManager;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<SessionResultDto> Handle(CallbackCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new ApplicationValidationException("Code", "Authorization code must be provided.");
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            await RecordDenialAsync(null, "INVALID_OIDC_STATE", "Missing OIDC state.", "unknown", cancellationToken);
            throw new PermissionDeniedException("Invalid or missing OIDC state parameter.", "INVALID_OIDC_STATE");
        }

        var transaction = await _transactionStore.ConsumeAsync(request.State, request.BindingToken, cancellationToken);
        if (transaction == null)
        {
            await RecordDenialAsync(null, "INVALID_OIDC_STATE", "Invalid, expired, or mismatched OIDC state.", request.State, cancellationToken);
            throw new PermissionDeniedException("Invalid, expired, or mismatched OIDC login session.", "INVALID_OIDC_STATE");
        }

        VneidTokenResponse tokenResponse;
        VneidClaimsDto claims;
        try
        {
            tokenResponse = await _vneidAuthClient.ExchangeCodeAsync(
                request.Code,
                transaction.RedirectUri,
                transaction.CodeVerifier,
                cancellationToken);

            claims = await _vneidAuthClient.ValidateIdTokenAsync(
                tokenResponse.IdToken ?? string.Empty,
                transaction.Nonce,
                cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            await RecordDenialAsync(null, ex.Code, ex.Message, request.State, cancellationToken);
            throw;
        }

        if (string.IsNullOrWhiteSpace(claims.Cchn))
        {
            _logger.LogWarning("Denying login for CCCD '{Cccd}': citizen has no CCHN", claims.Cccd);
            await RecordDenialAsync(null, "NO_CCHN_PERMISSION", "Citizen has no CCHN.", claims.Cccd, cancellationToken);
            throw new PermissionDeniedException(
                "Citizen does not hold an active CCHN.",
                "NO_CCHN_PERMISSION");
        }

        var practitioner = await _mockGovClient.GetPractitionerByCchnAsync(claims.Cchn, cancellationToken);
        if (practitioner == null || !practitioner.TrangThai.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Denying login for CCHN '{Cchn}': practitioner is missing or inactive in BYT", claims.Cchn);
            await RecordDenialAsync(null, "PRACTITIONER_SUSPENDED", "Practitioner CCHN is inactive in BYT.", claims.Cchn, cancellationToken);
            throw new PermissionDeniedException(
                "Practitioner CCHN is suspended or not active in the Ministry of Health registry.",
                "PRACTITIONER_SUSPENDED");
        }

        var facilityCode = !string.IsNullOrWhiteSpace(practitioner.MaCskcbCongTac)
            ? practitioner.MaCskcbCongTac.Trim()
            : claims.FacilityId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(facilityCode))
        {
            await RecordDenialAsync(null, "HOSPITAL_NOT_ONBOARDED", "Missing practitioner MaCskcb.", claims.Cccd, cancellationToken);
            throw new HospitalNotActiveException("UNKNOWN");
        }

        var existingHospital = await _hospitalRepository.GetByMaCskcbAsync(facilityCode, cancellationToken);
        FacilityVerificationDto? facilityInfo = null;
        if (existingHospital == null)
        {
            facilityInfo = await _mockGovClient.GetFacilityByCodeAsync(facilityCode, cancellationToken);
            if (facilityInfo == null || string.IsNullOrWhiteSpace(facilityInfo.TenBv))
            {
                await RecordDenialAsync(null, "HOSPITAL_NOT_ONBOARDED", "Hospital not found in BYT.", facilityCode, cancellationToken);
                throw new HospitalNotActiveException(facilityCode);
            }
        }
        else if (!existingHospital.IsActive)
        {
            await RecordDenialAsync(null, "HOSPITAL_NOT_ONBOARDED", "Hospital is inactive.", facilityCode, cancellationToken);
            throw new HospitalNotActiveException(facilityCode);
        }

        var sessionId = Guid.NewGuid().ToString("N");
        Hospital hospital;
        User user;
        string roleName;
        Guid? suspendedUserId = null;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (existingHospital == null)
            {
                hospital = new Hospital(
                    maCskcb: facilityInfo!.MaBv,
                    name: facilityInfo.TenBv,
                    address: string.IsNullOrWhiteSpace(facilityInfo.DiaChi) ? "Vietnam" : facilityInfo.DiaChi,
                    isActive: true);

                await _hospitalRepository.AddAsync(hospital, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            else
            {
                hospital = existingHospital;
            }

            var existingUser = await _userRepository.GetByCccdAsync(claims.Cccd, cancellationToken);
            if (existingUser == null)
            {
                user = new User(
                    claims.Cccd,
                    claims.Name,
                    UserRole.DOCTOR_USER,
                    hospital.Id,
                    claims.Cchn,
                    isActive: true);

                await _userRepository.AddAsync(user, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            else
            {
                if (!existingUser.IsActive)
                {
                    suspendedUserId = existingUser.Id;
                    throw new PermissionDeniedException("User is suspended.", "USER_SUSPENDED");
                }

                existingUser.UpdateProfile(claims.Name, claims.Cchn);
                existingUser.AssignHospital(hospital.Id);
                await _userRepository.UpdateAsync(existingUser, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                user = existingUser;
            }

            roleName = user.Role.ToString();

            await _auditService.RecordAsync(
                user.Id,
                "LOGIN_SUCCESS",
                "UserSession",
                sessionId,
                new { maCskcb = hospital.MaCskcb, role = roleName },
                cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (PermissionDeniedException ex) when (ex.Code == "USER_SUSPENDED")
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            await RecordDenialAsync(
                suspendedUserId,
                "USER_SUSPENDED",
                "User is suspended.",
                suspendedUserId?.ToString() ?? claims.Cccd,
                cancellationToken);
            throw;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        var userSession = new UserSession
        {
            SessionId = sessionId,
            UserId = user.Id,
            Cccd = claims.Cccd,
            FullName = user.FullName,
            Role = roleName,
            HospitalId = hospital.Id,
            MaCskcb = hospital.MaCskcb,
            HospitalName = hospital.Name,
            MaCchn = user.MaCchn,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            IdToken = tokenResponse.IdToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8)
        };

        await _sessionManager.SaveSessionAsync(userSession, TimeSpan.FromHours(8), cancellationToken);

        return new SessionResultDto(
            sessionId,
            user.Id,
            user.FullName,
            roleName,
            hospital.MaCskcb,
            hospital.Name,
            user.MaCchn,
            transaction.ReturnUrl);
    }

    private Task RecordDenialAsync(
        Guid? userId,
        string code,
        string reason,
        string resourceId,
        CancellationToken cancellationToken)
    {
        return _auditService.RecordFailureAsync(
            userId,
            "LOGIN_DENIED",
            "Auth",
            resourceId,
            new { code, reason },
            cancellationToken);
    }
}
