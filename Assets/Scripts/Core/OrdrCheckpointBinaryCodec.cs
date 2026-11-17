using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Ordr.Mobile.Core
{
    // Durable checkpoint payload codec. Unity JsonUtility drops unsupported
    // fields without error; a save can be checksummed yet impossible to load.
    // This codec handles the checkpoint's explicitly declared public fields,
    // preserves full 64-bit numbers, rejects unsupported types, and never
    // serializes arbitrary runtime objects or delegates.
    internal static class OrdrCheckpointBinaryCodec
    {
        private const int MaxBytes = 64 * 1024 * 1024;
        private const int MaxItems = 500000;
        private const int MaxDepth = 96;
        // Reflection-based checkpoint fields changed: never deserialize old
        // nonplayable draft payloads as if they contained newer owner state.
        // External lane ownership and shared-native event history changed strict payload fields.
        // Older nonplayable drafts cannot be interpreted as current worlds.
        private const int Version = 7;
        private static readonly Dictionary<Type, FieldInfo[]> Cache =
            new Dictionary<Type, FieldInfo[]>();

        public static string Encode(OrdrCoreCheckpointState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Version);
                Write(writer, typeof(OrdrCoreCheckpointState), state, 0);
                writer.Flush();
                if (stream.Length > MaxBytes)
                    throw new FormatException("저장본 크기가 허용 범위를 넘습니다.");
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        public static OrdrCoreCheckpointState Decode(string payload)
        {
            if (string.IsNullOrEmpty(payload) ||
                payload.Length > (MaxBytes + 2L) / 3L * 4L + 8L)
                throw new FormatException("저장본 인코딩 크기가 잘못됐습니다.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(payload); }
            catch (FormatException) { throw new FormatException("저장본 인코딩이 잘못됐습니다."); }
            if (bytes.Length > MaxBytes)
                throw new FormatException("저장본 크기가 허용 범위를 넘습니다.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadInt32() != Version)
                    throw new FormatException("지원되지 않는 저장본 직렬화 버전입니다.");
                var restored = Read(reader, typeof(OrdrCoreCheckpointState), 0)
                    as OrdrCoreCheckpointState;
                if (restored == null || stream.Position != stream.Length)
                    throw new FormatException("저장본에 누락되거나 남은 데이터가 있습니다.");
                return restored;
            }
        }

        private static FieldInfo[] Fields(Type type)
        {
            FieldInfo[] found;
            if (Cache.TryGetValue(type, out found)) return found;
            found = Array.FindAll(type.GetFields(
                BindingFlags.Instance | BindingFlags.Public),
                f => !f.IsStatic && !f.IsNotSerialized);
            Array.Sort(found, (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
            Cache[type] = found;
            return found;
        }

        private static Type Underlying(Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        private static void Write(BinaryWriter writer, Type declared, object value, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("저장본 중첩 깊이가 너무 큽니다.");
            bool exists = value != null;
            writer.Write(exists);
            if (!exists)
            {
                if (declared.IsValueType && Nullable.GetUnderlyingType(declared) == null)
                    throw new FormatException("필수 값 형식이 비어 있습니다.");
                return;
            }
            Type type = Underlying(declared);
            if (type == typeof(string)) { writer.Write((string)value); return; }
            if (type == typeof(bool)) { writer.Write((bool)value); return; }
            if (type == typeof(byte)) { writer.Write((byte)value); return; }
            if (type == typeof(sbyte)) { writer.Write((sbyte)value); return; }
            if (type == typeof(short)) { writer.Write((short)value); return; }
            if (type == typeof(ushort)) { writer.Write((ushort)value); return; }
            if (type == typeof(int)) { writer.Write((int)value); return; }
            if (type == typeof(uint)) { writer.Write((uint)value); return; }
            if (type == typeof(long)) { writer.Write((long)value); return; }
            if (type == typeof(ulong)) { writer.Write((ulong)value); return; }
            if (type == typeof(float)) { writer.Write((float)value); return; }
            if (type == typeof(double)) { writer.Write((double)value); return; }
            if (type == typeof(decimal)) { writer.Write((decimal)value); return; }
            if (type == typeof(char)) { writer.Write((char)value); return; }
            if (type.IsEnum)
            {
                writer.Write(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return;
            }
            if (type.IsArray || (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                var list = value as IList;
                if (list == null || list.Count > MaxItems)
                    throw new FormatException("저장본 목록 형식이나 크기가 잘못됐습니다.");
                Type element = type.IsArray ? type.GetElementType() :
                    type.GetGenericArguments()[0];
                writer.Write(list.Count);
                foreach (object item in list)
                    Write(writer, element, item, depth + 1);
                return;
            }
            if (typeof(IDictionary).IsAssignableFrom(type) && type.IsGenericType &&
                type.GetGenericArguments().Length == 2)
            {
                var dict = value as IDictionary;
                if (dict == null || dict.Count > MaxItems)
                    throw new FormatException("저장본 사전 형식이나 크기가 잘못됐습니다.");
                var keys = new List<object>();
                foreach (object key in dict.Keys) keys.Add(key);
                keys.Sort((a, b) => StringComparer.Ordinal.Compare(
                    Convert.ToString(a, CultureInfo.InvariantCulture),
                    Convert.ToString(b, CultureInfo.InvariantCulture)));
                writer.Write(keys.Count);
                var generics = type.GetGenericArguments();
                foreach (var key in keys)
                {
                    Write(writer, generics[0], key, depth + 1);
                    Write(writer, generics[1], dict[key], depth + 1);
                }
                return;
            }
            if (!type.IsClass && !type.IsValueType)
                throw new FormatException("지원하지 않는 저장본 형식: " + type.FullName);
            if (type.IsAbstract || type.IsInterface || !type.IsSerializable)
                throw new FormatException("직렬화할 수 없는 저장본 형식: " + type.FullName);
            var fields = Fields(type);
            if (fields.Length > 1024)
                throw new FormatException("저장본 필드가 너무 많습니다.");
            writer.Write(fields.Length);
            foreach (FieldInfo field in fields)
            {
                writer.Write(field.Name);
                using (var fragment = new MemoryStream())
                using (var inner = new BinaryWriter(fragment, Encoding.UTF8))
                {
                    Write(inner, field.FieldType, field.GetValue(value), depth + 1);
                    inner.Flush();
                    if (fragment.Length > MaxBytes)
                        throw new FormatException("저장본 필드가 너무 큽니다.");
                    writer.Write((int)fragment.Length);
                    writer.Write(fragment.ToArray());
                }
            }
        }

        private static object Read(BinaryReader reader, Type declared, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("저장본 중첩 깊이가 너무 큽니다.");
            bool exists = reader.ReadBoolean();
            if (!exists)
            {
                if (declared.IsValueType && Nullable.GetUnderlyingType(declared) == null)
                    throw new FormatException("필수 값 형식이 비어 있습니다.");
                return null;
            }
            Type type = Underlying(declared);
            if (type == typeof(string)) return reader.ReadString();
            if (type == typeof(bool)) return reader.ReadBoolean();
            if (type == typeof(byte)) return reader.ReadByte();
            if (type == typeof(sbyte)) return reader.ReadSByte();
            if (type == typeof(short)) return reader.ReadInt16();
            if (type == typeof(ushort)) return reader.ReadUInt16();
            if (type == typeof(int)) return reader.ReadInt32();
            if (type == typeof(uint)) return reader.ReadUInt32();
            if (type == typeof(long)) return reader.ReadInt64();
            if (type == typeof(ulong)) return reader.ReadUInt64();
            if (type == typeof(float)) return reader.ReadSingle();
            if (type == typeof(double)) return reader.ReadDouble();
            if (type == typeof(decimal)) return reader.ReadDecimal();
            if (type == typeof(char)) return reader.ReadChar();
            if (type.IsEnum)
            {
                long numeric = reader.ReadInt64();
                object e = Enum.ToObject(type, numeric);
                if (!Enum.IsDefined(type, e) &&
                    !Attribute.IsDefined(type, typeof(FlagsAttribute)))
                    throw new FormatException("저장본 열거형 값이 유효하지 않습니다.");
                return e;
            }
            if (type.IsArray || (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                int count = ReadCount(reader);
                Type element = type.IsArray ? type.GetElementType() :
                    type.GetGenericArguments()[0];
                if (type.IsArray)
                {
                    Array array = Array.CreateInstance(element, count);
                    for (int i = 0; i < count; i++)
                        array.SetValue(Read(reader, element, depth + 1), i);
                    return array;
                }
                var list = (IList)Activator.CreateInstance(type);
                for (int i = 0; i < count; i++)
                    list.Add(Read(reader, element, depth + 1));
                return list;
            }
            if (typeof(IDictionary).IsAssignableFrom(type) && type.IsGenericType &&
                type.GetGenericArguments().Length == 2)
            {
                int count = ReadCount(reader);
                var dict = (IDictionary)Activator.CreateInstance(type);
                var generics = type.GetGenericArguments();
                for (int i = 0; i < count; i++)
                {
                    object key = Read(reader, generics[0], depth + 1);
                    object val = Read(reader, generics[1], depth + 1);
                    if (key == null || dict.Contains(key))
                        throw new FormatException("중복·누락된 저장본 키가 있습니다.");
                    dict.Add(key, val);
                }
                return dict;
            }
            if ((!type.IsClass && !type.IsValueType) || type.IsAbstract ||
                type.IsInterface || !type.IsSerializable)
                throw new FormatException("직렬화할 수 없는 저장본 형식: " + type.FullName);
            object instance = Activator.CreateInstance(type);
            var declaredFields = Fields(type);
            int fieldsCount = ReadCount(reader);
            if (fieldsCount != declaredFields.Length)
                throw new FormatException("저장본 필드 구성에 차이가 있습니다: " + type.Name);
            var map = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (var field in declaredFields) map.Add(field.Name, field);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fieldsCount; i++)
            {
                string key = reader.ReadString();
                FieldInfo field;
                if (!seen.Add(key) || !map.TryGetValue(key, out field))
                    throw new FormatException("저장본 필드가 중복되거나 알려지지 않았습니다.");
                int length = reader.ReadInt32();
                if (length < 0 || length > MaxBytes ||
                    length > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new FormatException("저장본 필드 길이가 잘못됐습니다.");
                byte[] bytes = reader.ReadBytes(length);
                if (bytes.Length != length)
                    throw new EndOfStreamException();
                using (var fragment = new MemoryStream(bytes, false))
                using (var nested = new BinaryReader(fragment, Encoding.UTF8))
                {
                    object value = Read(nested, field.FieldType, depth + 1);
                    if (fragment.Position != fragment.Length)
                        throw new FormatException("저장본 필드에 추가 데이터가 있습니다.");
                    field.SetValue(instance, value);
                }
            }
            return instance;
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxItems)
                throw new FormatException("저장본 항목 수가 잘못됐습니다.");
            return count;
        }
    }
}
