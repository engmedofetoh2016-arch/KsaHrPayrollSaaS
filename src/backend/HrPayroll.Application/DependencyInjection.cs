using Microsoft.Extensions.DependencyInjection;

namespace HrPayroll.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
