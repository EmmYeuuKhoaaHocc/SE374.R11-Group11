namespace HospitalCore.Application.Common.Interfaces.Security;

public static class AppAuthorizationPolicies
{
    public const string RequireDoctor = "RequireDoctor";
    public const string RequireHospitalAdmin = "RequireHospitalAdmin";
    public const string RequireMohAdmin = "RequireMohAdmin";
}
