using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.TvMaze.ExternalIds
{
    /// <inheritdoc />
    public class TvMazeEpisodeExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TVmaze";

        /// <inheritdoc />
        public string Key => TvMazePlugin.ProviderId;

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;

        /// <inheritdoc />
        public string UrlFormatString => "http://www.tvmaze.com/episodes/{0}";

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item)
        {
            return item is Episode;
        }
    }
}
