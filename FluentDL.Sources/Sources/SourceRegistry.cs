using System.Diagnostics.CodeAnalysis;
using FluentDL.Contracts.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace FluentDL.Services.Sources;

/// <summary>Finds a registered source by id and role. Sources are listed in catalog order.</summary>
public sealed class SourceRegistry
{
    private readonly IReadOnlyList<ISource> sources;

    public SourceRegistry(IEnumerable<ISource> sources)
    {
        var order = Core.Sources.SourceCatalog.All.Select(d => d.Id).ToList();
        this.sources = sources.OrderBy(s => order.IndexOf(s.Descriptor.Id)).ToList();
    }

    public bool TryGet<TRole>(string id, [NotNullWhen(true)] out TRole? role) where TRole : class, ISource
    {
        role = sources.OfType<TRole>().FirstOrDefault(s => s.Descriptor.Id == id);
        return role != null;
    }

    public IEnumerable<TRole> All<TRole>() where TRole : class, ISource => sources.OfType<TRole>();

    /// <summary>The source that imports this link, or null when the text isn't a supported link.</summary>
    public ILinkSource? FindLinkHandler(string url) => All<ILinkSource>().FirstOrDefault(s => s.CanHandle(url));
}

public static class SourceRegistration
{
    /// <summary>Registers a source as a singleton. The registry picks up every role it implements.</summary>
    public static IServiceCollection AddSource<T>(this IServiceCollection services) where T : class, ISource
    {
        services.AddSingleton<T>();
        services.AddSingleton<ISource>(provider => provider.GetRequiredService<T>());
        return services;
    }
}
