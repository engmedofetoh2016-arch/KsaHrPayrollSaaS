namespace HrPayroll.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
