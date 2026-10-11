using System;
using System.Collections.Generic;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class ExtendedFeaturesTests
    {
        public class Src { public string Name { get; set; } public int Value { get; set; } }
        public class Dst { public string Name { get; set; } public int Value { get; set; } }

        [Fact]
        public void TryMap_returns_true_and_result_on_success()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, Dst>());
            var mapper = config.CreateMapper();

            var ok = mapper.TryMap<Src, Dst>(new Src { Name = "n", Value = 5 }, out var result);
            Assert.True(ok);
            Assert.Equal("n", result.Name);
        }

        [Fact]
        public void TryMap_returns_false_on_missing_map()
        {
            var config = new MapperConfiguration(cfg => { });
            var mapper = config.CreateMapper();

            var ok = mapper.TryMap<Src, Dst>(new Src { Name = "n" }, out var result);
            Assert.False(ok);
            Assert.Null(result);
        }

        public class Node { public string Label { get; set; } public Node Child { get; set; } }

        [Fact]
        public void Clone_produces_a_deep_copy()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Node, Node>());
            var mapper = config.CreateMapper();

            var original = new Node { Label = "root", Child = new Node { Label = "child" } };
            var clone = mapper.Clone(original);

            Assert.NotSame(original, clone);
            Assert.NotSame(original.Child, clone.Child);
            Assert.Equal("root", clone.Label);
            Assert.Equal("child", clone.Child.Label);
        }

        [Fact]
        public void MapOrDefault_returns_fallback_for_null_source()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, Dst>());
            var mapper = config.CreateMapper();

            var fallback = new Dst { Name = "fallback" };
            var result = mapper.MapOrDefault<Dst>(null, fallback);
            Assert.Same(fallback, result);
        }

        // ---- Interceptor ----
        private sealed class CountingInterceptor : MappingInterceptor
        {
            public int Before; public int After;
            public override object OnBeforeMap(MappingInterceptionContext context) { Before++; return null; }
            public override object OnAfterMap(MappingInterceptionContext context, object result) { After++; return result; }
        }

        [Fact]
        public void Interceptor_runs_around_top_level_map()
        {
            var interceptor = new CountingInterceptor();
            var config = new MapperConfiguration(cfg =>
            {
                cfg.AddInterceptor(interceptor);
                cfg.CreateMap<Src, Dst>();
            });
            var mapper = config.CreateMapper();

            mapper.Map<Dst>(new Src { Name = "a" });
            Assert.Equal(1, interceptor.Before);
            Assert.Equal(1, interceptor.After);
        }

        private sealed class ShortCircuitInterceptor : MappingInterceptor
        {
            public override object OnBeforeMap(MappingInterceptionContext context)
                => context.DestinationType == typeof(Dst) ? new Dst { Name = "intercepted" } : null;
        }

        [Fact]
        public void Interceptor_can_short_circuit_the_result()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.AddInterceptor(new ShortCircuitInterceptor());
                cfg.CreateMap<Src, Dst>();
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<Dst>(new Src { Name = "real" });
            Assert.Equal("intercepted", result.Name);
        }
    }
}
