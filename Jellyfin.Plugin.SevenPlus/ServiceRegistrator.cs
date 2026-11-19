using Jellyfin.Plugin.SevenPlus.Channels;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.SevenPlus;

/// <summary>Registers the channel provider with Jellyfin's service container.</summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddEnumerable(ServiceDescriptor.Singleton<IChannel, SevenPlusChannel>());
    }
}

