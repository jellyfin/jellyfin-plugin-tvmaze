using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using TvMaze.Api.Client;
using TvMaze.Api.Client.Configuration;
using TvMaze.Api.Client.Models;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TVMaze series provider.
    /// </summary>
    public class TvMazeSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TvMazeSeriesProvider> _logger;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeriesProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of <see cref="ILogger{TvMazeSeriesProvider}"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        public TvMazeSeriesProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<TvMazeSeriesProvider> logger,
            ILibraryManager libraryManager)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _libraryManager = libraryManager;
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("[GetSearchResults] Starting for {Name}", searchInfo.Name);
                var tvMazeClient = new TvMazeClient(_httpClientFactory.CreateClient(NamedClient.Default), new RetryRateLimitingStrategy());
                var showSearchResults = (await tvMazeClient.Search.ShowSearchAsync(searchInfo.Name?.Trim() ?? string.Empty).ConfigureAwait(false)).ToList();
                _logger.LogDebug("[GetSearchResults] Result count for {Name}: {Count}", searchInfo.Name, showSearchResults.Count);
                var searchResults = new List<RemoteSearchResult>();
                foreach (var show in showSearchResults)
                {
                    _logger.LogDebug("[GetSearchResults] Result for {Name}: {@Show}", searchInfo.Name, show);
                    if (show.Show == null)
                    {
                        continue;
                    }

                    var searchResult = new RemoteSearchResult
                    {
                        Name = show.Show.Name,
                        SearchProviderName = Name,
                        ImageUrl = show.Show.Image?.Original
                    };

                    if (show.Show.Premiered is not null)
                    {
                        var premiereDate = show.Show.Premiered.Value;
                        searchResult.PremiereDate = premiereDate;
                        searchResult.ProductionYear = premiereDate.Year;
                    }

                    SetProviderIds(show.Show, searchResult);
                    searchResults.Add(searchResult);
                }

                _logger.LogDebug("[GetSearchResults] Result for {Name}: {@Series}", searchInfo.Name, searchResults);
                return searchResults;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetSearchResults] Error searching for {Name}", searchInfo.Name);
                return Enumerable.Empty<RemoteSearchResult>();
            }
        }

        /// <inheritdoc />
        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("[GetMetadata] Starting for {Name}", info.Name);
                var tvMazeClient = new TvMazeClient(_httpClientFactory.CreateClient(NamedClient.Default), new RetryRateLimitingStrategy());
                var result = new MetadataResult<Series>();

                var tvMazeId = TvHelpers.GetTvMazeId(info.ProviderIds);
                Show? tvMazeShow = null;
                if (tvMazeId is not null)
                {
                    // Search by TVMaze id.
                    tvMazeShow = await tvMazeClient.Shows.GetShowMainInformationAsync(tvMazeId.Value).ConfigureAwait(false);
                }

                if (tvMazeShow == null
                    && info.ProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out var imdbId)
                    && !string.IsNullOrEmpty(imdbId))
                {
                    // Lookup by imdb id.
                    tvMazeShow = await tvMazeClient.Lookup.GetShowByImdbIdAsync(imdbId).ConfigureAwait(false);
                }

                if (tvMazeShow == null
                    && info.ProviderIds.TryGetValue(MetadataProvider.TvRage.ToString(), out var tvRageId)
                    && !string.IsNullOrEmpty(tvRageId))
                {
                    // Lookup by tv rage id.
                    var id = Convert.ToInt32(tvRageId, CultureInfo.InvariantCulture);
                    tvMazeShow = await tvMazeClient.Lookup.GetShowByTvRageIdAsync(id).ConfigureAwait(false);
                }

                if (tvMazeShow == null
                    && info.ProviderIds.TryGetValue(MetadataProvider.Tvdb.ToString(), out var tvdbId)
                    && !string.IsNullOrEmpty(tvdbId))
                {
                    var id = Convert.ToInt32(tvdbId, CultureInfo.InvariantCulture);
                    tvMazeShow = await tvMazeClient.Lookup.GetShowByTheTvdbIdAsync(id).ConfigureAwait(false);
                }

                if (tvMazeShow == null)
                {
                    // Series still not found, search by name.
                    var parsedName = _libraryManager.ParseName(info.Name);
                    _logger.LogDebug("[GetMetadata] No TVMaze Id, searching by parsed name: {@Name}", parsedName);
                    tvMazeShow = await GetIdentifyShow(parsedName, tvMazeClient).ConfigureAwait(false);
                }

                if (tvMazeShow == null)
                {
                    // Invalid TVMaze id.
                    _logger.LogDebug("[GetMetadata] No TVMaze result found for {Name}", info.Name);
                    return result;
                }

                var series = new Series();
                series.Name = tvMazeShow.Name;
                series.Genres = tvMazeShow.Genres?.ToArray() ?? Array.Empty<string>();

                if (!string.IsNullOrWhiteSpace(tvMazeShow.Network?.Name))
                {
                    var networkName = tvMazeShow.Network.Name;
                    if (!string.IsNullOrWhiteSpace(tvMazeShow.Network?.Country?.Code))
                    {
                        networkName = $"{tvMazeShow.Network.Name} ({tvMazeShow.Network.Country.Code})";
                    }

                    series.Studios = new[] { networkName };
                }

                if (tvMazeShow.Premiered is not null)
                {
                    series.PremiereDate = tvMazeShow.Premiered.Value;
                    series.ProductionYear = tvMazeShow.Premiered.Value.Year;
                }

                if (tvMazeShow.Rating?.Average != null)
                {
                    series.CommunityRating = (float?)tvMazeShow.Rating.Average;
                }

                if (tvMazeShow.Runtime is not null)
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

                // Set cast.
                var castMembers = await tvMazeClient.Shows.GetShowCastAsync(tvMazeShow.Id).ConfigureAwait(false);
                foreach (var castMember in castMembers)
                {
                    if (castMember.Person == null)
                    {
                        continue;
                    }

                    var personInfo = new PersonInfo();
                    personInfo.SetProviderId(TvMazePlugin.ProviderId, castMember.Person.Id.ToString(CultureInfo.InvariantCulture));
                    personInfo.Name = castMember.Person.Name;
                    personInfo.Role = castMember.Character?.Name;
                    personInfo.Type = PersonKind.Actor;
                    personInfo.ImageUrl = castMember.Person.Image?.Original
                                          ?? castMember.Person.Image?.Medium;

                    result.AddPerson(personInfo);
                }

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
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }

        private async Task<Show?> GetIdentifyShow(ItemLookupInfo lookupInfo, TvMazeClient tvMazeClient)
        {
            var searchResults = (await tvMazeClient.Search.ShowSearchAsync(lookupInfo.Name?.Trim() ?? string.Empty).ConfigureAwait(false)).ToList();
            if (searchResults.Count == 0)
            {
                // No search results.
                return null;
            }

            if (lookupInfo.Year is not null)
            {
                return searchResults.OrderBy(
                        s => s.Show?.Premiered is not null ? Math.Abs(s.Show.Premiered.Value.Year - lookupInfo.Year.Value) : 1)
                    .ThenByDescending(s => s.Score)
                    .FirstOrDefault()?.Show;
            }

            return searchResults[0].Show;
        }

        private void SetProviderIds(Show show, IHasProviderIds providerIds)
        {
            providerIds.SetProviderId(TvMazePlugin.ProviderId, show.Id.ToString(CultureInfo.InvariantCulture));

            // Set all provider ids.
            if (!string.IsNullOrEmpty(show.Externals?.Imdb))
            {
                providerIds.SetProviderId(MetadataProvider.Imdb.ToString(), show.Externals.Imdb);
            }

            if (show.Externals?.TvRage is not null)
            {
                providerIds.SetProviderId(MetadataProvider.TvRage.ToString(), show.Externals.TvRage.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (show.Externals?.TheTvdb is not null)
            {
                providerIds.SetProviderId(MetadataProvider.Tvdb.ToString(), show.Externals.TheTvdb.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
