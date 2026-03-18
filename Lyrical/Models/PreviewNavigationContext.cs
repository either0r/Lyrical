using System;

namespace Lyrical.Models;

public class PreviewNavigationContext
{
    public SongDocument Song { get; init; } = SongDocument.CreateNew();

    public bool ShowBackButton { get; init; } = true;

    public Action? CloseAction { get; init; }
}
