namespace HospitalCore.Application.Common.Interfaces.Security;

public record OidcTransaction
{
    public string Nonce { get; init; } = string.Empty;
    public string CodeVerifier { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = "/";
    public string BindingHash { get; init; } = string.Empty;
}

public interface IOidcTransactionStore
{
    Task SaveAsync(string state, OidcTransaction transaction, TimeSpan lifetime, CancellationToken ct = default);
    Task<OidcTransaction?> ConsumeAsync(string state, string? bindingToken, CancellationToken ct = default);
}
