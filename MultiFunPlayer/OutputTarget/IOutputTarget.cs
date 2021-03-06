﻿using MultiFunPlayer.Common;
using System;

namespace MultiFunPlayer.OutputTarget
{
    public interface IOutputTarget : IConnectable, IDisposable
    {
        string Name { get; }
        bool ContentVisible { get; set; }
    }
}
