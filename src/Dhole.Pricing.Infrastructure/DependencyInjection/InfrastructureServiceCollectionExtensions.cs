using CustomCodeFramework.Auth.DependencyInjection;
using CustomCodeFramework.Mongo.DependencyInjection;
using CustomCodeFramework.Redis.DependencyInjection;
using Dhole.DataExtraction.Contracts.Grpc;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Mongo;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Infrastructure.Cache;
using Dhole.Pricing.Infrastructure.GrpcClients;
using Dhole.Pricing.Infrastructure.Mongo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.Pricing.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeAuth(configuration);

        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;

            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        services.AddCustomCodeRedis(configuration);
        services.AddCustomCodeMongo(configuration);

        services.AddPricingCacheServices();
        services.AddPricingMongoSnapshotWriters();
        services.AddPricingDataExtractionGrpcClient(configuration);

        services.AddScoped<ExtractAndPersistFclPricingImportService>();

        return services;
    }

    public static IServiceCollection AddPricingWorkerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeRedis(configuration);
        services.AddCustomCodeMongo(configuration);

        services.AddPricingCacheServices();
        services.AddPricingMongoSnapshotWriters();
        services.AddPricingDataExtractionGrpcClient(configuration);

        services.AddScoped<ExtractAndPersistFclPricingImportService>();

        return services;
    }

    private static IServiceCollection AddPricingCacheServices(this IServiceCollection services)
    {
        services.AddScoped<ICostCacheService, CostCacheService>();

        services.AddScoped<IImportRateCacheService, ImportRateCacheService>();

        services.AddScoped<IRateHeaderCacheService, RateHeaderCacheService>();

        return services;
    }

    private static IServiceCollection AddPricingMongoSnapshotWriters(
        this IServiceCollection services
    )
    {
        services.AddScoped<ICostChangeSnapshotWriter, CostChangeSnapshotWriter>();

        services.AddScoped<IImportFclRateChangeSnapshotWriter, ImportFclRateChangeSnapshotWriter>();

        services.AddScoped<IRateDetailChangeSnapshotWriter, RateDetailChangeSnapshotWriter>();

        services.AddScoped<IRateHeaderChangeSnapshotWriter, RateHeaderChangeSnapshotWriter>();

        return services;
    }

    public static IServiceCollection AddPricingDataExtractionGrpcClient(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var address = GetRequiredDataExtractionAddress(configuration);

        var maxMessageSize = ReadPositiveInt(
            configuration["Grpc:Clients:DataExtraction:MaxMessageSizeBytes"],
            64 * 1024 * 1024
        );

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        services
            .AddGrpcClient<DataExtractionGrpc.DataExtractionGrpcClient>(options =>
            {
                options.Address = address;
            })
            .ConfigureChannel(options =>
            {
                options.MaxReceiveMessageSize = maxMessageSize;
                options.MaxSendMessageSize = maxMessageSize;
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
            );

        services.AddScoped<IDataExtractionFclPricingClient, DataExtractionFclPricingGrpcClient>();

        return services;
    }

    private static Uri GetRequiredDataExtractionAddress(IConfiguration configuration)
    {
        var configuredAddress = configuration["Grpc:Clients:DataExtraction:Address"];

        if (string.IsNullOrWhiteSpace(configuredAddress))
        {
            throw new InvalidOperationException(
                "Debe configurar " + "Grpc:Clients:DataExtraction:Address."
            );
        }

        if (!Uri.TryCreate(configuredAddress, UriKind.Absolute, out var address))
        {
            throw new InvalidOperationException(
                "La configuración "
                    + "Grpc:Clients:DataExtraction:Address "
                    + $"no contiene una URL válida: '{configuredAddress}'."
            );
        }

        return address;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
