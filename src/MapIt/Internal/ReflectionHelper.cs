using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Reflection and type utilities used by the mapping engine.
    /// </summary>
    public static class ReflectionHelper
    {
        /// <summary>Whether a member can be set (has a public setter).</summary>
        public static bool CanBeSet(MemberInfo member)
        {
            if (member is PropertyInfo p)
                return p.CanWrite && p.GetSetMethod(true) != null && p.GetSetMethod(true).IsPublic;
            if (member is FieldInfo f)
                return f.IsPublic && !f.IsInitOnly;
            return false;
        }

        /// <summary>Whether a member has a given attribute type.</summary>
        public static bool HasAttribute(MemberInfo member, Type attributeType) =>
            member?.GetCustomAttribute(attributeType) != null;

        // Compiled getter/setter caches: avoids per-call reflection on hot paths.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<MemberInfo, Func<object, object>> _getters
            = new System.Collections.Concurrent.ConcurrentDictionary<MemberInfo, Func<object, object>>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<MemberInfo, Action<object, object>> _setters
            = new System.Collections.Concurrent.ConcurrentDictionary<MemberInfo, Action<object, object>>();

        /// <summary>Gets a cached, compiled getter delegate for a property or field (null for methods).</summary>
        public static Func<object, object> GetCompiledGetter(MemberInfo member)
        {
            if (member is MethodInfo) return null;   // extension/Get methods use the reflection path
            return _getters.GetOrAdd(member, BuildGetter);
        }

        /// <summary>Gets a cached, compiled setter delegate for a settable property or field.</summary>
        public static Action<object, object> GetCompiledSetter(MemberInfo member)
            => _setters.GetOrAdd(member, BuildSetter);

        private static Func<object, object> BuildGetter(MemberInfo member)
        {
            try
            {
                var target = Expression.Parameter(typeof(object), "t");
                Expression instance = Expression.Convert(target, member.DeclaringType);
                Expression access;
                if (member is PropertyInfo p)
                {
                    if (p.GetGetMethod(true) == null) return null;
                    access = Expression.Property(instance, p);
                }
                else if (member is FieldInfo f)
                {
                    access = Expression.Field(instance, f);
                }
                else return null;
                var body = Expression.Convert(access, typeof(object));
                return Expression.Lambda<Func<object, object>>(body, target).Compile();
            }
            catch { return null; }
        }

        private static Action<object, object> BuildSetter(MemberInfo member)
        {
            try
            {
                var target = Expression.Parameter(typeof(object), "t");
                var value = Expression.Parameter(typeof(object), "v");
                Expression instance = Expression.Convert(target, member.DeclaringType);
                Type memberType;
                Expression assignTarget;
                if (member is PropertyInfo p)
                {
                    if (p.GetSetMethod(true) == null) return null;
                    memberType = p.PropertyType;
                    assignTarget = Expression.Property(instance, p);
                }
                else if (member is FieldInfo f)
                {
                    if (f.IsInitOnly || f.IsLiteral) return null;
                    memberType = f.FieldType;
                    assignTarget = Expression.Field(instance, f);
                }
                else return null;
                var typedValue = Expression.Convert(value, memberType);
                var body = Expression.Assign(assignTarget, typedValue);
                return Expression.Lambda<Action<object, object>>(body, target, value).Compile();
            }
            catch { return null; }
        }

        /// <summary>Gets a member value from a target object.</summary>
        public static object GetMemberValue(MemberInfo member, object target)
        {
            if (target == null) return null;
            // Fast path: compiled getter for properties/fields.
            if (!(member is MethodInfo))
            {
                var getter = GetCompiledGetter(member);
                if (getter != null) return getter(target);
            }
            try
            {
                if (member is PropertyInfo p)
                    return p.GetValue(target);
                if (member is FieldInfo f)
                    return f.GetValue(target);
                if (member is MethodInfo m)
                    return m.IsStatic
                        ? m.Invoke(null, new[] { target })   // extension method: source is the first argument
                        : m.Invoke(target, null);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
            return null;
        }

        /// <summary>Sets a member value on a target object.</summary>
        public static void SetMemberValue(MemberInfo member, object target, object value)
        {
            if (target == null) return;
            // Fast path: compiled setter.
            var setter = GetCompiledSetter(member);
            if (setter != null)
            {
                setter(target, value);
                return;
            }
            try
            {
                if (member is PropertyInfo p)
                    p.SetValue(target, value);
                else if (member is FieldInfo f)
                    f.SetValue(target, value);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        /// <summary>Gets the type of a member.</summary>
        public static Type GetMemberType(MemberInfo member)
        {
            if (member is PropertyInfo p) return p.PropertyType;
            if (member is FieldInfo f) return f.FieldType;
            if (member is MethodInfo m) return m.ReturnType;
            return null;
        }

        /// <summary>Gets the element type of a collection type.</summary>
        public static Type GetEnumerableElementType(Type type)
        {
            if (type == null) return null;
            if (type.IsArray) return type.GetElementType();
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];
            return null;
        }

        /// <summary>Gets the element type of a collection type (including arrays).</summary>
        public static Type GetElementType(Type type)
        {
            if (type == null) return null;
            if (type.IsArray) return type.GetElementType();
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType)
                {
                    var def = iface.GetGenericTypeDefinition();
                    if (def == typeof(IEnumerable<>) || def == typeof(IReadOnlyCollection<>) || def == typeof(IDictionary<,>))
                        return iface.GetGenericArguments()[0];
                }
            }
            return null;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> _isCollection
            = new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

        /// <summary>Whether a type is a collection type (cached per type).</summary>
        public static bool IsCollection(Type type)
        {
            if (type == null || type == typeof(string)) return false;
            if (type.IsArray) return true;
            return _isCollection.GetOrAdd(type, t => t.GetInterfaces().Any(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IEnumerable<>) || i.GetGenericTypeDefinition() == typeof(IDictionary<,>))));
        }

        /// <summary>Whether a type is a nullable type.</summary>
        public static bool IsNullableType(Type type) =>
            type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

        /// <summary>Gets the underlying type of a nullable type.</summary>
        public static Type GetUnderlyingNullableType(Type type) =>
            IsNullableType(type) ? type.GetGenericArguments()[0] : type;

        /// <summary>Gets the base classes and interfaces of a type.</summary>
        public static IEnumerable<Type> BaseClassesAndInterfaces(Type type)
        {
            var result = new List<Type>();
            var current = type;
            while (current != null && current != typeof(object))
            {
                result.Add(current);
                foreach (var i in current.GetInterfaces())
                    result.Add(i);
                current = current.BaseType;
            }
            return result;
        }

        /// <summary>Gets the type inheritance chain.</summary>
        public static IEnumerable<Type> GetTypeInheritance(Type type)
        {
            var result = new List<Type>();
            var current = type;
            while (current != null)
            {
                result.Add(current);
                current = current.BaseType;
            }
            return result;
        }

        /// <summary>Gets a field or property by name.</summary>
        public static MemberInfo GetFieldOrProperty(Type type, string name)
        {
            var prop = type?.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (prop != null) return prop;
            return type?.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        }

        /// <summary>Creates an instance of a type.</summary>
        public static object CreateInstance(Type type)
        {
            if (type == null) return null;
            if (type.IsValueType) return Activator.CreateInstance(type);
            if (type.IsArray) return Activator.CreateInstance(type, 0);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) return null;
            if (type == typeof(string)) return null;
            if (type.IsAbstract)
            {
                // Find a concrete subclass
                var concrete = type.Assembly.GetTypes().FirstOrDefault(t => t.IsClass && !t.IsAbstract && type.IsAssignableFrom(t));
                if (concrete == null) throw new MapItMappingException($"Cannot create instance of abstract type {type.Name}");
                return Activator.CreateInstance(concrete);
            }
            return Activator.CreateInstance(type);
        }

        /// <summary>Gets the default value for a type.</summary>
        public static object GetDefaultValue(Type type)
        {
            if (type == null) return null;
            if (type.IsValueType) return Activator.CreateInstance(type);
            return null;
        }
    }

    /// <summary>
    /// Type extensions for the mapping engine.
    /// </summary>
    public static class TypeExtensions
    {
        /// <summary>Whether a type is a list-like type (has Add).</summary>
        public static bool IsListType(this Type type)
        {
            if (!ReflectionHelper.IsCollection(type)) return false;
            return type.GetMethod("Add", new[] { ReflectionHelper.GetEnumerableElementType(type) }) != null
                || (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>)));
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> _isDictionary
            = new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

        /// <summary>Whether a type is a dictionary-like type (cached per type).</summary>
        public static bool IsDictionaryType(this Type type)
        {
            if (type == null) return false;
            return _isDictionary.GetOrAdd(type, t =>
                t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)));
        }

        /// <summary>Whether a type is a key-value pair type.</summary>
        public static bool IsKeyValue(this Type type)
        {
            if (type == null) return false;
            var name = type.Name;
            return name == "KeyValuePair`2"
                || (type.IsGenericType && name.StartsWith("KeyValuePair"));
        }
    }
}
