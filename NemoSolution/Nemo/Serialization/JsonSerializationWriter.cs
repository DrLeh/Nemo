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

namespace Nemo.Serialization
{
    public static class JsonSerializationWriter
    {
        internal delegate void JsonObjectSerializer(object values, TextWriter output, bool compact);

        private static readonly ConcurrentDictionary<Tuple<string, bool>, JsonObjectSerializer> _serializers = new ConcurrentDictionary<Tuple<string, bool>, JsonObjectSerializer>();

        public static void Write(string value, TextWriter output)
        {
            output.Write(value);
        }

        private static void WriteString(string value, TextWriter output)
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

        internal static void WriteList(IList items, TextWriter output)
        {
            var lastIndex = items.Count - 1;
            for (int i = 0; i < items.Count; i++)
            {
                WriteObject(items[i], null, output, i != lastIndex);
            }
        }

        internal static void WriteList<T>(IList<T> items, TextWriter output)
        {
            WriteList((IList)items, output);
        }

        internal static void WriteDictionary(IDictionary map, TextWriter output)
        {
            var lastIndex = map.Count - 1;
            var index = 0;
            foreach (DictionaryEntry pair in map)
            {
                WriteObject(pair.Value, (string)pair.Key, output, index != lastIndex);
                index++;
            }
        }

        internal static void WriteDictionary<T>(IDictionary<string, T> map, TextWriter output)
        {
            WriteDictionary((IDictionary)map, output);
        }

        public static void WriteObject(object value, string name, TextWriter output, bool hasMore = false, bool compact = true)
        {
            if (value != null)
            {
                var objectType = value.GetType();
                var typeCode = Type.GetTypeCode(objectType);
                var isText = false;
                if (typeCode != TypeCode.DBNull)
                {
                    string jsonValue = null;
                    switch (typeCode)
                    {
                        case TypeCode.Boolean:
                            jsonValue = ((bool)value).ToString().ToLower();
                            break;
                        case TypeCode.String:
                            jsonValue = (string)value;
                            isText = true;
                            break;
                        case TypeCode.DateTime:
                            jsonValue = XmlConvert.ToString((DateTime)value, XmlDateTimeSerializationMode.Utc);
                            isText = true;
                            break;
                        case TypeCode.Byte:
                        case TypeCode.UInt16:
                        case TypeCode.UInt32:
                        case TypeCode.UInt64:
                        case TypeCode.SByte:
                        case TypeCode.Int16:
                        case TypeCode.Int32:
                        case TypeCode.Int64:
                        case TypeCode.Char:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            jsonValue = Convert.ToString(value);
                            break;
                        default:
                            if (objectType == typeof(DateTimeOffset))
                            {
                                jsonValue = XmlConvert.ToString((DateTimeOffset)value);
                                isText = true;
                            }
                            else if (objectType == typeof(TimeSpan))
                            {
                                jsonValue = XmlConvert.ToString((TimeSpan)value);
                                isText = true;
                            }
                            else if (objectType == typeof(Guid))
                            {
                                jsonValue = ((Guid)value).ToString("D");
                                isText = true;
                            }
                            else
                            {
                                var map = value as IDictionary;
                                if (map != null)
                                {
                                    Write(name != null ? string.Format("\"{0}\":{", name) : "{", output);
                                    WriteDictionary(map, output);
                                    Write("}", output);
                                    if (hasMore)
                                    {
                                        Write(",", output);
                                    }
                                }
                                else
                                {
                                    var list = value as IList;
                                    if (list != null)
                                    {
                                        Write(name != null ? string.Format("\"{0}\":[", name) : "[", output);
                                        var items = list.Cast<object>().ToArray();
                                        if (items.Length > 0 && (items[0] is IDataEntity || !Reflector.IsSimpleType(items[0].GetType())))
                                        {
                                            var args = list.GetType().GetGenericArguments();
                                            var isPolymorphicList = args.Length == 1 && args[0].IsAbstract && !args[0].IsInterface;
                                            for (var i = 0; i < items.Length; i++)
                                            {
                                                var elementType = items[i].GetType();
                                                var serializer = CreateDelegate(elementType, isPolymorphicList);
                                                serializer(items[i], output, compact);
                                                if (i < items.Length - 1)
                                                {
                                                    Write(",", output);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            WriteList(list, output);
                                        }
                                        Write("]", output);
                                        if (hasMore)
                                        {
                                            Write(",", output);
                                        }
                                    }
                                    else 
                                    {
                                        var serializer = CreateDelegate(objectType);
                                        serializer(value, output, compact);
                                        if (hasMore)
                                        {
                                            Write(",", output);
                                        }
                                    }
                                }
                            }
                            break;
                    }

                    if (jsonValue != null)
                    {
                        if (name != null)
                        {
                            Write(string.Format("\"{0}\":", name), output);
                        }

                        if (isText)
                        {
                            Write(string.Format("\"", name), output);
                        }

                        if (typeCode == TypeCode.String)
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
                Write(string.Format("\"{0}\":null", name), output);
                if (hasMore)
                {
                    Write(",", output);
                }
            }
        }

        private static JsonObjectSerializer CreateDelegate(Type objectType, bool isPolymorphic = false)
        {
            var reflectedType = Reflector.GetReflectedType(objectType);
            return _serializers.GetOrAdd(Tuple.Create(reflectedType.InterfaceTypeName ?? reflectedType.FullTypeName, isPolymorphic), k => GenerateDelegate(k.Item1, objectType, k.Item2));
        }

        private static JsonObjectSerializer GenerateDelegate(string key, Type objectType, bool isPolymorphic)
        {
            var method = new DynamicMethod("JsonSerialize_" + key, null, new[] { typeof(object), typeof(TextWriter), typeof(bool) }, typeof(JsonSerializationWriter).Module);
            var il = method.GetILGenerator();

            var writeObject = typeof(JsonSerializationWriter).GetMethod("WriteObject");
            var write = typeof(JsonSerializationWriter).GetMethod("Write");

            var interfaceType = objectType;
            if (Reflector.IsEmitted(objectType))
            {
                interfaceType = Reflector.GetInterface(objectType) ?? objectType;
            }

            var properties = Reflector.GetPropertyMap(interfaceType).Where(p => p.Key.CanRead && p.Key.CanWrite && p.Key.Name != "Indexer" && !p.Key.GetCustomAttributes(typeof(DoNotSerializeAttribute), false).Any()).ToArray();

            il.Emit(OpCodes.Ldstr, "{");
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, write);

            var index = 0;

            if (isPolymorphic)
            {
                il.Emit(OpCodes.Ldstr, string.Format("\"$type\":\"{0},{1}\",", objectType.FullName, objectType.Assembly.GetName().Name));
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, write);
            }

            foreach (var property in properties)
            {
                // Write property value
                il.Emit(OpCodes.Ldarg_0);
                il.EmitCastToReference(interfaceType);
                il.EmitCall(OpCodes.Callvirt, property.Key.GetGetMethod(), null);
                il.BoxIfNeeded(property.Key.PropertyType);
                il.Emit(OpCodes.Ldstr, property.Key.Name);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(++index == properties.Length ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, writeObject);
            }

            il.Emit(OpCodes.Ldstr, "}");
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, write);

            il.Emit(OpCodes.Ret);

            var serializer = (JsonObjectSerializer)method.CreateDelegate(typeof(JsonObjectSerializer));
            return serializer;
        }
    }
}
