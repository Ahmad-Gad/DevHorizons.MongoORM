namespace DevHorizons.MongoORM
{
    using Connection;

    using Context;

    using Microsoft.Extensions.DependencyInjection;

    using Settings;

    public static class DependencyInjection
    {
        public static IServiceCollection UseMongoDB(this IServiceCollection services, IMongoConnectionSettings connectionSettings)
        {
            if (services is null || connectionSettings is null)
            {
                return services;
            }

            return services.UseMongoDB(connectionSettings, null, false);
        }

        public static IServiceCollection UseMongoDB(this IServiceCollection services, IMongoConnectionSettings connectionSettings, IProvisioningSettings provisioningSettings)
        {
            if (services is null || connectionSettings is null || provisioningSettings is null)
            {
                return services;
            }

            return services.UseMongoDB(connectionSettings, provisioningSettings, true);
        }

        private static IServiceCollection UseMongoDB(this IServiceCollection services, IMongoConnectionSettings connectionSettings, IProvisioningSettings provisioningSettings, bool includeProvisioningSettings)
        {
            services.AddSingleton<IMongoConnectionSettings, MongoConnectionSettings>((s) => { return connectionSettings as MongoConnectionSettings; });

            if (includeProvisioningSettings)
            {
                services.AddSingleton<IProvisioningSettings, ProvisioningSettings>((s) => { return provisioningSettings as ProvisioningSettings; });
            }

            services.AddSingleton<IMongoConnection, MongoConnection>();
            return services;
        }
    }
}
