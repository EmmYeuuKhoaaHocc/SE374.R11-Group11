using System.Text.Json.Serialization;

namespace HospitalCore.Application.Common.Interfaces.External;

public record CitizenVerificationDto
{
    [JsonPropertyName("cccd")]
    public string Cccd { get; init; } = string.Empty;

    [JsonPropertyName("ho_va_ten")]
    public string HoVaTen { get; init; } = string.Empty;

    [JsonPropertyName("ngay_sinh")]
    public string NgaySinh { get; init; } = string.Empty;

    [JsonPropertyName("gioi_tinh")]
    public string GioiTinh { get; init; } = string.Empty;

    [JsonPropertyName("so_the_bhyt")]
    public string? SoTheBhyt { get; init; }

    [JsonPropertyName("dia_chi_thuong_tru")]
    public string? DiaChiThuongTru { get; init; }

    [JsonPropertyName("muc_huong_bhyt")]
    public int? MucHuongBhyt { get; init; }
}

public record PractitionerVerificationDto
{
    [JsonPropertyName("ma_cchn")]
    public string MaCchn { get; init; } = string.Empty;

    [JsonPropertyName("cccd")]
    public string? Cccd { get; init; }

    [JsonPropertyName("ho_va_ten")]
    public string HoVaTen { get; init; } = string.Empty;

    [JsonPropertyName("ma_cskcb_cong_tac")]
    public string MaCskcbCongTac { get; init; } = string.Empty;

    [JsonPropertyName("pham_vi_hoat_dong")]
    public string PhamViHoatDong { get; init; } = string.Empty;

    [JsonPropertyName("trang_thai")]
    public string TrangThai { get; init; } = string.Empty;
}

public record FacilityVerificationDto
{
    [JsonPropertyName("ma_bv")]
    public string MaBv { get; init; } = string.Empty;

    [JsonPropertyName("ten_bv")]
    public string TenBv { get; init; } = string.Empty;

    [JsonPropertyName("dia_chi")]
    public string DiaChi { get; init; } = string.Empty;

    [JsonPropertyName("hang_bv")]
    public string? HangBv { get; init; }
}

public interface IMockGovCitizenClient
{
    Task<CitizenVerificationDto?> GetCitizenByCccdAsync(string cccd, CancellationToken ct = default);
    Task<PractitionerVerificationDto?> GetPractitionerByCchnAsync(string cchn, CancellationToken ct = default);
    Task<FacilityVerificationDto?> GetFacilityByCodeAsync(string maBv, CancellationToken ct = default);
}
