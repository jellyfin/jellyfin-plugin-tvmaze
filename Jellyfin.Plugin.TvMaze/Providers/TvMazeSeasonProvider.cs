using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TvMaze.Api.Client;
using TvMaze.Api.Client.Configuration;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TVMaze Season provider.
    /// </summary>
    public class TvMazeSeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>
    {
        private const string MemoryCachePrefix = "tvmaze_";
        private static readonly TimeSpan _absoluteCacheExpiration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan _slidingCacheExpiration = TimeSpan.FromMinutes(2);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TvMazeSeasonProvider> _logger;
        private readonly IMemoryCache _memoryCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeasonProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{TvMazeSeasonProvider}"/>.</param>
        /// <param name="memoryCache">Instance of <see cref="IMemoryCache"/>.</param>
        public TvMazeSeasonProvider(IHttpClientFactory httpClientFactory, ILogger<TvMazeSeasonProvider> logger, IMemoryCache memoryCache)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _memoryCache = memoryCache;
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            try
            {
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
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetSearchResults]");
                return results;
            }
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();

            try
            {
                if (!info.IndexNumber.HasValue && TvHelpers.GetTvMazeId(info.ProviderIds) == null)
                {
                    // Requires either a season number or a direct TVMaze season ID.
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

        private async Task<Season?> GetSeasonInternal(SeasonInfo info)
        {
            var tvMazeId = TvHelpers.GetTvMazeId(info.SeriesProviderIds);
            if (tvMazeId == null)
            {
                // Requires series TVMaze id.
                return null;
            }

            var directSeasonId = TvHelpers.GetTvMazeId(info.ProviderIds);

            var tvMazeClient = new TvMazeClient(_httpClientFactory.CreateClient(NamedClient.Default), new RetryRateLimitingStrategy());
            var tvMazeSeasons = await _memoryCache.GetOrCreateAsync($"{MemoryCachePrefix}_show_seasons_{tvMazeId.Value}", async entry =>
            {
                entry
                    .SetAbsoluteExpiration(_absoluteCacheExpiration)
                    .SetSlidingExpiration(_slidingCacheExpiration);
                return await tvMazeClient.Shows.GetShowSeasonsAsync(tvMazeId.Value).ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (tvMazeSeasons == null)
            {
                return null;
            }

            foreach (var tvMazeSeason in tvMazeSeasons)
            {
                var isMatch = (directSeasonId.HasValue && tvMazeSeason.Id == directSeasonId.Value)
                    || tvMazeSeason.Number == info.IndexNumber;

                if (isMatch)
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
