﻿using Nemo.Configuration;
using Nemo.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nemo;

internal static class Identity
{
    internal static IIdentityMap Get(Type objectType, INemoConfiguration configuration)
    {
        var executionContext = configuration?.ExecutionContext ?? ConfigurationFactory.Get(objectType).ExecutionContext;
        IIdentityMap identityMap;
        var identityMapKey = objectType.FullName + "/IdentityMap";
        if (!executionContext.Exists(identityMapKey))
        {
            identityMap = (IIdentityMap)Activator.CreateInstance(typeof(IdentityMap<>).MakeGenericType(objectType));
            executionContext.Set(identityMapKey, identityMap);
        }
        else
        {
            identityMap = (IIdentityMap)executionContext.Get(identityMapKey);
        }
        return identityMap;
    }

    internal static IdentityMap<T> Get<T>(INemoConfiguration configuration)
    {
        configuration ??= ConfigurationFactory.Get<T>();
        var executionContext = configuration.ExecutionContext;
        var identityMapKey = typeof(T).FullName + "/IdentityMap";
        if (!executionContext.TryGet(identityMapKey, out var identityMap))
        {
            identityMap = new IdentityMap<T>();
            executionContext.Set(identityMapKey, identityMap);
        }
        return (IdentityMap<T>)identityMap;
    }

    internal static void WriteThrough<T>(this IIdentityMap identityMap, T value, string hash)
    {
        if (identityMap != null && value != null && hash != null)
        {
            identityMap.Set(hash, value);
        }
    }

    internal static TResult GetEntityByKey<T, TResult>(this IIdentityMap identityMap, Func<SortedDictionary<string, object>> getKey, out string hash)
    {
        hash = null;

        if (identityMap != null)
        {
            var primaryKeyValue = getKey();
            hash = primaryKeyValue.ComputeHash(typeof(T));

            if (identityMap.TryGetValue(hash, out object result))
            {
                return (TResult)result;
            }
        }

        return default(TResult);
    }
}
