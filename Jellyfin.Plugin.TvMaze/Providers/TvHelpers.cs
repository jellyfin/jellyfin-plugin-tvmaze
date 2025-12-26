using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.TvMaze.Providers;

/// <summary>
/// Search helpers.
/// </summary>
public static class TvHelpers
{
    internal static int? GetTvMazeId(Dictionary<string, string> providerIds)
    {
        if (providerIds.TryGetValue(TvMazePlugin.ProviderId, out var id)
        && !string.IsNullOrEmpty(id))
        {
            return Convert.ToInt32(id, CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <summary>
    /// Strips html tags from input.
    /// </summary>
    /// <param name="input">Input string.</param>
    /// <returns>Stripped input.</returns>
    internal static string? GetStrippedHtml(string? input)
    {
        return input?
            .Replace("<br>", Environment.NewLine, StringComparison.OrdinalIgnoreCase)
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<i>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</i>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<b>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</b>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<li>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<ul>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</ul>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<div>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<em>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<em/>", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
