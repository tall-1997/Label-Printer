using System.Collections.Generic;
using System.Text;

namespace BarTenderPrinter
{
    public static class CsvUtils
    {
        public static List<string> ParseLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;
            var current = new StringBuilder();
            var inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }

        public static string Escape(string value)
        {
            value ??= "";
            if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@' || value[0] == '\t' || value[0] == '\r'))
                value = "'" + value;
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
