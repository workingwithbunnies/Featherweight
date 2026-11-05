using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Featherweight
{
    // Small JSON helper: parsing via the framework's JavaScriptSerializer, writing with indentation
    // so plans/backups/snapshots stay readable and compatible with Featherweight.ps1.
    static class Json
    {
        public static object Parse(string text)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            return js.DeserializeObject(text);
        }

        public static Dictionary<string, object> ReadFile(string path)
        {
            return Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        }

        public static void WriteFile(string path, object value)
        {
            File.WriteAllText(path, Write(value), new UTF8Encoding(false));
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is byte[]) { var list = new List<object>(); foreach (var b in (byte[])v) list.Add((int)b); WriteValue(sb, list, depth); return; }
            if (v is int || v is long || v is uint || v is ulong || v is short || v is byte)
            { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float || v is decimal)
            { sb.Append(Convert.ToDouble(v).ToString("0.###", CultureInfo.InvariantCulture)); return; }
            string pad = new string(' ', (depth + 1) * 2), endPad = new string(' ', depth * 2);
            var dict = v as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n"); bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(",\n"); first = false;
                    sb.Append(pad); WriteString(sb, e.Key.ToString()); sb.Append(": ");
                    WriteValue(sb, e.Value, depth + 1);
                }
                sb.Append("\n").Append(endPad).Append("}"); return;
            }
            var en = v as IEnumerable;
            if (en != null)
            {
                var items = new List<object>(); foreach (var x in en) items.Add(x);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(",\n");
                    sb.Append(pad); WriteValue(sb, items[i], depth + 1);
                }
                sb.Append("\n").Append(endPad).Append("]"); return;
            }
            WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- typed accessors for parsed objects ----
        public static string Str(this Dictionary<string, object> d, string key)
        {
            object v; return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }
        public static bool Bool(this Dictionary<string, object> d, string key)
        {
            object v; return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
        }
        public static double Num(this Dictionary<string, object> d, string key)
        {
            object v; if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }
        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string key)
        {
            object v; return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }
        public static List<Dictionary<string, object>> List(this Dictionary<string, object> d, string key)
        {
            var res = new List<Dictionary<string, object>>();
            object v; if (d == null || !d.TryGetValue(key, out v) || v == null) return res;
            var single = v as Dictionary<string, object>;
            if (single != null) { res.Add(single); return res; } // PowerShell writes 1-element arrays as an object
            var en = v as IEnumerable;
            if (en != null) foreach (var x in en) { var o = x as Dictionary<string, object>; if (o != null) res.Add(o); }
            return res;
        }
    }
}
