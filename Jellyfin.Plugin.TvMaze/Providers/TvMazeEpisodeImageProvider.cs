using System;
using System.Collections.Generic;
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

namespace Jellyfin.Plugin.TvMaze.Providers;

/// <summary>
/// TVMaze episode image provider.
/// </summary>
public class TvMazeEpisodeImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TvMazeEpisodeImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeEpisodeImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of <see cref="ILogger{TvMazeEpisodeImageProvider}"/>.</param>
    public TvMazeEpisodeImageProvider(IHttpClientFactory httpClientFactory, ILogger<TvMazeEpisodeImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
            _logger.LogDebug("[GetImages] {Name}", item.Name);
            var episode = (Episode)item;
            var series = episode.Series;
            if (series is null)
            {
                // Episode or series is null.
                return [];
            }

            var tvMazeId = TvHelpers.GetTvMazeId(episode.ProviderIds);
            if (tvMazeId is null)
            {
                // Requires episode TVMaze id.
                return [];
            }

            var tvMazeClient = new TvMazeClient(_httpClientFactory.CreateClient(NamedClient.Default), new RetryRateLimitingStrategy());
            var tvMazeEpisode = await tvMazeClient.Episodes.GetEpisodeMainInformationAsync(tvMazeId.Value).ConfigureAwait(false);
            if (tvMazeEpisode is null)
            {
                return [];
            }

            var imageResults = new List<RemoteImageInfo>();
            if (tvMazeEpisode.Image?.Original is not null)
            {
                imageResults.Add(new RemoteImageInfo
                {
                    Url = tvMazeEpisode.Image.Original,
                    ProviderName = TvMazePlugin.ProviderName,
                    Language = "en",
                    Type = ImageType.Primary
                });
            }

            _logger.LogInformation("[GetImages] Images found for {Name}: {@Images}", item.Name, imageResults);
            return imageResults;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[GetImages]");
            return [];
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
    }
}
