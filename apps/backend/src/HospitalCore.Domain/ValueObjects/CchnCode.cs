using HospitalCore.Domain.Common;
using HospitalCore.Domain.Exceptions;

namespace HospitalCore.Domain.ValueObjects;

public sealed class CchnCode : ValueObject
{
    public string Value { get; }

    public CchnCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 5)
        {
            throw new DomainValidationException($"Invalid Medical Practitioner Certificate Code (CCHN): '{value}'.");
        }

        Value = value.Trim().ToUpperInvariant();
    }

    public static implicit operator string(CchnCode cchn) => cchn.Value;
    public static explicit operator CchnCode(string value) => new(value);

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
