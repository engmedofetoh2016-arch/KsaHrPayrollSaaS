namespace HrPayroll.Application.Common;

public static class RoleNames
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Hr = "HR";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    public static readonly string[] All = [Owner, Admin, Hr, Manager, Employee];
}
