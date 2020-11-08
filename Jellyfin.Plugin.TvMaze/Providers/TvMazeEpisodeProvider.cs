using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using TvMaze.Api.Client;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TV Maze episode provider.
    /// </summary>
    public class TvMazeEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly ITvMazeClient _tvMazeClient;
        private readonly IHttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeEpisodeProvider"/> class.
        /// </summary>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        public TvMazeEpisodeProvider(IHttpClient httpClient)
        {
            // TODO DI.
            _tvMazeClient = new TvMazeClient();
            _httpClient = httpClient;
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var tvMazeId = Helpers.GetTvMazeId(searchInfo.SeriesProviderIds);
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

            return results;
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();
            var episode = await GetMetadataInternal(info).ConfigureAwait(false);
            if (episode != null)
            {
                result.Item = episode;
                result.HasMetadata = true;
            }

            return result;
        }

        /// <inheritdoc />
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }

        private async Task<Episode?> GetMetadataInternal(EpisodeInfo info)
        {
            var tvMazeId = Helpers.GetTvMazeId(info.SeriesProviderIds);
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

            var tvMazeEpisode = await _tvMazeClient.Shows.GetEpisodeByNumberAsync(tvMazeId.Value, info.ParentIndexNumber.Value, info.IndexNumber.Value).ConfigureAwait(false);
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

            episode.Overview = Helpers.StripHtml(tvMazeEpisode.Summary);
            episode.SetProviderId(TvMazePlugin.ProviderId, tvMazeEpisode.Id.ToString(CultureInfo.InvariantCulture));

            return episode;
        }
    }
}