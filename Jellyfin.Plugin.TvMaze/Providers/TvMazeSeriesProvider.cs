using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// TV Maze series provider.
    /// </summary>
    public class TvMazeSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
    {
        private readonly IHttpClient _httpClient;
        private readonly ITvMazeClient _tvMazeClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeriesProvider"/> class.
        /// </summary>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        public TvMazeSeriesProvider(IHttpClient httpClient)
        {
            _httpClient = httpClient;
            // TODO DI.
            _tvMazeClient = new TvMazeClient();
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var show = await _tvMazeClient.Search.ShowSingleSearchAsync($"q={searchInfo.Name}").ConfigureAwait(false);

            // No results
            if (show == null)
            {
                return Enumerable.Empty<RemoteSearchResult>();
            }

            var searchResult = new RemoteSearchResult
            {
                Name = show.Show.Name,
                SearchProviderName = Name
            };

            if (DateTime.TryParse(show.Show.Premiered, out var premiereDate))
            {
                searchResult.PremiereDate = premiereDate;
                searchResult.ProductionYear = premiereDate.Year;
            }

            // Set all provider ids.
            if (!string.IsNullOrEmpty(show.Show.Externals.Imdb))
            {
                searchResult.SetProviderId(MetadataProvider.Imdb.ToString(), show.Show.Externals.Imdb);
            }

            if (show.Show.Externals.TvRage.HasValue)
            {
                searchResult.SetProviderId(MetadataProvider.TvRage.ToString(), show.Show.Externals.TvRage.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (show.Show.Externals.TheTvdb.HasValue)
            {
                searchResult.SetProviderId(MetadataProvider.Tvdb.ToString(), show.Show.Externals.TheTvdb.Value.ToString(CultureInfo.InvariantCulture));
            }

            return new[]
            {
                searchResult
            };
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();

            var tvMazeId = Helpers.GetTvMazeId(info.ProviderIds);
            if (!tvMazeId.HasValue)
            {
                // Requires a tv maze id.
                return result;
            }

            var tvMazeShow = await _tvMazeClient.Shows.GetShowMainInformation(tvMazeId.Value).ConfigureAwait(false);
            if (tvMazeShow == null)
            {
                // Invalid tv maze id.
                return result;
            }

            var series = new Series();
            series.Name = tvMazeShow.Name;
            series.Genres = tvMazeShow.Genres.ToArray();

            if (!string.IsNullOrWhiteSpace(tvMazeShow.Network?.Name))
            {
                var networkName = tvMazeShow.Network.Name;
                if (!string.IsNullOrWhiteSpace(tvMazeShow.Network?.Country?.Code))
                {
                    networkName = $"{tvMazeShow.Network.Name} ({tvMazeShow.Network.Country.Code})";
                }

                series.Studios = new[] { networkName };
            }

            if (DateTime.TryParse(tvMazeShow.Premiered, out var premiereDate))
            {
                series.PremiereDate = premiereDate;
                series.ProductionYear = premiereDate.Year;
            }

            if (tvMazeShow.Rating?.Average != null)
            {
                series.CommunityRating = (float?)tvMazeShow.Rating.Average;
            }

            if (tvMazeShow.Runtime.HasValue)
            {
                series.RunTimeTicks = TimeSpan.FromMinutes(tvMazeShow.Runtime.Value).Ticks;
            }

            if (string.Equals(tvMazeShow.Status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                series.Status = SeriesStatus.Continuing;
            }
            else if (string.Equals(tvMazeShow.Status, "Ended", StringComparison.OrdinalIgnoreCase))
            {
                series.Status = SeriesStatus.Ended;
            }

            series.Overview = Helpers.StripHtml(tvMazeShow.Summary);
            series.HomePageUrl = tvMazeShow.Url;
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
    }
}