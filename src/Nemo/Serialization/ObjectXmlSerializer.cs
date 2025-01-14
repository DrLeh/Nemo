﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Nemo.Attributes;
using Nemo.Collections;
using Nemo.Collections.Extensions;
using Nemo.Extensions;
using Nemo.Fn;
using Nemo.Reflection;
using Nemo.Utilities;

namespace Nemo.Serialization;

public static class ObjectXmlSerializer
{
    /// <summary>
    /// ToXml method provides an ability to convert an object to XML string. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="dataEntity"></param>
    /// <returns></returns>
    private static void ToXml<T>(this T dataEntity, string documentElement, TextWriter output, bool addSchemaDeclaration)
        where T : class
    {
        var documentElementName = documentElement ?? Xml.GetElementNameFromType<T>();
        new XmlSerializationWriter().WriteObject(dataEntity, documentElementName, output, addSchemaDeclaration);
    }

    private static string ToXml<T>(this T dataEntity, string documentElement, bool addSchemaDeclaration)
        where T : class
    {
        var output = new StringBuilder(1024);
        using (var writer = new StringWriter(output))
        {
            dataEntity.ToXml(documentElement, writer, addSchemaDeclaration);
        }
        return output.ToString();
    }

    public static string ToXml<T>(this T dataEntity)
        where T : class
    {
        return dataEntity.ToXml(null, true);
    }

    public static void ToXml<T>(this T dataEntity, TextWriter writer)
        where T : class
    {
        dataEntity.ToXml(null, writer, true);
    }

    public static string ToXml<T>(this IEnumerable<T> dataEntitys)
       where T : class
    {
        var output = new StringBuilder(1024);
        using (var writer = new StringWriter(output))
        {
            dataEntitys.ToXml(writer);
        }
        return output.ToString();
    }

    public static void ToXml<T>(this IEnumerable<T> dataEntitys, TextWriter writer)
        where T : class
    {
        var documentElementName = string.Empty;
        var addSchemaDeclaration = true;

        foreach (var dataEntity in dataEntitys)
        {
            if (string.IsNullOrEmpty(documentElementName))
            {
                documentElementName = Xml.GetElementNameFromType<T>();
                if (!string.IsNullOrEmpty(documentElementName))
                {
                    writer.Write("<ArrayOf{0} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">", documentElementName);
                    addSchemaDeclaration = false;
                }
            }
            dataEntity.ToXml(null, addSchemaDeclaration);
        }

        if (!string.IsNullOrEmpty(documentElementName))
        {
            writer.Write("</ArrayOf{0}>", documentElementName);
        }
    }

    public static T FromXml<T>(this string xml)
        where T : class
    {
        return FromXml<T>(new StringReader(xml));
    }

    public static T FromXml<T>(this Stream stream)
        where T : class
    {
        using (var reader = new StreamReader(stream))
        {
            return FromXml<T>(reader);
        }
    }

    public static T FromXml<T>(this TextReader textReader)
       where T : class
    {
        using (var reader = XmlReader.Create(textReader))
        {
            return FromXml<T>(reader);
        }
    }

    public static T FromXml<T>(this XmlReader reader)
        where T : class
    {
        return (T)FromXml(reader, typeof(T));
    }

    public static object FromXml(this string xml, Type objectType)
    {
        return FromXml(new StringReader(xml), objectType);
    }

    public static object FromXml(this Stream stream, Type objectType)
    {
        using (var reader = new StreamReader(stream))
        {
            return FromXml(reader, objectType);
        }
    }

    public static object FromXml(this TextReader textReader, Type objectType)
    {
        using (var reader = XmlReader.Create(textReader))
        {
            return FromXml(reader, objectType);
        }
    }

    public static object FromXml(this XmlReader reader, Type objectType)
    {
        bool isArray;
        var result = XmlSerializationReader.ReadObject(reader, objectType, out isArray);
        return result;
    }
}
