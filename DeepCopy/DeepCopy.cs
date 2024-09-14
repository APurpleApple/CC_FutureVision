using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Myosotis.DeepCopy
{
    public static class DeepCopy
    {
        private static Dictionary<Type, FieldInfo[]> copyCache = new();
        private static HashSet<Type> copyTypeSkip = new();
        private static Dictionary<Type, bool> valueTypes = new();
        private static Dictionary<Type, bool> parameterlessConstructor = new();
        private static Dictionary<int, object> magicTransposer = new();

        public static void Init()
        {
            copyTypeSkip.Add(typeof(Dictionary<,>.KeyCollection));
            copyTypeSkip.Add(typeof(Dictionary<,>.ValueCollection));
            copyTypeSkip.Add(typeof(IEqualityComparer<>));
        }

        private static HashSet<Type> seenTypes = new();

        public static Array CopyArray(Array thing)
        {
            Array result = Array.CreateInstance(thing.GetType().GetElementType()!, thing.Length);
            for (int i = 0; i < thing.Length; i++)
            {
                result.SetValue(Copy(thing.GetValue(i)), i);
            }
            return result;
        }

        public static bool IsPureValueType(Type t)
        {
            FieldInfo[] fields = GetFieldsToCache(t).ToArray();
            foreach (FieldInfo field in fields)
            {
                if (!field.FieldType.IsValueType)
                {
                    return false;
                }
            }

            return true;
        }

        public static T? TryTranspose<T>(T thing)
        {
            if (thing == null) return default(T);

            int hashCode = thing.GetHashCode();
            if (magicTransposer.ContainsKey(thing.GetHashCode()))
            {
                object transposition = magicTransposer[hashCode];
                if (transposition.GetType() == thing.GetType())
                {
                    return (T)transposition;
                }
            }

            return default(T);
        }

        public static T Copy<T>(T thing)
        {
            //return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(thing, JSON.settingDefault), JSON.settingDefault);
            if (thing == null) return thing;
            if (thing is Type)
            {
                return thing;
            }

            Type typeT = thing.GetType();

            if (typeT.IsValueType)
            {
                if (!valueTypes.ContainsKey(typeT))
                {
                    valueTypes.Add(typeT, IsPureValueType(typeT));
                }

                if (valueTypes[typeT]) return thing;
            }

            if (typeT.IsArray)
            {
                dynamic at = CopyArray((thing as Array)!);
                return at;
            }

            if (typeT == typeof(string))
            {
                return thing;
            }

            if (!copyCache.ContainsKey(typeT))
            {
                CacheTypeFieldInfos(typeT);
            }

            T result;
            if (parameterlessConstructor[typeT]){
                result = (T)Activator.CreateInstance(typeT)!;
            }
            else
            {
                result = (T)RuntimeHelpers.GetUninitializedObject(typeT);
            }

            FieldInfo[] flds = copyCache[typeT];
            if (seenTypes.Contains(typeT))
            {
                //throw new Exception("Recursive loop found during copy!");
            }
            seenTypes.Add(typeT);
            foreach (var field in flds)
            { 
                if (field.GetCustomAttribute<CopyByRefAttribute>() != null)
                {
                    field.SetValue(result, field.GetValue(thing));
                }
                else
                {
                    field.SetValue(result, Copy(field.GetValue(thing)));
                }
            }
            seenTypes.Remove(typeT);

            magicTransposer[thing.GetHashCode()] = result!;
            return result;
        }
        public static void CacheTypeFieldInfos(Type t)
        {
            FieldInfo[] fields = GetFieldsToCache(t).ToArray();
            copyCache.Add(t, fields);
        }

        public static IEnumerable<FieldInfo> GetFieldsToCache(Type t)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            if (t.GetConstructor(Type.EmptyTypes) != null)
            {
                parameterlessConstructor[t] = true;
            }
            else
            {
                parameterlessConstructor[t] = false;
            }

            if (t.BaseType == null)
            {
                return t.GetFields(flags).Where(f =>
                f.GetCustomAttribute<JsonIgnoreAttribute>() == null &&
                !(f.FieldType.IsGenericType && copyTypeSkip.Contains(f.FieldType.GetGenericTypeDefinition())) &&
                !copyTypeSkip.Contains(f.FieldType) &&
                !f.IsInitOnly
                );
            }
            else
            {
                return t.GetFields(flags).Where(f =>
                f.GetCustomAttribute<JsonIgnoreAttribute>() == null &&
                !(f.FieldType.IsGenericType && copyTypeSkip.Contains(f.FieldType.GetGenericTypeDefinition())) &&
                !copyTypeSkip.Contains(f.FieldType) &&
                !f.IsInitOnly
                ).Concat(GetFieldsToCache(t.BaseType));
            }
        }
    }

    public class CopyByRefAttribute : Attribute
    {

    }

    public class CopyIgnoreAttribute : Attribute
    {

    }
}
