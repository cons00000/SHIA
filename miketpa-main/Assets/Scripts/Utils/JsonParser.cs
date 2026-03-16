using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Assets.Scripts.Utils
{
    public enum JsonType
    {
        Object,
        Array,
        String,
        Number,
        Boolean,
        Null
    }

    public class JsonValue
    {
        public JsonType Type { get; set; }
        public Dictionary<string, JsonValue> ObjectValues { get; set; }
        public List<JsonValue> ArrayValues { get; set; }
        public string StringValue { get; set; }
        public double NumberValue { get; set; }
        public bool BoolValue { get; set; }

        public JsonValue(JsonType type)
        {
            Type = type;
            if (type == JsonType.Object) ObjectValues = new Dictionary<string, JsonValue>();
            if (type == JsonType.Array) ArrayValues = new List<JsonValue>();
        }

        public JsonValue this[string key] => ObjectValues[key];
        public JsonValue this[int index] => ArrayValues[index];

        public override string ToString()
        {
            return Type switch
            {
                JsonType.String => $"\"{StringValue}\"",
                JsonType.Number => NumberValue.ToString(),
                JsonType.Boolean => BoolValue ? "true" : "false",
                JsonType.Null => "null",
                JsonType.Object => "{object}",
                JsonType.Array => "[array]",
                _ => base.ToString()
            };
        }

        public string ToJsonString()
        {
            var sb = new StringBuilder();
            WriteJson(this, sb);
            return sb.ToString();
        }

        private static void WriteJson(JsonValue value, StringBuilder sb)
        {
            switch (value.Type)
            {
                case JsonType.Object:
                    sb.Append('{');
                    bool firstObj = true;
                    foreach (var kv in value.ObjectValues)
                    {
                        if (!firstObj) sb.Append(',');
                        firstObj = false;
                        sb.Append('\"').Append(EscapeString(kv.Key)).Append("\":");
                        WriteJson(kv.Value, sb);
                    }
                    sb.Append('}');
                    break;

                case JsonType.Array:
                    sb.Append('[');
                    bool firstArr = true;
                    foreach (var elem in value.ArrayValues)
                    {
                        if (!firstArr) sb.Append(',');
                        firstArr = false;
                        WriteJson(elem, sb);
                    }
                    sb.Append(']');
                    break;

                case JsonType.String:
                    sb.Append('\"').Append(EscapeString(value.StringValue)).Append('\"');
                    break;

                case JsonType.Number:
                    sb.Append(value.NumberValue.ToString(CultureInfo.InvariantCulture));
                    break;

                case JsonType.Boolean:
                    sb.Append(value.BoolValue ? "true" : "false");
                    break;

                case JsonType.Null:
                    sb.Append("null");
                    break;
            }
        }

        private static string EscapeString(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                sb.Append(c switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => c < 32 ? $"\\u{((int)c):X4}" : c.ToString()
                });
            }
            return sb.ToString();
        }

    }

    public class JsonParser
    {
        private string _input;
        private int _pos;

        public JsonValue Parse(string json)
        {
            _input = json;
            _pos = 0;
            SkipWhitespace();
            var value = ParseValue();
            SkipWhitespace();
            if (_pos != _input.Length)
                throw new Exception("Extra characters after JSON value");
            return value;
        }

        private void SkipWhitespace()
        {
            while (_pos < _input.Length && char.IsWhiteSpace(_input[_pos])) _pos++;
        }

        private char Peek() => _pos < _input.Length ? _input[_pos] : '\0';
        private char Next() => _input[_pos++];

        private JsonValue ParseValue()
        {
            SkipWhitespace();
            char c = Peek();
            return c switch
            {
                '{' => ParseObject(),
                '[' => ParseArray(),
                '"' => ParseString(),
                't' or 'f' => ParseBoolean(),
                'n' => ParseNull(),
                _ => ParseNumber(),
            };
        }

        private JsonValue ParseObject()
        {
            var obj = new JsonValue(JsonType.Object);
            Expect('{');
            SkipWhitespace();
            if (Peek() == '}')
            {
                Next();
                return obj;
            }
            while (true)
            {
                SkipWhitespace();
                var key = ParseString().StringValue;
                SkipWhitespace();
                Expect(':');
                SkipWhitespace();
                var value = ParseValue();
                obj.ObjectValues[key] = value;
                SkipWhitespace();
                if (Peek() == '}')
                {
                    Next();
                    break;
                }
                Expect(',');
            }
            return obj;
        }

        private JsonValue ParseArray()
        {
            var arr = new JsonValue(JsonType.Array);
            Expect('[');
            SkipWhitespace();
            if (Peek() == ']')
            {
                Next();
                return arr;
            }
            while (true)
            {
                arr.ArrayValues.Add(ParseValue());
                SkipWhitespace();
                if (Peek() == ']')
                {
                    Next();
                    break;
                }
                Expect(',');
            }
            return arr;
        }

        private JsonValue ParseString()
        {
            Expect('"');
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _input.Length) throw new Exception("Unterminated string");
                char c = Next();
                if (c == '"') break;
                if (c == '\\')
                {
                    if (_pos >= _input.Length) throw new Exception("Invalid escape sequence");
                    c = Next();
                    c = c switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'u' => ParseUnicode(),
                        _ => throw new Exception($"Invalid escape: \\{c}")
                    };
                }
                sb.Append(c);
            }
            return new JsonValue(JsonType.String) { StringValue = sb.ToString() };
        }

        private char ParseUnicode()
        {
            if (_pos + 4 > _input.Length) throw new Exception("Incomplete unicode escape");
            string hex = _input.Substring(_pos, 4);
            _pos += 4;
            return (char)Convert.ToInt32(hex, 16);
        }

        private JsonValue ParseNumber()
        {
            int start = _pos;
            if (Peek() == '-') Next();
            while (char.IsDigit(Peek())) Next();
            if (Peek() == '.')
            {
                Next();
                while (char.IsDigit(Peek())) Next();
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                Next();
                if (Peek() == '+' || Peek() == '-') Next();
                while (char.IsDigit(Peek())) Next();
            }

            string numStr = _input.Substring(start, _pos - start);
            if (!double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double result))
                throw new Exception($"Invalid number: {numStr}");
            return new JsonValue(JsonType.Number) { NumberValue = result };
        }

        private JsonValue ParseBoolean()
        {
            if (_input.Substring(_pos).StartsWith("true"))
            {
                _pos += 4;
                return new JsonValue(JsonType.Boolean) { BoolValue = true };
            }
            if (_input.Substring(_pos).StartsWith("false"))
            {
                _pos += 5;
                return new JsonValue(JsonType.Boolean) { BoolValue = false };
            }
            throw new Exception("Invalid boolean value");
        }

        private JsonValue ParseNull()
        {
            if (_input.Substring(_pos).StartsWith("null"))
            {
                _pos += 4;
                return new JsonValue(JsonType.Null);
            }
            throw new Exception("Invalid null value");
        }

        private void Expect(char expected)
        {
            if (Next() != expected)
                throw new Exception($"Expected '{expected}' at position {_pos}");
        }




    }




    // Exemple d'utilisation
    /*class Program
    {
        static void Main()
        {
            string json = "{\"name\":\"Alice\",\"age\":30,\"skills\":[\"C#\",\"JSON\"],\"active\":true}";
            var parser = new JsonParser();
            var root = parser.Parse(json);

            Console.WriteLine(root["name"].StringValue);      // Alice
            Console.WriteLine(root["age"].NumberValue);       // 30
            Console.WriteLine(root["skills"][0].StringValue); // C#
            Console.WriteLine(root["active"].BoolValue);      // True
        }
    }*/
}
