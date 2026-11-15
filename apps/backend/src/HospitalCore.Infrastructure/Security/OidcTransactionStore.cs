using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Features.Auth.Login;
using Microsoft.Extensions.Caching.Distributed;

namespace HospitalCore.Infrastructure.Security;

public class OidcTransactionStore : IOidcTransactionStore
{
    private const string KeyPrefix = "oidc:tx:";
    private readonly IDistributedCache _cache;

    public OidcTransactionStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public Task SaveAsync(string state, OidcTransaction transaction, TimeSpan lifetime, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(transaction);
        return _cache.SetStringAsync(
            $"{KeyPrefix}{state}",
            json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            ct);
    }

    public async Task<OidcTransaction?> ConsumeAsync(string state, string? bindingToken, CancellationToken ct = default)
    {
        var key = $"{KeyPrefix}{state}";
        var json = await _cache.GetStringAsync(key, ct);
        await _cache.RemoveAsync(key, ct);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var transaction = JsonSerializer.Deserialize<OidcTransaction>(json);
        if (transaction == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(transaction.BindingHash))
        {
            if (string.IsNullOrWhiteSpace(bindingToken))
            {
                return null;
            }

            var actualHash = LoginQueryHandler.ComputeBindingHash(bindingToken);
            var expectedBytes = Encoding.ASCII.GetBytes(transaction.BindingHash);
            var actualBytes = Encoding.ASCII.GetBytes(actualHash);
            if (expectedBytes.Length != actualBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                return null;
            }
        }

        return transaction;
    }
}
