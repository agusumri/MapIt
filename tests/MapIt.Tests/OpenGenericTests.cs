using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class OpenGenericTests
    {
        public class Source<T> { public T Value { get; set; } public string Name { get; set; } }
        public class Dest<T> { public T Value { get; set; } public string Label { get; set; } }

        [Fact]
        public void Closes_open_map_by_convention()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap(typeof(Source<>), typeof(Dest<>)));
            var mapper = config.CreateMapper();

            var r = mapper.Map<Dest<int>>(new Source<int> { Value = 7, Name = "n" });
            Assert.Equal(7, r.Value);
        }

        public class PassThroughConverter : IValueConverter<string, string>
        {
            public string Convert(string sourceMember, ResolutionContext context) => "converted:" + sourceMember;
        }

        [Fact]
        public void Closes_open_map_with_member_name_recipe()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap(typeof(Source<>), typeof(Dest<>))
                   .ForMember("Label", opt => ((IMemberConfigurationExpression)opt).ConvertUsing(typeof(PassThroughConverter), "Name")));
            var mapper = config.CreateMapper();

            var r = mapper.Map<Dest<int>>(new Source<int> { Value = 3, Name = "hi" });
            Assert.Equal("converted:hi", r.Label);
            Assert.Equal(3, r.Value);
        }

        public class Resolver<T> : IValueResolver<Source<T>, Dest<T>, string>
        {
            public string Resolve(Source<T> source, Dest<T> destination, string destMember, ResolutionContext context)
                => "resolved:" + source.Name;
        }

        [Fact]
        public void Closes_open_map_with_resolver_type()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap(typeof(Source<>), typeof(Dest<>))
                   .ForMember("Label", opt => ((IMemberConfigurationExpression)opt).MapFrom(typeof(Resolver<>))));
            var mapper = config.CreateMapper();

            var r = mapper.Map<Dest<int>>(new Source<int> { Value = 1, Name = "zz" });
            Assert.Equal("resolved:zz", r.Label);
        }

        [Fact]
        public void Concurrent_close_of_same_pair_is_safe()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap(typeof(Source<>), typeof(Dest<>)));
            var mapper = config.CreateMapper();

            Parallel.For(0, 1000, i =>
            {
                var r = mapper.Map<Dest<int>>(new Source<int> { Value = i });
                Assert.Equal(i, r.Value);
            });
        }

        [Fact]
        public void Unclosable_map_throws_documented_exception()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap(typeof(Source<>), typeof(Dest<>))
                   .ForMember("NoSuchMember", opt => ((IMemberConfigurationExpression)opt).MapFrom(typeof(Resolver<>))));
            var mapper = config.CreateMapper();

            Assert.Throws<MapItMappingException>(() => mapper.Map<Dest<int>>(new Source<int> { Value = 1 }));
        }
    }
}
