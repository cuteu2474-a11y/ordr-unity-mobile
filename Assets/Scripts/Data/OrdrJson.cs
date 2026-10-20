using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ordr.Mobile.Data
{
    // Unity 엔진/외부 JSON 패키지에 의존하지 않는 읽기 전용 JSON 문서 파서.
    // 원본 스킬 효과의 이질적인 필드를 잘라내지 않고 그대로 보관하기 위한 자료형.
    public static class OrdrJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return new Reader(json).ReadDocument();
        }

        public static Dictionary<string, object> Object(object value, string field)
        {
            var result = value as Dictionary<string, object>;
            if (result == null) throw new FormatException(field + " 필드는 JSON object여야 합니다.");
            return result;
        }

        public static List<object> Array(object value, string field)
        {
            var result = value as List<object>;
            if (result == null) throw new FormatException(field + " 필드는 JSON array여야 합니다.");
            return result;
        }

        public static object Value(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj.TryGetValue(key, out value) ? value : null;
        }

        public static string String(Dictionary<string, object> obj, string key)
        {
            object value = Value(obj, key);
            if (value == null) return null;
            var str = value as string;
            if (str == null) throw new FormatException(key + " 문자열 형식 오류.");
            return str;
        }

        public static string RequiredString(Dictionary<string, object> obj, string key)
        {
            var value = String(obj, key);
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException(key + " 값이 필요합니다.");
            return value;
        }

        public static bool TryNumber(Dictionary<string, object> obj, string key, out double number)
        {
            object value = Value(obj, key);
            if (value == null) { number = 0; return false; }
            if (!(value is double)) throw new FormatException(key + " 숫자 형식 오류.");
            number = (double)value;
            return true;
        }

        public static double? Number(Dictionary<string, object> obj, string key)
        {
            double number;
            return TryNumber(obj, key, out number) ? (double?)number : null;
        }

        private sealed class Reader
        {
            private readonly string source;
            private int index;

            public Reader(string source) { this.source = source; }

            public object ReadDocument()
            {
                SkipWhitespace();
                object value = ReadValue();
                SkipWhitespace();
                if (index != source.Length) Fail("뒤에 남은 JSON 문자가 있습니다.");
                return value;
            }

            private object ReadValue()
            {
                SkipWhitespace();
                if (index >= source.Length) Fail("값이 없습니다.");
                char c = source[index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Literal("true"); return true;
                    case 'f': Literal("false"); return false;
                    case 'n': Literal("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        Fail("허용되지 않는 JSON 토큰입니다.");
                        return null;
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                Require('{');
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Consume('}')) return result;
                do
                {
                    SkipWhitespace();
                    if (Peek() != '"') Fail("객체 키가 문자열이 아닙니다.");
                    string key = ReadString();
                    SkipWhitespace();
                    Require(':');
                    object value = ReadValue();
                    if (result.ContainsKey(key)) Fail("중복 JSON 키: " + key);
                    result.Add(key, value);
                    SkipWhitespace();
                    if (Consume('}')) return result;
                    Require(',');
                } while (true);
            }

            private List<object> ReadArray()
            {
                Require('[');
                var result = new List<object>();
                SkipWhitespace();
                if (Consume(']')) return result;
                do
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (Consume(']')) return result;
                    Require(',');
                } while (true);
            }

            private string ReadString()
            {
                Require('"');
                var text = new StringBuilder();
                while (index < source.Length)
                {
                    char c = source[index++];
                    if (c == '"') return text.ToString();
                    if (c < 0x20) Fail("문자열 내부 제어 문자는 이스케이프해야 합니다.");
                    if (c != '\\') { text.Append(c); continue; }
                    if (index >= source.Length) Fail("미완성 문자열 이스케이프.");
                    char escape = source[index++];
                    switch (escape)
                    {
                        case '"': text.Append('"'); break;
                        case '\\': text.Append('\\'); break;
                        case '/': text.Append('/'); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            if (index + 4 > source.Length) Fail("불완전 유니코드 이스케이프.");
                            int code;
                            if (!int.TryParse(source.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code)) Fail("유니코드 이스케이프 오류.");
                            text.Append((char)code);
                            index += 4;
                            break;
                        default: Fail("잘못된 문자열 이스케이프.");
                            break;
                    }
                }
                Fail("닫히지 않은 문자열.");
                return null;
            }

            private double ReadNumber()
            {
                int start = index;
                Consume('-');
                if (Consume('0'))
                {
                    if (index < source.Length && char.IsDigit(source[index]))
                        Fail("숫자 0 뒤에 숫자가 바로 올 수 없습니다.");
                }
                else
                {
                    if (index >= source.Length || source[index] < '1' || source[index] > '9')
                        Fail("숫자 형식 오류.");
                    while (index < source.Length && source[index] >= '0' && source[index] <= '9') index++;
                }
                if (Consume('.'))
                {
                    if (index >= source.Length || !char.IsDigit(source[index])) Fail("소수점 뒤 숫자가 없습니다.");
                    while (index < source.Length && source[index] >= '0' && source[index] <= '9') index++;
                }
                if (Consume('e') || Consume('E'))
                {
                    if (!Consume('+')) Consume('-');
                    if (index >= source.Length || !char.IsDigit(source[index])) Fail("지수 숫자가 없습니다.");
                    while (index < source.Length && source[index] >= '0' && source[index] <= '9') index++;
                }
                double value;
                if (!double.TryParse(source.Substring(start, index - start),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                    double.IsNaN(value) || double.IsInfinity(value)) Fail("숫자 오버플로.");
                return value;
            }

            private void Literal(string literal)
            {
                if (index + literal.Length > source.Length ||
                    string.CompareOrdinal(source, index, literal, 0, literal.Length) != 0)
                    Fail("JSON 리터럴 오류.");
                index += literal.Length;
            }

            private void Require(char expected)
            {
                SkipWhitespace();
                if (!Consume(expected)) Fail("'" + expected + "' 문자가 필요합니다.");
            }

            private bool Consume(char expected)
            {
                if (index >= source.Length || source[index] != expected) return false;
                index++;
                return true;
            }

            private char Peek() { return index < source.Length ? source[index] : '\0'; }

            private void SkipWhitespace()
            {
                while (index < source.Length)
                {
                    char c = source[index];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
                    index++;
                }
            }

            private void Fail(string message)
            {
                throw new FormatException(message + " (index " +
                    index.ToString(CultureInfo.InvariantCulture) + ")");
            }
        }
    }
}
