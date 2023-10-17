using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nemo.Configuration.Mapping;
using Nemo.Reflection;

namespace Nemo.Configuration;

public class ConfigurationFactory
{
    private MappingFactory MappingFactory = new();

    private Lazy<INemoConfiguration>? _configuration;
    private Lazy<INemoConfiguration> Configuration => _configuration ??= new(() =>
    {
        MappingFactory.Initialize();
        return new DefaultNemoConfiguration();
    }, true);

    private readonly ConcurrentDictionary<Type, INemoConfiguration> _typedConfigurations = new();

    public string DefaultConnectionName
    {
        get { return DefaultConfiguration.DefaultConnectionName; }
    }

    public INemoConfiguration DefaultConfiguration
    {
        get
        {
            return Configuration.Value;
        }
    }

    public INemoConfiguration Configure(Func<INemoConfiguration>? config = null)
    {
        if (Configuration.IsValueCreated)
            return null;

        MappingFactory.Initialize();
        if (config != null)
        {
            _configuration = new Lazy<INemoConfiguration>(config, true);
        }
        return Configuration.Value;
    }

    public void RefreshEntityMappings()
    {
        MappingFactory.Initialize();
    }

    public INemoConfiguration CloneCurrentConfiguration()
    {
        var clone = new DefaultNemoConfiguration();
        return clone.Merge(Configuration.Value);
    }

    public INemoConfiguration CloneConfiguration(INemoConfiguration config)
    {
        if (config == null) return null;

        var clone = new DefaultNemoConfiguration();
        return clone.Merge(config);
    }

    public INemoConfiguration Get<T>()
    {
        return Get(typeof(T));
    }

    public INemoConfiguration Get(Type type)
    {
        var globalConfig = DefaultConfiguration;
        if (!IsConfigurable(type)) return globalConfig;

        if (_typedConfigurations.TryGetValue(type, out var typedConfig)) return typedConfig;
        return globalConfig;
    }

    public void Set<T>(INemoConfiguration configuration)
    {
        Set(typeof(T), configuration);
    }

    public void Set(Type type, INemoConfiguration configuration)
    {
        if (configuration == null || !IsConfigurable(type)) return;

        var globalConfig = DefaultConfiguration;
        _typedConfigurations[type] = configuration.Merge(globalConfig);
    }

    private bool IsConfigurable(Type type)
    {
        var refletectType = Reflector.GetReflectedType(type);
        return !refletectType.IsSimpleType && !refletectType.IsSimpleList;
    }
}
