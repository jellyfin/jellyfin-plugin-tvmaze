using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.TvMaze.ExternalIds
{
    /// <inheritdoc />
    public class TvMazeSeriesExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TVmaze";

        /// <inheritdoc />
        public string Key => TvMazePlugin.ProviderId;

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item)
        {
            return item is Series;
        }
    }
}
