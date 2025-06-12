using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TvMaze.ExternalIds;

/// <summary>
/// External url provider for TVmaze.
/// </summary>
public class TvMazeExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => TvMazePlugin.ProviderName;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId(TvMazePlugin.ProviderId, out var externalId))
        {
            switch (item)
            {
                case Series:
                    yield return $"https://www.tvmaze.com/shows/{externalId}";
                    break;
                case Season:
                    yield return $"https://www.tvmaze.com/seasons/{externalId}/season";
                    break;
                case Episode:
                    yield return $"https://www.tvmaze.com/episodes/{externalId}";
                    break;
            }
        }
    }
}
