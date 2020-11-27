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
    /// TV Maze episode image provider.
    /// </summary>
    public class TvMazeEpisodeImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TvMazeEpisodeImageProvider> _logger;
        private readonly ITvMazeClient _tvMazeClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeEpisodeImageProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of <see cref="ILogger{TvMazeEpisodeImageProvider}"/>.</param>
        public TvMazeEpisodeImageProvider(IHttpClientFactory httpClientFactory, ILogger<TvMazeEpisodeImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _tvMazeClient = new TvMazeClient();
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Episode;
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
                _logger.LogDebug("[GetImages] {name}", item.Name);
                var episode = (Episode)item;
                var series = episode.Series;
                if (series == null)
                {
                    // Episode or series is null.
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                var tvMazeId = TvHelpers.GetTvMazeId(episode.Series.ProviderIds);
                if (tvMazeId == null)
                {
                    // Requires series tv maze id.
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                if (episode.IndexNumber == null || episode.ParentIndexNumber == null)
                {
                    // Missing episode or season number.
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                var tvMazeEpisode = await _tvMazeClient.Shows.GetEpisodeByNumberAsync(tvMazeId.Value, episode.ParentIndexNumber.Value, episode.IndexNumber.Value).ConfigureAwait(false);
                if (tvMazeEpisode == null)
                {
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                var imageResults = new List<RemoteImageInfo>();
                if (tvMazeEpisode.Image?.Original != null)
                {
                    imageResults.Add(new RemoteImageInfo
                    {
                        Url = tvMazeEpisode.Image.Original,
                        ProviderName = TvMazePlugin.ProviderName,
                        Language = "en",
                        Type = ImageType.Primary
                    });
                }

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
    }
}
