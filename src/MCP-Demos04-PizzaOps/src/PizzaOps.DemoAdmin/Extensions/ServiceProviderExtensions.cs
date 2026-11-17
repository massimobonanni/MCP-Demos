using Microsoft.Extensions.DependencyInjection;

namespace PizzaOps.DemoAdmin.Extensions;

internal static class ServiceProviderExtensions
{
    public static T GetCommand<T>(this IServiceProvider provider) where T : notnull =>
        provider.GetRequiredService<T>();
}
