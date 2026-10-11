using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class SealingTests
    {
        public class Src { public string Name { get; set; } public int Value { get; set; } }
        public class Dst { public string Name { get; set; } public int Value { get; set; } }

        [Fact]
        public void Options_are_settable_from_cfg_and_frozen_on_the_snapshot()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.MaxGraphDepth = 4;
                cfg.MaxCollectionItems = 10;
                cfg.AllowAdditiveTypeMapCreation = true;
            });

            var g = (IGlobalConfiguration)config;
            Assert.Equal(4, g.MaxGraphDepth);
            Assert.Equal(10, g.MaxCollectionItems);
        }

        [Fact]
        public void Default_options_report_documented_defaults()
        {
            var config = new MapperConfiguration(cfg => { });
            var g = (IGlobalConfiguration)config;
            Assert.Equal(256, g.MaxGraphDepth);
            Assert.Equal(1_000_000, g.MaxCollectionItems);
        }

        [Fact]
        public void Post_construction_mutation_has_no_effect()
        {
            var expr = new MapperConfigurationExpression();
            expr.CreateMap<Src, Dst>();
            var config = new MapperConfiguration((IMapperConfigurationExpression)expr);
            var mapper = config.CreateMapper();

            // Mutate the original expression after sealing.
            expr.CreateMap<Dst, Src>();

            // The late-added map is not visible on the sealed snapshot.
            var g = (IGlobalConfiguration)config;
            Assert.Null(g.FindTypeMapFor(typeof(Dst), typeof(Src)));
        }

        [Fact]
        public void Concurrent_map_on_one_mapper_is_safe()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, Dst>());
            var mapper = config.CreateMapper();

            Parallel.For(0, 2000, i =>
            {
                var r = mapper.Map<Dst>(new Src { Name = "n" + i, Value = i });
                Assert.Equal("n" + i, r.Name);
                Assert.Equal(i, r.Value);
            });
        }

        [Fact]
        public void Two_mappers_sharing_one_config_are_safe()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, Dst>());
            var m1 = config.CreateMapper();
            var m2 = config.CreateMapper();

            Parallel.For(0, 2000, i =>
            {
                var mapper = (i % 2 == 0) ? m1 : m2;
                var r = mapper.Map<Dst>(new Src { Value = i });
                Assert.Equal(i, r.Value);
            });
        }

        private sealed class EqualityThrows
        {
            public EqualityThrows Child { get; set; }
            public string Name { get; set; }
            public override bool Equals(object obj) => throw new InvalidOperationException("Equals must not be called by the cache.");
            public override int GetHashCode() => throw new InvalidOperationException("GetHashCode must not be called by the cache.");
        }

        private sealed class EqualityThrowsDto
        {
            public EqualityThrowsDto Child { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public void Preserve_references_cache_never_calls_user_equality()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<EqualityThrows, EqualityThrowsDto>().PreserveReferences());
            var mapper = config.CreateMapper();

            var shared = new EqualityThrows { Name = "shared" };
            var root = new EqualityThrows { Name = "root", Child = shared };

            var ex = Record.Exception(() => mapper.Map<EqualityThrowsDto>(root));
            Assert.Null(ex);
        }
    }
}
