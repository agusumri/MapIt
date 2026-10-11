using System;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// Builds a representative execution-plan lambda with the signature
    /// (TSource, TDestination, ResolutionContext) whose body dispatches into the reflection
    /// engine via a captured mapper/config (BEHAVIOR §9). This is a reflection-dispatch shim,
    /// not a fully inlined expression tree.
    /// </summary>
    internal static class ExecutionPlanShim
    {
        public static LambdaExpression BuildExecutionPlan(MapperConfiguration configuration, Type sourceType, Type destinationType)
        {
            var typeMap = configuration.FindTypeMapFor(sourceType, destinationType);
            if (typeMap == null)
                throw TypeMap.MissingMapException(sourceType, destinationType);

            var srcParam = Expression.Parameter(sourceType, "src");
            var destParam = Expression.Parameter(destinationType, "dest");
            var ctxParam = Expression.Parameter(typeof(ResolutionContext), "ctx");

            // Body: Shim.Invoke(src, dest, ctx, configuration, sourceType, destinationType)
            var invoke = typeof(ExecutionPlanShim).GetMethod(nameof(Invoke),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            var call = Expression.Call(invoke,
                Expression.Convert(srcParam, typeof(object)),
                Expression.Convert(destParam, typeof(object)),
                ctxParam,
                Expression.Constant(configuration),
                Expression.Constant(sourceType, typeof(Type)),
                Expression.Constant(destinationType, typeof(Type)));

            var body = Expression.Convert(call, destinationType);
            return Expression.Lambda(body, srcParam, destParam, ctxParam);
        }

        private static object Invoke(object source, object destination, ResolutionContext ctx,
            MapperConfiguration configuration, Type sourceType, Type destinationType)
        {
            // Reuse the caller's live context, or build one from a fresh mapper.
            if (ctx != null && ctx.Mapper != null)
                return ctx.Mapper.Map(source, destination, sourceType, destinationType);

            var mapper = configuration.CreateMapper();
            return mapper.Map(source, destination, sourceType, destinationType);
        }
    }
}
