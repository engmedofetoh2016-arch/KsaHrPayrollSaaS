using HrPayroll.Application.Abstractions;

namespace HrPayroll.Infrastructure;

public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
