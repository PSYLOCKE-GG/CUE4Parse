using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;

namespace CUE4Parse.UE4.Assets.Utils
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class StructFallback : Attribute { }

    public static class StructFallbackUtil
    {
        public static ObjectMapper? ObjectMapper = new DefaultObjectMapper();

        private static readonly ConcurrentDictionary<Type, Func<FStructFallback, object>?> ConstructorCache = new();

        private static Func<FStructFallback, object>? BuildConstructorDelegate(Type type)
        {
            var ctor = type.GetConstructor(new[] { typeof(FStructFallback) });
            if (ctor == null) return null;
            var param = Expression.Parameter(typeof(FStructFallback), "fallback");
            var newExpr = Expression.New(ctor, param);
            var cast = Expression.Convert(newExpr, typeof(object));
            return Expression.Lambda<Func<FStructFallback, object>>(cast, param).Compile();
        }

        public static object? MapToClass(this FStructFallback? fallback, Type type)
        {
            if (fallback == null)
            {
                return null;
            }

            var factory = ConstructorCache.GetOrAdd(type, static t => BuildConstructorDelegate(t));
            if (factory != null)
            {
                return factory(fallback);
            }

            var value = Activator.CreateInstance(type);
            ObjectMapper?.Map(fallback, value);
            return value;
        }
    }

    public abstract class ObjectMapper
    {
        public abstract void Map(IPropertyHolder src, object dst);
    }

    public class DefaultObjectMapper : ObjectMapper
    {
        public override void Map(IPropertyHolder src, object dst)
        {
            // TODO
        }
    }
}