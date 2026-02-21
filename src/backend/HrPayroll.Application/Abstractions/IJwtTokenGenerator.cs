namespace HrPayroll.Application.Abstractions;

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid userId, string email, Guid tenantId, IReadOnlyCollection<string> roles);
}
