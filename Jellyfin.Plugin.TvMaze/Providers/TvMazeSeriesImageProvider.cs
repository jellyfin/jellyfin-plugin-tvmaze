using System;
using System.Collections.Generic;
using System.Linq;
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
using TvMaze.Api.Client.Models;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TV Maze series image provider.
    /// </summary>
    public class TvMazeSeriesImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly ITvMazeClient _tvMazeClient;
        private readonly ILogger<TvMazeSeriesImageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeriesImageProvider"/> class.
        /// </summary>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{TvMazeSeriesImageProvider}"/> interface.</param>
        public TvMazeSeriesImageProvider(IHttpClient httpClient, ILogger<TvMazeSeriesImageProvider> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _tvMazeClient = new TvMazeClient();
        }

        /// <inheritdoc />
        public string Name => TvMazePlugin.ProviderName;

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Series;
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
            yield return ImageType.Backdrop;
            yield return ImageType.Banner;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("[GetImages] {name}", item.Name);
                var series = (Series)item;
                var tvMazeId = TvHelpers.GetTvMazeId(series.ProviderIds);
                if (tvMazeId == null)
                {
                    // Requires series tv maze id.
                    _logger.LogWarning("[GetImages] tv maze id is required;");
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                var images = await _tvMazeClient.Shows.GetShowImagesAsync(tvMazeId.Value).ConfigureAwait(false);
                if (images == null)
                {
                    _logger.LogDebug("[GetImages] No images found.");
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                var imageResults = new List<RemoteImageInfo>();
                // Order by type, then by Main=true
                foreach (var image in images.OrderBy(o => o.Type).ThenByDescending(o => o.Main))
                {
                    if (image.Resolutions.Original != null && image.Type.HasValue)
                    {
                        imageResults.Add(new RemoteImageInfo
                        {
                            Url = image.Resolutions.Original.Url,
                            ProviderName = TvMazePlugin.ProviderName,
                            Language = "en",
                            Type = GetImageType(image.Type.Value)
                        });
                    }
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
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }

        private static ImageType GetImageType(ShowImageType showImageType)
        {
            return showImageType switch
            {
                ShowImageType.Poster => ImageType.Primary,
                ShowImageType.Banner => ImageType.Banner,
                ShowImageType.Background => ImageType.Backdrop,
                ShowImageType.Typography => ImageType.Logo,
                _ => throw new ArgumentOutOfRangeException(nameof(showImageType), showImageType, "Unknown ShowImageType")
            };
        }
    }
}