namespace HospitalCore.Application.Common.Interfaces.Security;

public interface IOidcSettings
{
    string Issuer { get; }
    string ClientId { get; }
    string ClientSecret { get; }
    string FrontendUrl { get; }
}
