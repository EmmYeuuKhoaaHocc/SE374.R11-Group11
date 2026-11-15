using HospitalCore.Application.Common.Interfaces.External;
using HospitalCore.Application.Common.Interfaces.Repositories;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Infrastructure.ExternalServices;
using HospitalCore.Infrastructure.Persistence;
using HospitalCore.Infrastructure.Persistence.Repositories;
using HospitalCore.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HospitalCore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<HospitalDbContext>(options =>
            options.UseNpgsql(connectionString));

        var redisConnectionString = configuration["Redis:ConnectionString"]
            ?? configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "NeuroScan:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.Configure<VneidOptions>(configuration.GetSection(VneidOptions.SectionName));
        services.Configure<MockGovOptions>(configuration.GetSection(MockGovOptions.SectionName));

        services.AddSingleton<IOidcSettings, OidcSettings>();
        services.AddSingleton<IOidcTransactionStore, OidcTransactionStore>();
        services.AddSingleton<ISessionManager, DistributedSessionManager>();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IActiveStatusChecker, ActiveStatusChecker>();

        services.AddScoped<IHospitalRepository, HospitalRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddHttpClient<IVneidAuthClient, VneidAuthClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<VneidOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(opts.InternalBaseUrl) ? opts.Issuer : opts.InternalBaseUrl;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient<IMockGovCitizenClient, MockGovCitizenClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<MockGovOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds));
        });

        return services;
    }
}
