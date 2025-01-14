﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Nemo.Configuration.Mapping;

public abstract class EntityMap<T> : IEntityMap
    where T : class
{
    private readonly Dictionary<string, IPropertyMap> _properties;

    protected EntityMap()
    {
        _properties = new Dictionary<string, IPropertyMap>();
        TableName = ObjectFactory.GetUnmappedTableName(typeof(T));
    }

    public PropertyMap<T, U> Property<U>(Expression<Func<T, U>> selector)
    {
        IPropertyMap map;
        var key = selector.ToString();
        if (_properties.TryGetValue(key, out map)) return (PropertyMap<T, U>)map;
        map = new PropertyMap<T, U>(selector);
        _properties[key] = map;
        return (PropertyMap<T, U>)map;
    }

    public ICollection<IPropertyMap> Properties
    {
        get
        {
            return _properties.Values;
        }
    }

    public string TableName { get; protected set; }

    public string SchemaName { get; protected set; }

    public string DatabaseName { get; protected set; }

    public string ConnectionStringName { get; protected set; }
    
    public bool ReadOnly { get; protected set; }

    public string SoftDeleteColumnName { get; protected set; }
}
