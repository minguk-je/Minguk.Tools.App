using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Minguk.Tools.Projects.Settings;

/// <summary>
/// 설정 값 - <c>settings.values.json</c>. 이름 → JSON 값.
/// </summary>
/// <remarks>양식에 없는 이름의 값도 지우지 않는다 - 칸을 잠깐 지웠다 되살리면 값이 돌아온다.</remarks>
public sealed class SettingsValues
{
    public Dictionary<string, JsonNode?> Values { get; } = new(StringComparer.Ordinal);

    public static SettingsValues Load(string path)
    {
        var result = new SettingsValues();

        if (!File.Exists(path)) return result;

        if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonObject { } root && root["values"] is JsonObject values)
        {
            foreach (var (name, value) in values)
                result.Values[name] = value?.DeepClone();
        }

        return result;
    }

    public void Save(string path)
    {
        var values = new JsonObject();

        foreach (var (name, value) in Values.OrderBy(p => p.Key, StringComparer.Ordinal))
            values[name] = value?.DeepClone();

        var root = new JsonObject { ["version"] = 1, ["values"] = values };

        SolutionSettingsFiles.WriteAtomic(path, root.ToJsonString(SolutionSettingsFiles.Json));
    }
}

/// <summary>JSON 값과 스크립트 값 사이를 오간다.</summary>
public static class SettingsValue
{
    /// <summary>칸 종류에 맞는 값인가. 안 맞으면 읽을 때 다음 층으로 넘어간다.</summary>
    public static bool Fits(SettingsItem item, JsonNode? node) => item.Kind switch
    {
        SettingsItemKind.Text or SettingsItemKind.Combo => node is JsonValue v && v.GetValueKind() == JsonValueKind.String,
        SettingsItemKind.Number or SettingsItemKind.Slider => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number,
        SettingsItemKind.Check => node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        SettingsItemKind.List => node is JsonArray array && array.All(row => row is JsonObject),
        _ => false
    };

    /// <summary>스크립트에 줄 값 - 정수는 long, 아니면 double, 글·참거짓 그대로, 목록은 행 목록.</summary>
    public static object? ToObject(JsonNode? node) => node switch
    {
        null => null,
        JsonArray array => (IReadOnlyList<SettingsRow>)[.. array.OfType<JsonObject>().Select(row => new SettingsRow(row))],
        JsonObject obj => new SettingsRow(obj),
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => NumberOf(value),
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// 숫자 JSON 값을 double 로. 파일에서 읽은 값과 코드에서 만든 값(<c>JsonValue&lt;int&gt;</c>)을 똑같이 -
    /// 만든 값에 <c>GetValue&lt;double&gt;()</c> 을 부르면 형식이 달라 터진다(실측).
    /// </summary>
    public static double ToDouble(JsonValue value) => double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture);

    private static object NumberOf(JsonValue value)
    {
        var d = ToDouble(value);

        // (object) 로 감싼다 - 안 감싸면 삼항식이 long 을 double 로 맞춰 정수가 실수로 나간다(실측).
        return d == Math.Floor(d) && Math.Abs(d) < 9e15 ? (object)(long)d : d;
    }

    /// <summary>원하는 형식으로. 못 바꾸면 null 과 이유.</summary>
    public static bool TryConvert(JsonNode? node, Type type, out object? result)
    {
        result = null;

        var target = Nullable.GetUnderlyingType(type) ?? type;
        var raw = ToObject(node);

        if (raw is null) return !target.IsValueType || Nullable.GetUnderlyingType(type) is not null;

        if (target.IsInstanceOfType(raw))
        {
            result = raw;
            return true;
        }

        try
        {
            if (target == typeof(string))
            {
                result = Convert.ToString(raw, CultureInfo.InvariantCulture);
                return raw is string;
            }

            if (raw is bool && target != typeof(bool)) return false;
            if (raw is string && target != typeof(string)) return false;

            if (target == typeof(int) || target == typeof(long) || target == typeof(short))
            {
                var d = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (d != Math.Floor(d)) return false;
            }

            if (target.IsEnum) return false;

            result = Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>스크립트가 넘긴 값을 JSON 으로. 파이썬·JS 가 넘기는 모양(사전·배열)도 받는다.</summary>
    public static JsonNode? FromObject(object? value)
    {
        switch (value)
        {
            case null: return null;
            case JsonNode node: return node.DeepClone();
            case string s: return JsonValue.Create(s);
            case bool b: return JsonValue.Create(b);
            case SettingsRow row: return row.ToJson();
            case float or double or decimal:
                var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return d == Math.Floor(d) && Math.Abs(d) < 9e15 ? JsonValue.Create((long)d) : JsonValue.Create(d);
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            case IDictionary<string, object?> map:
                {
                    var obj = new JsonObject();
                    foreach (var (k, v) in map) obj[k] = FromObject(v);
                    return obj;
                }
            case IDictionary dictionary:
                {
                    var obj = new JsonObject();
                    foreach (DictionaryEntry e in dictionary) obj[Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? ""] = FromObject(e.Value);
                    return obj;
                }
            case IEnumerable items:
                {
                    var array = new JsonArray();
                    foreach (var item in items) array.Add(FromObject(item));
                    return array;
                }
            case IConvertible convertible:
                return JsonValue.Create(convertible.ToString(CultureInfo.InvariantCulture));
            default:
                return JsonValue.Create(value.ToString());
        }
    }
}

/// <summary>목록 칸의 한 행. <c>row["HP"]</c>·<c>row.Get&lt;int&gt;("HP")</c>.</summary>
public sealed class SettingsRow
{
    private readonly JsonObject _row;

    public SettingsRow(JsonObject row) => _row = (JsonObject)row.DeepClone();

    public object? this[string column] => SettingsValue.ToObject(_row[column]);

    public IEnumerable<string> Columns => _row.Select(p => p.Key);

    public T Get<T>(string column)
    {
        if (!_row.ContainsKey(column)) throw new KeyNotFoundException($"행에 「{column}」 열이 없습니다.");

        return SettingsValue.TryConvert(_row[column], typeof(T), out var result)
            ? (T)result!
            : throw new InvalidCastException($"「{column}」 열의 값 {_row[column]?.ToJsonString()} 을(를) {typeof(T).Name} 로 읽을 수 없습니다.");
    }

    internal JsonObject ToJson() => (JsonObject)_row.DeepClone();

    public override string ToString() => _row.ToJsonString();
}
