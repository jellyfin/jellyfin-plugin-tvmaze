using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using TvMaze.Api.Client;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TV Maze episode provider.
    /// </summary>
    public class TvMazeEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TvMazeEpisodeProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeEpisodeProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of <see cref="ILogger{TvMazeEpisodeProvider}"/>.</param>
        public TvMazeEpisodeProvider(IHttpClientFactory httpClientFactory, ILogger<TvMazeEpisodeProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("[GetSearchResults] Starting for {name} {seasonNumber}x{episodeNumber}", searchInfo.Name, searchInfo.ParentIndexNumber, searchInfo.IndexNumber);
                var results = new List<RemoteSearchResult>();

                var tvMazeId = TvHelpers.GetTvMazeId(searchInfo.SeriesProviderIds);
                if (!tvMazeId.HasValue)
                {
                    // Requires a tv maze id.
                    return results;
                }

                var episode = await GetMetadataInternal(searchInfo).ConfigureAwait(false);
                if (episode != null)
                {
                    results.Add(new RemoteSearchResult
                    {
                        IndexNumber = episode.IndexNumber,
                        Name = episode.Name,
                        ParentIndexNumber = episode.ParentIndexNumber,
                        PremiereDate = episode.PremiereDate,
                        ProductionYear = episode.ProductionYear,
                        ProviderIds = episode.ProviderIds,
                        SearchProviderName = Name,
                        IndexNumberEnd = episode.IndexNumberEnd
                    });
                }

                _logger.LogDebug("[GetSearchResults] Results for {name}: {@episode}", searchInfo.Name, results);
                return results;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetSearchResults]");
                return Enumerable.Empty<RemoteSearchResult>();
            }
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();
            try
            {
                _logger.LogDebug("[GetMetadata] Starting for {name}", info.Name);
                var episode = await GetMetadataInternal(info).ConfigureAwait(false);
                if (episode != null)
                {
                    result.Item = episode;
                    result.HasMetadata = true;
                }

                return result;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetMetadata]");
                return result;
            }
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }

        private async Task<Episode?> GetMetadataInternal(EpisodeInfo info)
        {
            var tvMazeId = TvHelpers.GetTvMazeId(info.SeriesProviderIds);
            if (!tvMazeId.HasValue)
            {
                // Requires a tv maze id.
                return null;
            }

            // The search query must provide an episode number.
            if (!info.IndexNumber.HasValue || !info.ParentIndexNumber.HasValue)
            {
                return null;
            }

            var tvMazeClient = new TvMazeClient(_httpClientFactory.CreateClient(NamedClient.Default));
            var tvMazeEpisode = await tvMazeClient.Shows.GetEpisodeByNumberAsync(tvMazeId.Value, info.ParentIndexNumber.Value, info.IndexNumber.Value).ConfigureAwait(false);
            if (tvMazeEpisode == null)
            {
                // No episode found.
                return null;
            }

            var episode = new Episode
            {
                Name = tvMazeEpisode.Name,
                IndexNumber = tvMazeEpisode.Number,
                ParentIndexNumber = tvMazeEpisode.Season
            };

            if (DateTime.TryParse(tvMazeEpisode.AirDate, out var airDate))
            {
                episode.PremiereDate = airDate;
            }

            if (tvMazeEpisode.Runtime.HasValue)
            {
                episode.RunTimeTicks = TimeSpan.FromTicks(tvMazeEpisode.Runtime.Value).Ticks;
            }

            episode.Overview = TvHelpers.GetStrippedHtml(tvMazeEpisode.Summary);
            episode.SetProviderId(TvMazePlugin.ProviderId, tvMazeEpisode.Id.ToString(CultureInfo.InvariantCulture));

            return episode;
        }
    }
}
