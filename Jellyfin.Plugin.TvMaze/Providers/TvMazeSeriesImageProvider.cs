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
using TvMaze.Api.Client.Configuration;
using TvMaze.Api.Client.Models;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TVMaze series image provider.
    /// </summary>
    public class TvMazeSeriesImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TvMazeSeriesImageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeriesImageProvider"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{TvMazeSeriesImageProvider}"/> interface.</param>
        public TvMazeSeriesImageProvider(IHttpClientFactory httpClientFactory, ILogger<TvMazeSeriesImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
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
                _logger.LogDebug("[GetImages] {Name}", item.Name);
                var series = (Series)item;
                var tvMazeId = TvHelpers.GetTvMazeId(series.ProviderIds);
                if (tvMazeId == null)
                {
                    // Requires series TVMaze id.
                    _logger.LogWarning("[GetImages] TVMaze id is required");
                    return Enumerable.Empty<RemoteImageInfo>();
                }

                var tvMazeClient = new TvMazeClient(_httpClientFactory.CreateClient(NamedClient.Default), new RetryRateLimitingStrategy());
                var images = await tvMazeClient.Shows.GetShowImagesAsync(tvMazeId.Value).ConfigureAwait(false);
                if (images == null)
                {
                    _logger.LogDebug("[GetImages] No images found");
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

                _logger.LogInformation("[GetImages] Images found for {Name}: {@Images}", item.Name, imageResults);
                return imageResults;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[GetImages]");
                return Enumerable.Empty<RemoteImageInfo>();
            }
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
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
