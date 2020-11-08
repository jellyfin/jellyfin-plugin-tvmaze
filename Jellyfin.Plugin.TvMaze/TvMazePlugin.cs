using System;
using Jellyfin.Plugin.TvMaze.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.TvMaze
{
    /// <summary>
    /// TvMaze Plugin.
    /// </summary>
    public class TvMazePlugin : BasePlugin<PluginConfiguration>
    {
        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public const string ProviderName = "TV Maze";

        /// <summary>
        /// Gets the provider id.
        /// </summary>
        public const string ProviderId = "TvMaze";

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazePlugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public TvMazePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets current plugin instance.
        /// </summary>
        public static TvMazePlugin? Instance { get; private set; }

        /// <inheritdoc />
        public override string Name => ProviderName;

        /// <inheritdoc />
        public override Guid Id => new Guid("A4A488D0-17A3-4919-8D82-7F3DE4F6B209");
    }
}