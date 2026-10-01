using System.Globalization;
using System.Numerics;
using System.Text;

namespace Tm2Ac.AcTrack.Ini;

/// <summary>Ordered INI document writer. Numbers are always formatted with the invariant culture (AC can't parse "0,99").</summary>
public sealed class IniFile
{
    private readonly List<(string Name, List<(string Key, string Value)> Entries)> _sections = [];

    public IniSection Section(string name)
    {
        var entries = new List<(string, string)>();
        _sections.Add((name, entries));
        return new IniSection(entries);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < _sections.Count; i++)
        {
            if (i > 0)
            {
                sb.Append("\r\n");
            }

            sb.Append('[').Append(_sections[i].Name).Append("]\r\n");
            foreach (var (key, value) in _sections[i].Entries)
            {
                sb.Append(key).Append('=').Append(value).Append("\r\n");
            }
        }

        return sb.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static string Format(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Format(Vector3 v) => $"{Format(v.X)},{Format(v.Y)},{Format(v.Z)}";
}

public sealed class IniSection
{
    private readonly List<(string Key, string Value)> _entries;

    internal IniSection(List<(string Key, string Value)> entries) => _entries = entries;

    public IniSection Set(string key, string value)
    {
        _entries.Add((key, value));
        return this;
    }

    public IniSection Set(string key, float value) => Set(key, IniFile.Format(value));

    public IniSection Set(string key, double value) => Set(key, IniFile.Format(value));

    public IniSection Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public IniSection Set(string key, bool value) => Set(key, value ? "1" : "0");

    public IniSection Set(string key, Vector3 value) => Set(key, IniFile.Format(value));
}
