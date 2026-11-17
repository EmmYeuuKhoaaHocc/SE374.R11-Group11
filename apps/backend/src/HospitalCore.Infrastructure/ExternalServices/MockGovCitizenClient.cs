using System.Net;
using System.Net.Http.Json;
using HospitalCore.Application.Common.Interfaces.External;
using Microsoft.Extensions.Logging;

namespace HospitalCore.Infrastructure.ExternalServices;

public class MockGovOptions
{
    public const string SectionName = "MockGov";

    public string BaseUrl { get; set; } = "http://localhost:5001";
    public int TimeoutSeconds { get; set; } = 10;
}

public class MockGovCitizenClient : IMockGovCitizenClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MockGovCitizenClient> _logger;

    public MockGovCitizenClient(HttpClient httpClient, ILogger<MockGovCitizenClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<CitizenVerificationDto?> GetCitizenByCccdAsync(string cccd, CancellationToken ct = default)
    {
        return GetOrDefaultAsync<CitizenVerificationDto>($"/api/v1/c06/citizen/{Uri.EscapeDataString(cccd.Trim())}", ct);
    }

    public Task<PractitionerVerificationDto?> GetPractitionerByCchnAsync(string cchn, CancellationToken ct = default)
    {
        return GetOrDefaultAsync<PractitionerVerificationDto>($"/api/v1/byt/practitioner/{Uri.EscapeDataString(cchn.Trim())}", ct);
    }

    public Task<FacilityVerificationDto?> GetFacilityByCodeAsync(string maBv, CancellationToken ct = default)
    {
        return GetOrDefaultAsync<FacilityVerificationDto>($"/api/v1/byt/facilities/{Uri.EscapeDataString(maBv.Trim())}", ct);
    }

    private async Task<T?> GetOrDefaultAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        var response = await _httpClient.GetAsync(relativeUrl, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("MockGov returned 404 for {Url}", relativeUrl);
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }
}
