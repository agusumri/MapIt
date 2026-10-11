using System;
using System.Collections.Generic;
using System.Linq;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class EdgeCaseTests
    {
        public class A
        {
            public string Name { get; set; }
            public B B { get; set; }
            public List<C> Cs { get; set; }
        }

        public class B
        {
            public string Name { get; set; }
        }

        public class C
        {
            public int Value { get; set; }
        }

        public class ADto
        {
            public string Name { get; set; }
            public BDto B { get; set; }
            public List<CDto> Cs { get; set; }
        }

        public class BDto
        {
            public string Name { get; set; }
        }

        public class CDto
        {
            public int Value { get; set; }
        }

        [Fact]
        public void Deeply_nested_collection_of_objects()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<B, BDto>();
                cfg.CreateMap<C, CDto>();
                cfg.CreateMap<A, ADto>();
            });
            var mapper = config.CreateMapper();

            var source = new A
            {
                Name = "root",
                B = new B { Name = "b" },
                Cs = new List<C> { new C { Value = 1 }, new C { Value = 2 }, new C { Value = 3 } }
            };
            var result = mapper.Map<ADto>(source);

            Assert.Equal("root", result.Name);
            Assert.Equal("b", result.B.Name);
            Assert.Equal(3, result.Cs.Count);
            Assert.Equal(new[] { 1, 2, 3 }, result.Cs.Select(c => c.Value).ToArray());
        }

        [Fact]
        public void Null_collection_maps_to_empty_by_default()
        {
            // AllowNullCollections defaults to false, so a null source collection maps to an
            // empty collection rather than null.
            var config = new MapperConfiguration(cfg => cfg.CreateMap<C, CDto>());
            var mapper = config.CreateMapper();

            var result = mapper.Map<List<C>, List<CDto>>(null);
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void Null_collection_maps_to_null_when_allowed()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.AllowNullCollections = true;
                cfg.CreateMap<C, CDto>();
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<List<C>, List<CDto>>(null);
            Assert.Null(result);
        }

        [Fact]
        public void IEnumerable_source_maps_to_list_destination()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<C, CDto>());
            var mapper = config.CreateMapper();

            IEnumerable<C> source = new C[] { new C { Value = 5 } };
            var result = mapper.Map<List<CDto>>(source);

            Assert.Single(result);
            Assert.Equal(5, result[0].Value);
        }

        [Fact]
        public void ReverseMap_and_forward_map_both_work()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<B, BDto>().ReverseMap());
            var mapper = config.CreateMapper();

            var b = mapper.Map<B>(new BDto { Name = "x" });
            Assert.Equal("x", b.Name);

            var bDto = mapper.Map<BDto>(new B { Name = "y" });
            Assert.Equal("y", bDto.Name);
        }

        [Fact]
        public void Nullable_value_type_member()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<N, NDto>());
            var mapper = config.CreateMapper();

            var withValue = mapper.Map<NDto>(new N { Count = 42 });
            Assert.Equal(42, withValue.Count);

            var withoutValue = mapper.Map<NDto>(new N { Count = null });
            Assert.Null(withoutValue.Count);
        }

        public class N
        {
            public int? Count { get; set; }
        }

        public class NDto
        {
            public int? Count { get; set; }
        }

        [Fact]
        public void Map_onto_existing_destination_updates_members()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<C, CDto>());
            var mapper = config.CreateMapper();

            var dest = new CDto { Value = 999 };
            var result = mapper.Map<C, CDto>(new C { Value = 7 }, dest);

            Assert.Same(dest, result);
            Assert.Equal(7, result.Value);
        }

        [Fact]
        public void Same_type_map_returns_source()
        {
            // Convention-only C->C is a pure pass-through (BEHAVIOR §6).
            var config = new MapperConfiguration(cfg => cfg.CreateMap<C, C>());
            var mapper = config.CreateMapper();

            var source = new C { Value = 10 };
            var result = mapper.Map<C>(source);

            Assert.Same(source, result);
        }

        // ---- Section 1 safety guarantees ----

        public class SelfRef
        {
            public string Name { get; set; }
            public SelfRef Child { get; set; }
        }

        public class SelfRefDto
        {
            public string Name { get; set; }
            public SelfRefDto Child { get; set; }
        }

        [Fact]
        public void Depth_cap_throws_documented_message_not_stackoverflow()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.MaxGraphDepth = 4;
                // Explicit MaxDepth very high so the self-reference default does not kick in and
                // truncate before the graph-depth cap is reached.
                cfg.CreateMap<SelfRef, SelfRefDto>().MaxDepth(1000);
            });
            var mapper = config.CreateMapper();

            // Build a chain deeper than the cap.
            var root = new SelfRef { Name = "0" };
            var cur = root;
            for (var i = 1; i < 20; i++) { cur.Child = new SelfRef { Name = i.ToString() }; cur = cur.Child; }

            var ex = Assert.Throws<MapItMappingException>(() => mapper.Map<SelfRefDto>(root));
            Assert.Contains("depth", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Self_referential_type_gets_maxdepth_and_preserve_references()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<SelfRef, SelfRefDto>());
            var map = ((IGlobalConfiguration)config).FindTypeMapFor(typeof(SelfRef), typeof(SelfRefDto));
            Assert.Equal(64, map.MaxDepth);
            Assert.True(map.PreserveReferences);
        }

        [Fact]
        public void Explicit_maxdepth_wins_over_self_reference_default()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<SelfRef, SelfRefDto>().MaxDepth(3));
            var map = ((IGlobalConfiguration)config).FindTypeMapFor(typeof(SelfRef), typeof(SelfRefDto));
            Assert.Equal(3, map.MaxDepth);
        }

        private class InfiniteSource : IEnumerable<C>
        {
            public IEnumerator<C> GetEnumerator()
            {
                while (true) yield return new C { Value = 1 };
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Fact]
        public void Collection_budget_throws_on_infinite_source()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.MaxCollectionItems = 100;
                cfg.CreateMap<C, CDto>();
            });
            var mapper = config.CreateMapper();

            var ex = Assert.Throws<MapItMappingException>(() => mapper.Map<List<CDto>>(new InfiniteSource()));
            Assert.Contains("budget", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        public class OnlyComplex { public B Value { get; set; } }
        public class OnlyComplexDto { public BDto Value { get; set; } }

        [Fact]
        public void Missing_map_throws()
        {
            // Map a complex pair with no configured map and no built-in conversion.
            var config = new MapperConfiguration(cfg => { });
            var mapper = config.CreateMapper();

            Assert.Throws<MapItMappingException>(() => mapper.Map<B, BDto>(new B { Name = "x" }));
        }

        // ---- ContextCacheKey identity (S4) ----

        private sealed class EqualitySensitiveSource
        {
            public string Name { get; set; }
            public override bool Equals(object obj) => throw new InvalidOperationException("Equals must not be called by the cache.");
            public override int GetHashCode() => throw new InvalidOperationException("GetHashCode must not be called by the cache.");
        }

        [Fact]
        public void ContextCacheKey_uses_reference_identity_not_user_equality()
        {
            var a = new EqualitySensitiveSource { Name = "a" };
            var b = new EqualitySensitiveSource { Name = "b" };

            var keyA1 = new ContextCacheKey(a, typeof(object));
            var keyA2 = new ContextCacheKey(a, typeof(object));
            var keyB = new ContextCacheKey(b, typeof(object));

            // No throw, correct identity semantics.
            var _ = keyA1.GetHashCode();
            Assert.True(keyA1.Equals(keyA2));
            Assert.False(keyA1.Equals(keyB));
        }
    }
}
