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
using TvMaze.Api.Client;

namespace Jellyfin.Plugin.TvMaze.Providers
{
    /// <summary>
    /// TV Maze season image provider.
    /// </summary>
    public class TvMazeSeasonImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly ITvMazeClient _tvMazeClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TvMazeSeasonImageProvider"/> class.
        /// </summary>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        public TvMazeSeasonImageProvider(IHttpClient httpClient)
        {
            _httpClient = httpClient;
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

            return await GetSeasonImagesInternal(series, season.IndexNumber.Value).ConfigureAwait(false);
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

        private async Task<IEnumerable<RemoteImageInfo>> GetSeasonImagesInternal(IHasProviderIds series, int seasonNumber)
        {
            var tvMazeId = Helpers.GetTvMazeId(series.ProviderIds);
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