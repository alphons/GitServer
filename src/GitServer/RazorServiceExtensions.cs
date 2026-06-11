namespace GitServer;

public static class RazorServiceExtensions
{
    public static IServiceCollection AddWwwRootRazor(this IServiceCollection services)
    {
        services.AddRazorPages(o => o.RootDirectory = "/wwwroot");
        return services;
    }
}
