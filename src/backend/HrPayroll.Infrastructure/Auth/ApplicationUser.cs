using Microsoft.AspNetCore.Identity;

namespace HrPayroll.Infrastructure.Auth;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
