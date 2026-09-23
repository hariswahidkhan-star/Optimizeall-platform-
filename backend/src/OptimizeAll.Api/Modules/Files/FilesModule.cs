namespace OptimizeAll.Api.Modules.Files;

public static class FilesModule
{
    /// <summary>Registers the Files module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddFilesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IFileService, FileService>();
        return services;
    }
}
