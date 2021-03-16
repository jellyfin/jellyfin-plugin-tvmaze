using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.TvMaze.ExternalIds
{
    /// <inheritdoc />
    public class TvMazeSeasonExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TVmaze Season";

        /// <inheritdoc />
        public string Key => TvMazePlugin.ProviderId;

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Season;

        /// <inheritdoc />
        public string UrlFormatString => "http://www.tvmaze.com/seasons/{0}/season";

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item)
        {
            return item is Season;
        }
    }
}