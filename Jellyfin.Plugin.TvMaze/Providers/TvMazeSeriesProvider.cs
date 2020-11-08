using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using TvMaze.Api.Client;
using TvMaze.Api.Client.Models;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TV Maze series provider.
    /// </summary>
    public class TvMazeSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
    {
        private readonly IHttpClient _httpClient;
        private readonly ITvMazeClient _tvMazeClient;
        private readonly ILogger<TvMazeSeriesProvider> _logger;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeriesProvider"/> class.
        /// </summary>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        /// <param name="logger">Instance of <see cref="ILogger{TvMazeSeriesProvider}"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        public TvMazeSeriesProvider(
            IHttpClient httpClient,
            ILogger<TvMazeSeriesProvider> logger,
            ILibraryManager libraryManager)
        {
            _httpClient = httpClient;
            _logger = logger;
            _libraryManager = libraryManager;
            // TODO DI.
            _tvMazeClient = new TvMazeClient();
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("[GetSearchResults] Starting for {name}", searchInfo.Name);
                var showSearchResults = (await _tvMazeClient.Search.ShowSearchAsync(searchInfo.Name?.Trim()).ConfigureAwait(false)).ToList();
                _logger.LogDebug("[GetSearchResults] Result count for {name}: {count}", searchInfo.Name, showSearchResults.Count);
                var searchResults = new List<RemoteSearchResult>();
                foreach (var show in showSearchResults)
                {
                    _logger.LogDebug("[GetSearchResults] Result for {name}: {@show}", searchInfo.Name, show);
                    var searchResult = new RemoteSearchResult
                    {
                        Name = show.Show.Name,
                        SearchProviderName = Name,
                        ImageUrl = show.Show.Image?.Original
                    };

                    if (DateTime.TryParse(show.Show.Premiered, out var premiereDate))
                    {
                        searchResult.PremiereDate = premiereDate;
                        searchResult.ProductionYear = premiereDate.Year;
                    }

                    SetProviderIds(show.Show, searchResult);
                    searchResults.Add(searchResult);
                }

                _logger.LogDebug("[GetSearchResults] Result for {name}: {@series}", searchInfo.Name, searchResults);
                return searchResults;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetSearchResults] Error searching for {name}", searchInfo.Name);
                return Enumerable.Empty<RemoteSearchResult>();
            }
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("[GetMetadata] Starting for {name}", info.Name);
                var result = new MetadataResult<Series>();

                var tvMazeId = TvHelpers.GetTvMazeId(info.ProviderIds);
                Show? tvMazeShow;
                if (tvMazeId.HasValue)
                {
                    // Search by tv maze id.
                    tvMazeShow = await _tvMazeClient.Shows.GetShowMainInformation(tvMazeId.Value).ConfigureAwait(false);
                }
                else
                {
                    // Search by name.
                    var parsedName = _libraryManager.ParseName(info.Name);
                    _logger.LogDebug("[GetMetadata] No TV Maze Id, searching by parsed name: {@name}", parsedName);
                    tvMazeShow = await GetIdentifyShow(parsedName).ConfigureAwait(false);
                }

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

                series.Overview = TvHelpers.GetStrippedHtml(tvMazeShow.Summary);
                series.HomePageUrl = tvMazeShow.Url;
                SetProviderIds(tvMazeShow, series);

                result.Item = series;
                result.HasMetadata = true;

                _logger.LogDebug("[GetMetadata] Metadata result: {@series}", tvMazeShow);
                return result;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetMetadata]");
                return new MetadataResult<Series>();
            }
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

        private async Task<Show?> GetIdentifyShow(ItemLookupInfo lookupInfo)
        {
            var searchResults = (await _tvMazeClient.Search.ShowSearchAsync(lookupInfo.Name?.Trim()).ConfigureAwait(false)).ToList();
            if (searchResults.Count == 0)
            {
                // No search results.
                return null;
            }

            if (lookupInfo.Year.HasValue)
            {
                return searchResults.OrderBy(
                        s => DateTime.TryParse(s.Show.Premiered, out var premiereDate) ? Math.Abs(premiereDate.Year - lookupInfo.Year.Value) : 1)
                    .ThenByDescending(s => s.Score)
                    .FirstOrDefault()?.Show;
            }

            return searchResults[0].Show;
        }

        private void SetProviderIds(Show show, IHasProviderIds providerIds)
        {
            providerIds.SetProviderId(TvMazePlugin.ProviderId, show.Id.ToString(CultureInfo.InvariantCulture));

            // Set all provider ids.
            if (!string.IsNullOrEmpty(show.Externals.Imdb))
            {
                providerIds.SetProviderId(MetadataProvider.Imdb.ToString(), show.Externals.Imdb);
            }

            if (show.Externals.TvRage.HasValue)
            {
                providerIds.SetProviderId(MetadataProvider.TvRage.ToString(), show.Externals.TvRage.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (show.Externals.TheTvdb.HasValue)
            {
                providerIds.SetProviderId(MetadataProvider.Tvdb.ToString(), show.Externals.TheTvdb.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}