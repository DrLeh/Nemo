﻿using System.Diagnostics;
using Nemo.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web;

namespace Nemo.Utilities;

public static class Log
{
    private const string LogContextName = "__LogContext";

    private static bool IsEnabled(INemoConfiguration config, out ILogProvider logProvider)
    {
        logProvider = null;
        if (!(config ?? ConfigurationFactory.DefaultConfiguration).Logging) return false;
        logProvider = GetLogProvider(config);
        return logProvider != null;
    }

    private static ILogProvider GetLogProvider(INemoConfiguration config)
    {
        return (config ?? ConfigurationFactory.DefaultConfiguration).LogProvider;
    }

    public static void Configure(INemoConfiguration config)
    {
        if (IsEnabled(config, out var logProvider))
        {
            logProvider.Configure();
        }
    }

    public static void Configure(string configFile, INemoConfiguration config)
    {
        if (!IsEnabled(config, out var logProvider)) return;

        if (File.Exists(configFile))
        {
            logProvider.Configure(configFile);
        }
    }

    public static void Capture(Exception error, INemoConfiguration config)
    {
        if (!IsEnabled(config, out var logProvider)) return;

        var context = GetContext(config);
        logProvider.Write(error, context.Item1 != Guid.Empty ? context.Item1.ToString() : null);
    }

    public static void Capture(string message, INemoConfiguration config)
    {
        if (!IsEnabled(config, out var logPRovider)) return;

        var context = GetContext(config);
        Capture(message, logPRovider, context);
    }

    private static void Capture(string message, ILogProvider logProvider, Tuple<Guid, Stopwatch> context)
    {
        if (message == null) return;

        if (context.Item1 != Guid.Empty && context.Item2 != null)
        {
            message = $"{context.Item1}: {message}";
        }
        logProvider.Write(message);
    }

    public static bool CaptureBegin(string message, INemoConfiguration config)
    {
        if (!IsEnabled(config, out var logProvider)) return false;

        var context = CreateContext(config);
        Capture(message, logProvider, context);
        context.Item2.Start();
        return true;
    }
    
    public static void CaptureEnd(INemoConfiguration config)
    {
        if (!IsEnabled(config, out var logProvider)) return;

        var context = GetContext(config);
        if (context.Item2 == null) return;
        
        context.Item2.Stop();
        var message = Convert.ToString(context.Item2.Elapsed.TotalMilliseconds, CultureInfo.InvariantCulture);
        Capture(message, logProvider, context);
        ClearContext(config);
    }

    private static Tuple<Guid, Stopwatch> GetContext(INemoConfiguration config)
    {
        if ((config ?? ConfigurationFactory.DefaultConfiguration).ExecutionContext.TryGet(LogContextName, out var context))
        {
            var logContext = (Stack<Tuple<Guid, Stopwatch>>)context;
            if (logContext != null && logContext.Count > 0)
            {
                return logContext.Peek();
            }
        }
        return new Tuple<Guid, Stopwatch>(Guid.Empty, null);
    }

    private static void ClearContext(INemoConfiguration config)
    {
        if ((config ?? ConfigurationFactory.DefaultConfiguration).ExecutionContext.TryGet(LogContextName, out var context))
        {
            var logContext = (Stack<Tuple<Guid, Stopwatch>>)context;
            if (logContext != null && logContext.Count > 0)
            {
                logContext.Pop();
            }
        }
    }

    private static Tuple<Guid, Stopwatch> CreateContext(INemoConfiguration config)
    {
        var executionContext = (config ?? ConfigurationFactory.DefaultConfiguration).ExecutionContext;

        if (!executionContext.TryGet(LogContextName, out var logContext))
        {
            logContext = new Stack<Tuple<Guid, Stopwatch>>();
            executionContext.Set(LogContextName, logContext);
        }

        if (logContext != null)
        {
            var context = Tuple.Create(Guid.NewGuid(), new Stopwatch());
            ((Stack<Tuple<Guid, Stopwatch>>)logContext).Push(context);
            return context;
        }
        return null;
    }
}
