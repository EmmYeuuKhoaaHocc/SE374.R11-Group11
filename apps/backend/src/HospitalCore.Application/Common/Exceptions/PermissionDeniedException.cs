namespace HospitalCore.Application.Common.Exceptions;

public class PermissionDeniedException : Exception
{
    public string Code { get; }

    public PermissionDeniedException(string message, string code = "PERMISSION_DENIED")
        : base(message)
    {
        Code = code;
    }
}
