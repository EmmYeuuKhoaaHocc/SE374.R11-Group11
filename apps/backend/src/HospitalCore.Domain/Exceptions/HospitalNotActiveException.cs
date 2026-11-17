namespace HospitalCore.Domain.Exceptions;

public class HospitalNotActiveException : Exception
{
    public string MaCskcb { get; }

    public HospitalNotActiveException(string maCskcb)
        : base($"Healthcare facility with code '{maCskcb}' is either not registered or not currently active.")
    {
        MaCskcb = maCskcb;
    }
}
