using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class ServicesRegistrationExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, Assembly assembly)
        {
            foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Service")))
            {
                var contract = type.GetInterface($"I{type.Name}");
                if (contract is not null)
                {
                    services.AddScoped(contract, type);
                }
            }

            return services;
        }
    }
}
