﻿using Nemo.Attributes;
using Nemo.Fn;
using Nemo.Reflection;
using Nemo.Utilities;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Nemo.Serialization;

public class JsonSerializationWriter
{
    internal delegate void JsonObjectSerializer(JsonSerializationWriter writer, object values, TextWriter output, bool compact);

    private static readonly ConcurrentDictionary<Tuple<string, bool>, JsonObjectSerializer> Serializers = new ConcurrentDictionary<Tuple<string, bool>, JsonObjectSerializer>();

    private readonly Stack<object> _path = new Stack<object>();

    public void Write(string value, TextWriter output)
    {
        output.Write(value);
    }

    private void WriteString(string value, TextWriter output)
    {
        var hexSeqBuffer = new char[4];
        var len = value.Length;
        for (var i = 0; i < len; i++)
        {
            switch (value[i])
            {
                case '\n':
                    output.Write("\\n");
                    continue;

                case '\r':
                    output.Write("\\r");
                    continue;

                case '\t':
                    output.Write("\\t");
                    continue;

                case '"':
                case '\\':
                    output.Write('\\');
                    output.Write(value[i]);
                    continue;

                case '\f':
                    output.Write("\\f");
                    continue;

                case '\b':
                    output.Write("\\b");
                    continue;
            }

            //Is printable char?
            if (value[i] >= 32 && value[i] <= 126)
            {
                output.Write(value[i]);
                continue;
            }

            var isValidSequence = value[i] < 0xD800 || value[i] > 0xDFFF;
            if (isValidSequence)
            {
                // Default, turn into a \uXXXX sequence
                Json.IntegerToHex(value[i], hexSeqBuffer);
                output.Write("\\u");
                output.Write(hexSeqBuffer);
            }
        }
    }

    internal void WriteList(IList items, TextWriter output)
    {
        var lastIndex = items.Count - 1;
        for (int i = 0; i < items.Count; i++)
        {
            WriteObject(items[i], null, output, i != lastIndex);
        }
    }

    internal void WriteList<T>(IList<T> items, TextWriter output)
    {
        WriteList((IList)items, output);
    }

    internal void WriteDictionary(IDictionary map, TextWriter output)
    {
        var lastIndex = map.Count - 1;
        var index = 0;
        foreach (DictionaryEntry pair in map)
        {
            WriteObject(pair.Value, (string)pair.Key, output, index != lastIndex);
            index++;
        }
    }

    internal void WriteDictionary<T>(IDictionary<string, T> map, TextWriter output)
    {
        WriteDictionary((IDictionary)map, output);
    }

    public void WriteObject(object value, string name, TextWriter output, bool hasMore = false, bool compact = true)
    {
        if (value != null)
        {
            var objectType = value.GetType();
            var typeCode = Reflector.GetObjectTypeCode(objectType);
            var isText = false;
            if (typeCode != ObjectTypeCode.DBNull && !_path.Any(p => Equals(p, value)))
            {
                string jsonValue = null;
                switch (typeCode)
                {
                    case ObjectTypeCode.Boolean:
                        jsonValue = ((bool)value).ToString().ToLower();
                        break;
                    case ObjectTypeCode.String:
                        jsonValue = (string)value;
                        isText = true;
                        break;
                    case ObjectTypeCode.DateTime:
                        jsonValue = XmlConvert.ToString((DateTime)value, XmlDateTimeSerializationMode.Utc);
                        isText = true;
                        break;
                    case ObjectTypeCode.Byte:
                    case ObjectTypeCode.UInt16:
                    case ObjectTypeCode.UInt32:
                    case ObjectTypeCode.UInt64:
                    case ObjectTypeCode.SByte:
                    case ObjectTypeCode.Int16:
                    case ObjectTypeCode.Int32:
                    case ObjectTypeCode.Int64:
                    case ObjectTypeCode.Char:
                    case ObjectTypeCode.Single:
                    case ObjectTypeCode.Double:
                    case ObjectTypeCode.Decimal:
                        jsonValue = Convert.ToString(value);
                        break;
                    case ObjectTypeCode.DateTimeOffset:
                        jsonValue = XmlConvert.ToString((DateTimeOffset)value);
                        isText = true;
                        break;
                    case ObjectTypeCode.TimeSpan:

                        jsonValue = XmlConvert.ToString((TimeSpan)value);
                        isText = true;
                        break;
                    case ObjectTypeCode.Guid:

                        jsonValue = ((Guid)value).ToString("D");
                        isText = true;
                        break;
                    case ObjectTypeCode.ObjectMap:
                        var map = (IDictionary)value;
                        Write(name != null ? $"\"{name}\":{{" : "{", output);
                        WriteDictionary(map, output);
                        Write("}", output);
                        if (hasMore)
                        {
                            Write(",", output);
                        }
                        break;
                    case ObjectTypeCode.ObjectList:
                    case ObjectTypeCode.PolymorphicObjectList:
                    {
                        var list = (IList)value;
                        Write(name != null ? $"\"{name}\":[" : "[", output);
                        var isPolymorphicList = typeCode == ObjectTypeCode.PolymorphicObjectList;
                        for (var i = 0; i < list.Count; i++)
                        {
                            var elementType = list[i].GetType();
                            var serializer = CreateDelegate(elementType, isPolymorphicList);
                            serializer(this, list[i], output, compact);
                            if (i < list.Count - 1)
                            {
                                Write(",", output);
                            }
                        }
                        Write("]", output);
                        if (hasMore)
                        {
                            Write(",", output);
                        }
                    }
                        break;
                    case ObjectTypeCode.SimpleList:
                    {
                        var list = (IList)value;
                        Write(name != null ? $"\"{name}\":[" : "[", output);
                        WriteList(list, output);
                        Write("]", output);
                        if (hasMore)
                        {
                            Write(",", output);
                        }
                    }
                        break;
                    case ObjectTypeCode.Object:
                    {
                        var serializer = CreateDelegate(objectType);
                        _path.Push(value);
                        serializer(this, value, output, compact);
                        _path.Pop();
                        if (hasMore)
                        {
                            Write(",", output);
                        }
                    }
                        break;
                }

                if (jsonValue != null)
                {
                    if (name != null)
                    {
                        Write($"\"{name}\":", output);
                    }

                    if (isText)
                    {
                        Write("\"", output);
                    }

                    if (typeCode == ObjectTypeCode.String)
                    {
                        WriteString(jsonValue, output);
                    }
                    else
                    {
                        Write(jsonValue, output);
                    }

                    if (isText)
                    {
                        Write("\"", output);
                    }

                    if (hasMore)
                    {
                        Write(",", output);
                    }
                }
            }
        }
        else if (!compact && !string.IsNullOrEmpty(name))
        {
            Write($"\"{name}\":null", output);
            if (hasMore)
            {
                Write(",", output);
            }
        }
    }

    private static JsonObjectSerializer CreateDelegate(Type objectType, bool isPolymorphic = false)
    {
        var reflectedType = Reflector.GetReflectedType(objectType);
        return Serializers.GetOrAdd(Tuple.Create(reflectedType.InterfaceTypeName ?? reflectedType.FullTypeName, isPolymorphic), k => GenerateDelegate(k.Item1, objectType, k.Item2));
    }

    private static JsonObjectSerializer GenerateDelegate(string key, Type objectType, bool isPolymorphic)
    {
        var method = new DynamicMethod("JsonSerialize_" + key, null, new[] { typeof(JsonSerializationWriter), typeof(object), typeof(TextWriter), typeof(bool) }, typeof(JsonSerializationWriter).Module);
        var il = method.GetILGenerator();

        var writeObject = typeof(JsonSerializationWriter).GetMethod("WriteObject");
        var write = typeof(JsonSerializationWriter).GetMethod("Write");

        var interfaceType = objectType;
        if (Reflector.IsEmitted(objectType))
        {
            interfaceType = Reflector.GetInterface(objectType) ?? objectType;
        }

        var properties = Reflector.GetPropertyMap(interfaceType).Where(p => p.Key.CanRead && p.Key.CanWrite && p.Key.Name != "Indexer" && !p.Key.GetCustomAttributes(typeof(DoNotSerializeAttribute), false).Any()).ToArray();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, write);

        var index = 0;

        if (isPolymorphic)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, $"\"$type\":\"{objectType.FullName},{objectType.Assembly.GetName().Name}\"");
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, write);
        }

        foreach (var property in properties)
        {
            // Write property value
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.EmitCastToReference(interfaceType);
            il.EmitCall(OpCodes.Callvirt, property.Key.GetGetMethod(), null);
            il.BoxIfNeeded(property.Key.PropertyType);
            il.Emit(OpCodes.Ldstr, property.Key.Name);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(++index == properties.Length ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, writeObject);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, write);

        il.Emit(OpCodes.Ret);

        var serializer = (JsonObjectSerializer)method.CreateDelegate(typeof(JsonObjectSerializer));
        return serializer;
    }
}
