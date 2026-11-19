using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.SevenPlus.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SevenPlus;

/// <summary>Registers the 7plus channel and its dashboard settings.</summary>
public sealed class SevenPlusPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Initializes a new instance of the <see cref="SevenPlusPlugin"/> class.</summary>
    public SevenPlusPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "7plus";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("4a5998a0-18da-4553-95a0-9e95540250b3");

    /// <summary>Gets the running plugin instance.</summary>
    public static SevenPlusPlugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
            },
        ];
    }
}

