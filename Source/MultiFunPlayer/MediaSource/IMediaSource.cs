﻿using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;

namespace MultiFunPlayer.MediaSource;

internal interface IMediaSource : IConnectable, IDisposable
{
    string Name { get; }

    void HandleSettings(JObject settings, SettingsAction action);
}
