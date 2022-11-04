﻿namespace MultiFunPlayer.MediaSource.MediaResource.Modifier;

internal interface IMediaPathModifier
{
    string Name { get; }
    string Description { get; }

    bool Process(ref string path);
}
