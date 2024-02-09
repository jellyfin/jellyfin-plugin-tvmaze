using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using TvMaze.Api.Client;
using TvMaze.Api.Client.Configuration;
using TvMaze.Api.Client.Models;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using TvMazeEpisode = TvMaze.Api.Client.Models.Episode;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TVMaze episode provider.
    /// </summary>
    public class TvMazeEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private static readonly char[] _normalizedEpisodeNamesIgnoredChars = { ' ', '+', '.', '-', '_' };
        private static readonly Regex _filenameDateRegex = new ("[0-9]{4}-[0-9]{2}-[0-9]{2}");
        private static readonly Regex _filenameIdRegex = new (@"\[([0-9]+)\]");

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
                _logger.LogDebug("[GetSearchResults] Starting for {Name} {SeasonNumber}x{EpisodeNumber}", searchInfo.Name, searchInfo.ParentIndexNumber, searchInfo.IndexNumber);
                var results = new List<RemoteSearchResult>();

                var tvMazeId = TvHelpers.GetTvMazeId(searchInfo.SeriesProviderIds);
                if (!tvMazeId.HasValue)
                {
                    // Requires a TVMaze id.
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

                _logger.LogDebug("[GetSearchResults] Results for {Name}: {@Episode}", searchInfo.Name, results);
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
                _logger.LogDebug("[GetMetadata] Starting for {Name}", info.Name);
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
                // Requires a TVMaze id.
                return null;
            }

            var tvMazeClient = new TvMazeClient(_httpClientFactory.CreateClient(NamedClient.Default), new RetryRateLimitingStrategy());
            // TODO maybe we should cache the last 10 shows or so because it is reasonable to assume that the metadata for multiple episodes of the same show will be requested
            var allEpisodes = (await tvMazeClient.Shows.GetShowEpisodeListAsync(tvMazeId.Value, true).ConfigureAwait(false)).ToArray();
            var possibleEpisodes = allEpisodes;

            var episode = new Episode();
            TvMazeEpisode? tvMazeEpisode = null;

            if (info.ParentIndexNumber.HasValue && info.ParentIndexNumber != 0 && info.IndexNumber.HasValue)
            {
                tvMazeEpisode = possibleEpisodes.FirstOrDefault(e => e.Season == info.ParentIndexNumber && e.Number == info.IndexNumber);

                if (tvMazeEpisode != null)
                {
                    episode.ParentIndexNumber = tvMazeEpisode.Season;
                    episode.IndexNumber = tvMazeEpisode.Number;
                }
            }

            if (tvMazeEpisode == null)
            {
                var filename = Path.GetFileNameWithoutExtension(info.Path);

                var dateMatch = _filenameDateRegex.Match(filename);
                if (dateMatch.Success)
                {
                    possibleEpisodes = possibleEpisodes.Where(e => e.AirDate == dateMatch.Value).ToArray();

                    if (possibleEpisodes.Length == 0)
                    {
                        // Get rid of the date filter because there is no episode found
                        possibleEpisodes = allEpisodes;
                    }
                    else if (possibleEpisodes.Length == 1)
                    {
                        tvMazeEpisode = possibleEpisodes.First();
                    }
                }

                if (tvMazeEpisode == null)
                {
                    var idMatch = _filenameIdRegex.Match(filename);
                    if (idMatch.Success)
                    {
                        tvMazeEpisode = possibleEpisodes.FirstOrDefault(e => e.Id.ToString(CultureInfo.InvariantCulture) == idMatch.Groups[1].Value);
                    }

                    if (tvMazeEpisode == null)
                    {
                        var normalizedFileName = NormalizeEpisodeName(filename);
                        var nameMatchedEpisodes = possibleEpisodes.Where(e => normalizedFileName.Contains(NormalizeEpisodeName(e.Name), StringComparison.CurrentCultureIgnoreCase)).ToArray();
                        if (nameMatchedEpisodes.Length > 0)
                        {
                            possibleEpisodes = nameMatchedEpisodes;
                        }

                        tvMazeEpisode = possibleEpisodes.FirstOrDefault();
                        if (possibleEpisodes.Length > 1)
                        {
                            var potentialEpisodesString = string.Join(", ", possibleEpisodes.Take(10).Select(ep => $"{ep.Name}(ID: {ep.Id})"));
                            var fileNameWithIdExample = tvMazeEpisode!.Name + $"[{tvMazeEpisode.Id}]" + Path.GetExtension(info.Path);
                            _logger.LogWarning("[GetMetadata] Found multiple possible episodes: {possibleEpisodes}. Include the name or the ID of the correct episode in the file name to make the match unique. TVmaze IDs have to be in brackets. (e.G., '{fileNameWithIdExample}')", potentialEpisodesString, fileNameWithIdExample);
                            return null;
                        }
                    }
                }
            }

            if (tvMazeEpisode == null)
            {
                return null;
            }

            switch (tvMazeEpisode.Type)
            {
                case EpisodeType.Regular:
                    episode.ParentIndexNumber = tvMazeEpisode.Season;
                    episode.IndexNumber = tvMazeEpisode.Number;
                    break;
                case EpisodeType.SignificantSpecial:
                case EpisodeType.InsignificantSpecial:
                    episode.ParentIndexNumber = 0;
                    // This way of calculating the index number is not perfectly stable because older specials could be added later but we need some index number to allow sorting by.
                    episode.IndexNumber = allEpisodes
                        .Where(e => e.Type == EpisodeType.InsignificantSpecial || e.Type == EpisodeType.SignificantSpecial)
                        .TakeWhile(e => e.Id != tvMazeEpisode.Id)
                        .Aggregate(1, (i, _) => i + 1);
                    break;
                default:
                    _logger.LogWarning("[GetMetadata] Found unknown episode type '{episodeType}'.", tvMazeEpisode.Type);
                    break;
            }

            if (tvMazeEpisode.Type == EpisodeType.SignificantSpecial)
            {
                SetPositionInSeason(allEpisodes, tvMazeEpisode, episode);
            }

            episode.Name = tvMazeEpisode.Name;

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

        private static void SetPositionInSeason(IReadOnlyList<TvMazeEpisode> allEpisodes, TvMazeEpisode tvMazeEpisode, Episode episode)
        {
            for (int i = 0; i < allEpisodes.Count; i++)
            {
                if (allEpisodes[i].Id != tvMazeEpisode.Id)
                {
                    continue;
                }

                if (i == 0 || allEpisodes[i - 1].Season != tvMazeEpisode.Season)
                {
                    episode.AirsBeforeSeasonNumber = tvMazeEpisode.Season;
                }
                else
                {
                    var firstRegularEpisodeAfter = allEpisodes.Skip(i + 1).SkipWhile(e => e.Type != EpisodeType.Regular).FirstOrDefault();
                    if (firstRegularEpisodeAfter != null && firstRegularEpisodeAfter.Season == tvMazeEpisode.Season)
                    {
                        episode.AirsBeforeSeasonNumber = tvMazeEpisode.Season;
                        episode.AirsBeforeEpisodeNumber = firstRegularEpisodeAfter.Number;
                    }
                    else
                    {
                        episode.AirsAfterSeasonNumber = tvMazeEpisode.Season;
                    }
                }

                return;
            }
        }

        private static string NormalizeEpisodeName(string originalName)
        {
            var normalizedNameBuilder = new StringBuilder();

            foreach (var character in originalName.Where(character => !_normalizedEpisodeNamesIgnoredChars.Contains(character)))
            {
                normalizedNameBuilder.Append(character);
            }

            return normalizedNameBuilder.ToString();
        }
    }
}
