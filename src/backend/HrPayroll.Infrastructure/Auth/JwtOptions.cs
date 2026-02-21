namespace HrPayroll.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "HrPayrollSaaS";
    public string Audience { get; set; } = "HrPayrollSaaS.Client";
    public string Key { get; set; } = "THIS_IS_A_DEVELOPMENT_ONLY_KEY_CHANGE_ME_12345";
    public int ExpiryMinutes { get; set; } = 120;
}
