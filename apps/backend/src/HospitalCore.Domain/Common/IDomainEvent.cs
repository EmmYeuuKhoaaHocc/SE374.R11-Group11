namespace HospitalCore.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
