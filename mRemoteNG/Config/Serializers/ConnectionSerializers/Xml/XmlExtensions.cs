using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;

public static class XmlExtensions
{
    private static readonly ConcurrentDictionary<(Type, string), object?> s_enumCache = new();

    /// <summary>
    /// Pre-builds a dictionary from XmlNode.Attributes for O(1) lookups.
    /// Use this when reading many attributes from the same node (e.g. connection deserialization).
    /// </summary>
    public static Dictionary<string, string> BuildAttributeDictionary(this XmlNode xmlNode)
    {
        if (xmlNode?.Attributes == null)
            return new Dictionary<string, string>(0, StringComparer.Ordinal);

        var dict = new Dictionary<string, string>(xmlNode.Attributes.Count, StringComparer.Ordinal);
        foreach (XmlAttribute attr in xmlNode.Attributes)
            dict[attr.Name] = attr.Value;
        return dict;
    }

    // --- Dictionary-based overloads (O(1) per lookup) ---

    public static string GetAttr(this Dictionary<string, string> attrs, string attribute, string defaultValue = "")
    {
        return attrs.TryGetValue(attribute, out string? value) ? value : defaultValue;
    }

    public static bool GetAttrBool(this Dictionary<string, string> attrs, string attribute, bool defaultValue = false)
    {
        if (!attrs.TryGetValue(attribute, out string? value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return bool.TryParse(value, out bool result) ? result : defaultValue;
    }

    public static int GetAttrInt(this Dictionary<string, string> attrs, string attribute, int defaultValue = 0)
    {
        if (!attrs.TryGetValue(attribute, out string? value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;
    }

    public static T GetAttrEnum<T>(this Dictionary<string, string> attrs, string attribute, T defaultValue = default)
        where T : struct
    {
        if (!attrs.TryGetValue(attribute, out string? value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var cacheKey = (typeof(T), value);
        if (s_enumCache.TryGetValue(cacheKey, out object? cached))
            return cached is T t ? t : defaultValue;

        bool parsed = Enum.TryParse<T>(value, true, out T result);
        s_enumCache[cacheKey] = parsed ? result : null;
        return parsed ? result : defaultValue;
    }

    // --- XmlNode-based methods (original, for backward compatibility) ---

    public static string GetAttributeAsString(this XmlNode xmlNode, string attribute, string defaultValue = "")
    {
        string? value = xmlNode?.Attributes?[attribute]?.Value;
        return value ?? defaultValue;
    }

    public static bool GetAttributeAsBool(this XmlNode xmlNode, string attribute, bool defaultValue = false)
    {
        string? value = xmlNode?.Attributes?[attribute]?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return bool.TryParse(value, out bool valueAsBool)
            ? valueAsBool
            : defaultValue;
    }

    public static int GetAttributeAsInt(this XmlNode xmlNode, string attribute, int defaultValue = 0)
    {
        string? value = xmlNode?.Attributes?[attribute]?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int valueAsBool)
            ? valueAsBool
            : defaultValue;
    }

    public static T GetAttributeAsEnum<T>(this XmlNode xmlNode, string attribute, T defaultValue = default)
        where T : struct
    {
        string? value = xmlNode?.Attributes?[attribute]?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var cacheKey = (typeof(T), value);
        if (s_enumCache.TryGetValue(cacheKey, out object? cached))
            return cached is T t ? t : defaultValue;

        bool parsed = Enum.TryParse<T>(value, true, out T result);
        s_enumCache[cacheKey] = parsed ? result : null;
        return parsed ? result : defaultValue;
    }
}