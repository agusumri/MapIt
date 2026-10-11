using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// A compiled, reflection-free assignment plan for the "simple" members of a type map
    /// (direct same-type / nullable / primitive-widening assignments from a single-member source
    /// chain, with no resolver/condition/transform/null-substitute). Non-simple members are left
    /// to the interpreted <c>MapProperty</c> path. This is what makes the runtime engine fast.
    /// </summary>
    internal sealed class CompiledMapPlan
    {
        private readonly Action<object, object>[] _assignments;

        private CompiledMapPlan(Action<object, object>[] assignments) => _assignments = assignments;

        /// <summary>The property maps that this plan handles (skipped by the interpreted loop).</summary>
        public HashSet<PropertyMap> HandledMembers { get; } = new HashSet<PropertyMap>();

        /// <summary>Applies all compiled assignments from source to destination.</summary>
        public void Apply(object source, object destination)
        {
            for (var i = 0; i < _assignments.Length; i++)
                _assignments[i](source, destination);
        }

        public bool HasAny => _assignments.Length > 0;

        /// <summary>
        /// Builds a plan for the simple members of <paramref name="typeMap"/>. Returns null when the
        /// map has nothing compilable.
        /// </summary>
        public static CompiledMapPlan Build(TypeMap typeMap)
        {
            if (typeMap.SourceType == null || typeMap.DestinationType == null) return null;

            var assignments = new List<Action<object, object>>();
            var handled = new List<PropertyMap>();

            foreach (var pm in typeMap.OrderedPropertyMaps)
            {
                if (!IsSimple(pm)) continue;
                var action = BuildAssignment(pm);
                if (action == null) continue;
                assignments.Add(action);
                handled.Add(pm);
            }

            if (assignments.Count == 0) return null;
            var plan = new CompiledMapPlan(assignments.ToArray());
            foreach (var h in handled) plan.HandledMembers.Add(h);
            return plan;
        }

        private static bool IsSimple(PropertyMap pm)
        {
            if (pm.Ignored) return false;
            if (pm.Resolver != null || pm.AsyncResolver != null) return false;
            if (pm.Condition != null || pm.ConditionDelegate != null) return false;
            if (pm.PreCondition != null || pm.PreConditionDelegate != null) return false;
            if (pm.NullSubstitute != null) return false;
            if (pm.UseDestinationValue == true) return false;
            if (pm.ValueTransformers != null && pm.ValueTransformers.Count > 0) return false;
            if (pm.AllowNull.HasValue) return false;
            // AllowNullDestinationValues = false turns null strings/objects into empty values,
            // which only the interpreter does.
            if (pm.TypeMap?.Profile != null && !pm.TypeMap.Profile.AllowNullDestinationValues
                && pm.DestinationType != null && !pm.DestinationType.IsValueType) return false;
            if (!pm.CanBeSet) return false;
            if (pm.DestinationMember == null) return false;
            // Exactly one source member (field/property), no deep chain, no method/extension.
            if (pm.SourceMembers == null || pm.SourceMembers.Length != 1) return false;
            var sm = pm.SourceMembers[0];
            if (!(sm is PropertyInfo || sm is FieldInfo)) return false;

            // Leaf eligibility (same type, T <-> T?, lossless widening) is decided by
            // FastPlanCompiler.TryBuildLeafAssignment; complex/collection members stay interpreted.
            return true;
        }

        private static Action<object, object> BuildAssignment(PropertyMap pm)
        {
            try
            {
                // Reuse the null-safe, engine-equivalent leaf builder (e.g. null int? -> 0 for int).
                var src = Expression.Parameter(typeof(object), "s");
                var dst = Expression.Parameter(typeof(object), "d");
                var sourceType = pm.TypeMap?.SourceType ?? pm.SourceMembers[0].DeclaringType;
                var destType = pm.TypeMap?.DestinationType ?? pm.DestinationMember.DeclaringType;
                var srcTyped = Expression.Variable(sourceType, "src");
                var dstTyped = Expression.Variable(destType, "dst");

                var leaf = FastPlanCompiler.TryBuildLeafAssignment(srcTyped, dstTyped, pm);
                if (leaf == null) return null;

                var body = Expression.Block(typeof(void), new[] { srcTyped, dstTyped },
                    Expression.Assign(srcTyped, Expression.Convert(src, sourceType)),
                    Expression.Assign(dstTyped, Expression.Convert(dst, destType)),
                    leaf);
                return Expression.Lambda<Action<object, object>>(body, src, dst).Compile();
            }
            catch
            {
                return null;
            }
        }

    }
}
