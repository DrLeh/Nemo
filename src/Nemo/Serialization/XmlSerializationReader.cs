﻿using Nemo.Collections;
using Nemo.Extensions;
using Nemo.Fn;
using Nemo.Reflection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Nemo.Serialization;

public static class XmlSerializationReader
{
    private static object ReadValue(XmlReader reader, Type objectType, string name)
    {
        object value = null;
        var hasValue = false;
        if (name != null && reader.HasAttributes)
        {
            var attrValue = reader.GetAttribute(name);
            hasValue = !string.IsNullOrEmpty(attrValue);
            attrValue.TryParse(objectType, out value);
        }

        if (!hasValue)
        {
            value = reader.ReadElementContentAs(objectType, null);
        }
        return value;
    }

    public static object ReadObject(XmlReader reader, Type objectType, out bool isArray)
    {
        var states = new LinkedList<SerializationReaderState>();
        object result = null;
        isArray = false;

        object item = null;
        IList list = null;
        Type elementType = null;
        IDictionary<string, ReflectedProperty> propertyMap = null;
        var isSimple = false;
        string name = null;

        if (reader.IsStartElement())
        {
            name = reader.Name;
            isArray = name.StartsWith("ArrayOf");
            var reflectedType = Reflector.GetReflectedType(objectType);
            if (isArray || reflectedType.IsList)
            {
                elementType = reflectedType.IsList ? reflectedType.ElementType : objectType;
                isSimple = reflectedType.IsList ? reflectedType.IsSimpleList : reflectedType.IsSimpleType;
                propertyMap = Reflector.GetPropertyNameMap(elementType);
                if (!objectType.IsInterface)
                {
                    list = (IList)objectType.New();
                }
                else
                {
                    list = List.Create(elementType);
                }
                result = list;
            }
            else if (reflectedType.IsDataEntity)
            {
                // Handle attributes
                if (reader.HasAttributes)
                {
                    objectType = Type.GetType(reader.GetAttribute("_.type") ?? "", false) ?? objectType;
                    propertyMap = Reflector.GetPropertyNameMap(objectType);
                    item = ObjectFactory.Create(objectType);
                    result = item;

                    for (var i = 0; i < reader.AttributeCount; i++)
                    {
                        reader.MoveToAttribute(i);
                        if (reader.Name == "_.type") continue;
                        ReflectedProperty property;
                        var propertyName = reader.Name;
                        if (!propertyMap.TryGetValue(propertyName, out property)) continue;
                        var value = ReadValue(reader, property.PropertyType, propertyName);
                        if (value != null)
                        {
                            item.Property(propertyName, value);
                        }
                    }
                }
                else
                {
                    propertyMap = Reflector.GetPropertyNameMap(objectType);
                    item = ObjectFactory.Create(objectType);
                    result = item;
                }
            }
            else if (reflectedType.IsSimpleType)
            {
                result = ReadValue(reader, objectType, null);
                isSimple = true;
            }
            reader.Read();
        }

        if (isSimple && list == null)
        {
            return result;
        }

        states.AddLast(new SerializationReaderState { Name = name, Item = item, List = list, ElementType = elementType, PropertyMap = propertyMap, IsSimple = isSimple });

        while (reader.IsStartElement())
        {
            name = reader.Name;
            var lastState = states.Last;
            var currentValue = lastState.Value;
            var currentMap = currentValue.PropertyMap;
            if (currentValue.ElementType != null)
            {
                if (currentValue.IsSimple)
                {
                    var value = ReadValue(reader, currentValue.ElementType, name);
                    if (currentValue.List != null)
                    {
                        currentValue.List.Add(value);
                    }
                    else
                    {
                        currentValue.Value = value;
                    }
                    if (reader.NodeType != XmlNodeType.EndElement)
                    {
                        continue;
                    }
                }
                else
                {
                    if (reader.HasAttributes)
                    {
                        currentValue.ElementType = Type.GetType(reader.GetAttribute("_.type") ?? "", false) ?? currentValue.ElementType;
                        currentValue.PropertyMap = Reflector.GetPropertyNameMap(currentValue.ElementType);
                    }
                    item = ObjectFactory.Create(currentValue.ElementType);
                    if (currentValue.List != null)
                    {
                        currentValue.List.Add(item);
                    }
                    else
                    {
                        currentValue.Value = item;
                    }
                    states.AddLast(new SerializationReaderState { Name = name, Item = item, PropertyMap = currentValue.PropertyMap });
                }
            } 
            else if (currentMap != null && currentMap.TryGetValue(name, out var property))
            {
                if (property.IsDataEntity)
                {
                    propertyMap = Reflector.GetPropertyNameMap(property.PropertyType);
                    item = ObjectFactory.Create(property.PropertyType);
                    states.AddLast(new SerializationReaderState { Name = name, Item = item, PropertyMap = propertyMap });

                    if (currentValue.Item != null)
                    {
                        currentValue.Item.Property(name, item);
                    }
                    else
                    {
                        currentValue.List.Add(item);
                    }
                }
                else if (property.IsDataEntityList)
                {
                    elementType = property.ElementType;
                    propertyMap = Reflector.GetPropertyNameMap(elementType);
                    if (!property.IsListInterface)
                    {
                        list = (IList)property.PropertyType.New();
                    }
                    else
                    {
                        list = List.Create(elementType, property.Distinct, property.Sorted);
                    }
                    states.AddLast(new SerializationReaderState { Name = name, List = list, ElementType = elementType, PropertyMap = propertyMap });

                    if (currentValue.Item != null)
                    {
                        currentValue.Item.Property(name, list);
                    }
                }
                else
                {
                    var value = ReadValue(reader, currentValue.ElementType ?? property.PropertyType, name);
                    if (value != null)
                    {
                        if (!currentValue.IsSimple)
                        {
                            currentValue.Item.Property(property.PropertyName, value);
                        }
                        else if (currentValue.List != null)
                        {
                            currentValue.List.Add(value);
                        }
                        else
                        {
                            currentValue.Value = value;
                        }
                    }
                    if (reader.NodeType != XmlNodeType.EndElement)
                    {
                        continue;
                    }
                }
            } 

            if (reader.NodeType == XmlNodeType.EndElement)
            {
                while (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (states.Count > 0)
                    {
                        states.RemoveLast();
                    }
                    reader.Read();
                }
            }
            else
            {
                reader.Read();
            }
        }
        return result;
    }
}
