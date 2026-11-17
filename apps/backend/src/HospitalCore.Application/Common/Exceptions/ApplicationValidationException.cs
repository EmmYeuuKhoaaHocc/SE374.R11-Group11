namespace HospitalCore.Application.Common.Exceptions;

public class ApplicationValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ApplicationValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation failures have occurred.")
    {
        Errors = errors;
    }

    public ApplicationValidationException(string propertyName, string errorMessage)
        : base(errorMessage)
    {
        Errors = new Dictionary<string, string[]>
        {
            [propertyName] = new[] { errorMessage }
        };
    }
}
