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
    /// TV Maze Season provider.
    /// </summary>
    public class TvMazeSeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>
    {
        private readonly ITvMazeClient _tvMazeClient;
        private readonly IHttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeasonProvider"/> class.
        /// </summary>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        public TvMazeSeasonProvider(IHttpClient httpClient)
        {
            // TODO DI.
            _tvMazeClient = new TvMazeClient();
            _httpClient = httpClient;
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            var season = await GetSeasonInternal(searchInfo).ConfigureAwait(false);
            if (season != null)
            {
                results.Add(new RemoteSearchResult
                {
                    IndexNumber = season.IndexNumber,
                    Name = season.Name,
                    PremiereDate = season.PremiereDate,
                    ProductionYear = season.ProductionYear,
                    ProviderIds = season.ProviderIds,
                    SearchProviderName = Name
                });
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();

            if (!info.IndexNumber.HasValue)
            {
                // Requires season number.
                return result;
            }

            var season = await GetSeasonInternal(info).ConfigureAwait(false);
            if (season != null)
            {
                result.Item = season;
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

        private async Task<Season?> GetSeasonInternal(SeasonInfo info)
        {
            var tvMazeId = Helpers.GetTvMazeId(info.SeriesProviderIds);
            if (tvMazeId == null)
            {
                // Requires series tv maze id.
                return null;
            }

            var tvMazeSeasons = await _tvMazeClient.Shows.GetShowSeasonsAsync(tvMazeId.Value).ConfigureAwait(false);
            if (tvMazeSeasons == null)
            {
                return null;
            }

            foreach (var tvMazeSeason in tvMazeSeasons)
            {
                if (tvMazeSeason.Number == info.IndexNumber)
                {
                    var season = new Season
                    {
                        Name = tvMazeSeason.Name,
                        IndexNumber = tvMazeSeason.Number
                    };

                    if (DateTime.TryParse(tvMazeSeason.PremiereDate, out var premiereDate))
                    {
                        season.PremiereDate = premiereDate;
                        season.ProductionYear = premiereDate.Year;
                    }

                    season.SetProviderId(TvMazePlugin.ProviderId, tvMazeSeason.Id.ToString(CultureInfo.InvariantCulture));

                    return season;
                }
            }

            // Season not found.
            return null;
        }
    }
}