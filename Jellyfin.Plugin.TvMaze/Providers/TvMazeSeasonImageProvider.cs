using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using TvMaze.Api.Client;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TV Maze season image provider.
    /// </summary>
    public class TvMazeSeasonImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ITvMazeClient _tvMazeClient;
        private readonly ILogger<TvMazeSeasonImageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeasonImageProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{TvMazeSeasonImageProvider}"/> interface.</param>
        public TvMazeSeasonImageProvider(IHttpClientFactory httpClientFactory, ILogger<TvMazeSeasonImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            // TODO DI.
            _tvMazeClient = new TvMazeClient();
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Season;
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            try
            {
                var season = (Season)item;
                var series = season.Series;

                if (series == null)
                {
                    // Invalid link.
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                if (!season.IndexNumber.HasValue)
                {
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                var imageResults = await GetSeasonImagesInternal(series, season.IndexNumber.Value).ConfigureAwait(false);
                _logger.LogInformation("[GetImages] Images found for {name}: {@images}", item.Name, imageResults);
                return imageResults;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetImages]");
                return Enumerable.Empty<RemoteImageInfo>();
            }
        }

        /// <inheritdoc />
        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
            return await httpClient.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        }

        private async Task<IEnumerable<RemoteImageInfo>> GetSeasonImagesInternal(IHasProviderIds series, int seasonNumber)
        {
            var tvMazeId = TvHelpers.GetTvMazeId(series.ProviderIds);
            if (tvMazeId == null)
            {
                // Requires series tv maze id.
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var tvMazeSeasons = await _tvMazeClient.Shows.GetShowSeasonsAsync(tvMazeId.Value).ConfigureAwait(false);
            if (tvMazeSeasons == null)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var imageResults = new List<RemoteImageInfo>();
            foreach (var tvMazeSeason in tvMazeSeasons)
            {
                if (tvMazeSeason.Number == seasonNumber)
                {
                    if (tvMazeSeason.Image?.Original != null)
                    {
                        imageResults.Add(new RemoteImageInfo
                        {
                            Url = tvMazeSeason.Image.Original,
                            ProviderName = TvMazePlugin.ProviderName,
                            Language = "en",
                            Type = ImageType.Primary
                        });
                    }

                    break;
                }
            }

            return imageResults;
        }
    }
}
