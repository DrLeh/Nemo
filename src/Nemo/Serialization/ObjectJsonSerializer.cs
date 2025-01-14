﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nemo.Collections.Extensions;
using Nemo.Utilities;

namespace Nemo.Serialization;

public static class ObjectJsonSerializer
{
    public static string ToJson<T>(this T dataEntity)
        where T : class
    {
        var output = new StringBuilder(1024);
        using (var writer = new StringWriter(output))
        {
            new JsonSerializationWriter().WriteObject(dataEntity, null, writer);
        }
        return output.ToString();
    }

    public static void ToJson<T>(this T dataEntity, TextWriter writer)
        where T : class
    {
        new JsonSerializationWriter().WriteObject(dataEntity, null, writer);
    }

    public static string ToJson<T>(this IEnumerable<T> dataEntitys)
        where T : class
    {
        var output = new StringBuilder(1024);
        using (var writer = new StringWriter(output))
        {
            new JsonSerializationWriter().WriteObject(dataEntitys.ToList(), null, writer);
        }
        return output.ToString();
    }

    public static void ToJson<T>(this IEnumerable<T> dataEntitys, TextWriter writer)
        where T : class
    {
        new JsonSerializationWriter().WriteObject(dataEntitys.ToList(), null, writer);
    }

    public static T FromJson<T>(this string json)
        where T : class
    {
        return (T)json.FromJson(typeof(T));
    }

    public static object FromJson(this string json, Type objectType)
    {
        var value = Json.Parse(json);
        return JsonSerializationReader.ReadObject(value, objectType);
    }
}
