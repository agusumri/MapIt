using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MapIt
{
    /// <summary>
    /// Resolves a destination member name to a source member chain using convention rules:
    /// prefix/postfix stripping, name replacement, naming-convention word splitting,
    /// case-insensitive exact match, "Get"-prefixed methods, and PascalCase flattening
    /// (CustomerName -> Customer.Name) to any depth (BEHAVIOR §3).
    /// </summary>
    internal static class MemberNameMatcher
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

        public static MemberInfo[] Match(Type sourceType, string destMemberName, Profile profile)
        {
            if (sourceType == null || string.IsNullOrEmpty(destMemberName)) return null;

            foreach (var candidate in CandidateDestinationNames(destMemberName, profile))
            {
                var chain = ResolveName(sourceType, candidate, profile);
                if (chain != null) return chain;
            }
            return null;
        }

        /// <summary>Produces destination-name candidates after prefix/postfix stripping and replacement.</summary>
        private static IEnumerable<string> CandidateDestinationNames(string name, Profile profile)
        {
            var names = new List<string> { name };

            if (profile != null)
            {
                // ReplaceMemberName substitutions.
                foreach (var repl in profile.MemberNameReplacers)
                {
                    for (var i = names.Count - 1; i >= 0; i--)
                        if (names[i].Contains(repl.Item1))
                            names.Add(names[i].Replace(repl.Item1, repl.Item2));
                }
                // Destination prefix/postfix stripping (recognized on the destination name).
                var stripped = new List<string>();
                foreach (var n in names)
                {
                    foreach (var pre in profile.DestinationPrefixes)
                        if (n.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && n.Length > pre.Length)
                            stripped.Add(n.Substring(pre.Length));
                    foreach (var post in profile.DestinationPostfixes)
                        if (n.EndsWith(post, StringComparison.OrdinalIgnoreCase) && n.Length > post.Length)
                            stripped.Add(n.Substring(0, n.Length - post.Length));
                }
                names.AddRange(stripped);
            }

            return names.Distinct();
        }

        /// <summary>Resolves a single destination-name candidate to a source chain.</summary>
        private static MemberInfo[] ResolveName(Type sourceType, string destName, Profile profile)
        {
            // 1. Direct member on the source (property/field) honoring source prefix/postfix.
            var direct = FindDirectMember(sourceType, destName, profile);
            if (direct != null) return new[] { direct };

            // 2. Cross-convention: split the destination name into words via the destination
            //    convention, then re-join using the source convention's separator (e.g. dest
            //    "FirstName" -> source "first_name").
            var words = SplitWords(destName, profile);
            var sourceConventionName = JoinWithSourceConvention(words, profile);
            if (sourceConventionName != null && !string.Equals(sourceConventionName, destName, StringComparison.OrdinalIgnoreCase))
            {
                var crossMember = FindDirectMember(sourceType, sourceConventionName, profile);
                if (crossMember != null) return new[] { crossMember };
            }

            // 3. Parameterless method, with recognized "Get" prefix (GetTotal -> Total, or Total()).
            var method = FindMethod(sourceType, destName);
            if (method != null) return new[] { method };

            // 4. Flattening: split the destination name into words and walk nested members,
            //    greedy longest-prefix first, to any depth.
            if (words.Length > 1)
            {
                var chain = Flatten(sourceType, words, profile);
                if (chain != null) return chain;
            }

            return null;
        }

        private static string JoinWithSourceConvention(string[] words, Profile profile)
        {
            if (words == null || words.Length == 0) return null;
            var convention = profile?.SourceMemberNamingConvention;
            if (convention == null || convention is ExactMatchNamingConvention) return null;
            var sep = convention.SeparatorCharacter ?? "";
            if (convention is LowerUnderscoreNamingConvention)
                return string.Join("_", words.Select(w => w.ToLowerInvariant()));
            return string.Join(sep, words);
        }

        private static MemberInfo FindDirectMember(Type sourceType, string name, Profile profile)
        {
            var member = FindByName(sourceType, name);
            if (member != null) return member;

            if (profile != null)
            {
                // Source prefix/postfix: the source member may carry a recognized prefix/postfix.
                foreach (var pre in profile.SourcePrefixes)
                {
                    var m = FindByName(sourceType, pre + name);
                    if (m != null) return m;
                }
                foreach (var post in profile.SourcePostfixes)
                {
                    var m = FindByName(sourceType, name + post);
                    if (m != null) return m;
                }
            }
            return null;
        }

        private static MemberInfo FindByName(Type type, string name)
        {
            var prop = type.GetProperties(Flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop != null) return prop;
            var field = type.GetFields(Flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            return field;
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            var methods = type.GetMethods(Flags).Where(m => m.GetParameters().Length == 0 && m.ReturnType != typeof(void) && !m.IsSpecialName);
            // Exact name as a method, or Get-prefixed (GetTotal matches destination "Total").
            return methods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? methods.FirstOrDefault(m => string.Equals(m.Name, "Get" + name, StringComparison.OrdinalIgnoreCase));
        }

        private static string[] SplitWords(string name, Profile profile)
        {
            var convention = profile?.DestinationMemberNamingConvention;
            if (convention != null && !(convention is ExactMatchNamingConvention))
            {
                var split = convention.Split(name);
                if (split != null && split.Length > 0) return split;
            }
            return PascalCaseNamingConvention.SplitCase(name, false);
        }

        /// <summary>
        /// Greedy flattening: group the leading words into a member, recurse into it with the rest.
        /// e.g. ["Customer","Address","City"] on a source whose Customer.Address.City resolves.
        /// </summary>
        private static MemberInfo[] Flatten(Type sourceType, string[] words, Profile profile)
        {
            // Try the longest leading prefix first (greedy).
            for (var take = words.Length - 1; take >= 1; take--)
            {
                var head = string.Concat(words.Take(take));
                var headMember = FindDirectMember(sourceType, head, profile) ?? (MemberInfo)FindMethod(sourceType, head);
                if (headMember == null) continue;

                var headType = ReflectionHelper.GetMemberType(headMember);
                if (headType == null) continue;

                var rest = words.Skip(take).ToArray();
                var restName = string.Concat(rest);

                // Resolve the remainder on the nested type (may itself flatten further).
                var tail = ResolveName(headType, restName, profile);
                if (tail != null)
                    return new[] { headMember }.Concat(tail).ToArray();
            }
            return null;
        }
    }
}
