using System.Text.RegularExpressions;
using HospitalCore.Domain.Common;
using HospitalCore.Domain.Exceptions;

namespace HospitalCore.Domain.ValueObjects;

public sealed class CccdNumber : ValueObject
{
    private static readonly Regex CccdRegex = new(@"^\d{12}$", RegexOptions.Compiled);

    public string Value { get; }

    public CccdNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !CccdRegex.IsMatch(value))
        {
            throw new DomainValidationException($"Invalid Vietnamese Citizen ID (CCCD): '{value}'. CCCD must be exactly 12 digits.");
        }

        Value = value;
    }

    public static implicit operator string(CccdNumber cccd) => cccd.Value;
    public static explicit operator CccdNumber(string value) => new(value);

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
