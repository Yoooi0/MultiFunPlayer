﻿using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.UI;

internal interface IStyletLogger : Stylet.Logging.ILogger
{
    void SuspendLogging();
    void ResumeLogging();
    bool IsLoggingEnabled();
}

internal interface IStyletLoggerManager
{
    IStyletLogger GetLogger(string name);

    void SuspendLogging();
    void ResumeLogging();
    bool IsLoggingEnabled();
}

internal sealed class StyletLoggerManager : IStyletLoggerManager
{
    private readonly ConcurrentDictionary<string, IStyletLogger> _loggers;
    private bool _enabled;

    public StyletLoggerManager()
    {
        _loggers = new ConcurrentDictionary<string, IStyletLogger>();
    }

    public IStyletLogger GetLogger(string name) => _loggers.GetOrAdd(name, name => new NLogStyletLogger(name));

    public void SuspendLogging()
    {
        if (!IsLoggingEnabled())
            return;

        foreach (var (_, logger) in _loggers)
            logger.SuspendLogging();

        _enabled = false;
    }

    public void ResumeLogging()
    {
        if (IsLoggingEnabled())
            return;

        foreach (var (_, logger) in _loggers)
            logger.ResumeLogging();

        _enabled = true;
    }

    public bool IsLoggingEnabled() => _enabled;

    private sealed class NLogStyletLogger(string name) : IStyletLogger
    {
        private readonly Logger _logger = LogManager.GetLogger(name);
        private bool _enabled;

        public void SuspendLogging() => _enabled = false;
        public void ResumeLogging() => _enabled = true;
        public bool IsLoggingEnabled() => _enabled;

        public void Error(Exception exception, string message = null)
        {
            if (IsLoggingEnabled())
                _logger.Error(exception, message);
        }

        public void Info(string format, params object[] args)
        {
            if (IsLoggingEnabled())
                _logger.Info(format, args);
        }

        public void Warn(string format, params object[] args)
        {
            if (IsLoggingEnabled())
                _logger.Warn(format, args);
        }
    }
}
