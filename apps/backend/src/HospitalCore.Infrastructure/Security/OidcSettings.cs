using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Infrastructure.ExternalServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HospitalCore.Infrastructure.Security;

public class OidcSettings : IOidcSettings
{
    private readonly VneidOptions _vneidOptions;
    private readonly string _frontendUrl;

    public OidcSettings(IOptions<VneidOptions> vneidOptions, IConfiguration configuration)
    {
        _vneidOptions = vneidOptions.Value;
        _frontendUrl = configuration["Frontend:Url"] ?? "http://localhost:3000";
    }

    public string Issuer => _vneidOptions.Issuer;
    public string ClientId => _vneidOptions.ClientId;
    public string ClientSecret => _vneidOptions.ClientSecret;
    public string FrontendUrl => _frontendUrl;
}
