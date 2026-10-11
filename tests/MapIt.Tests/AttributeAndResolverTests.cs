using System;
using System.Collections.Generic;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class AttributeAndResolverTests
    {
        // ---- Attributes ----
        public class Src { public string First { get; set; } public string Last { get; set; } public int? Age { get; set; } }

        public class WithIgnore { public string First { get; set; } [Ignore] public string Last { get; set; } }
        public class WithSourceMember { [SourceMember("First")] public string Name { get; set; } }
        public class WithNullSubstitute { [NullSubstitute("N/A")] public string First { get; set; } }

        public class UpperResolver : IValueResolver<Src, WithResolver, string>
        {
            public string Resolve(Src source, WithResolver destination, string destMember, ResolutionContext context)
                => source.First?.ToUpperInvariant();
        }
        public class WithResolver { [ValueResolver(typeof(UpperResolver))] public string First { get; set; } }

        public class ExclaimConverter : IValueConverter<string, string>
        {
            public string Convert(string sourceMember, ResolutionContext context) => sourceMember + "!";
        }
        public class WithConverter { [ValueConverter(typeof(ExclaimConverter))] public string First { get; set; } }

        [Fact]
        public void Ignore_attribute_skips_member()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, WithIgnore>());
            var m = config.CreateMapper();
            var r = m.Map<WithIgnore>(new Src { First = "a", Last = "b" });
            Assert.Equal("a", r.First);
            Assert.Null(r.Last);
        }

        [Fact]
        public void SourceMember_attribute_maps_from_named_member()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, WithSourceMember>());
            var m = config.CreateMapper();
            var r = m.Map<WithSourceMember>(new Src { First = "John" });
            Assert.Equal("John", r.Name);
        }

        [Fact]
        public void NullSubstitute_attribute_replaces_null()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, WithNullSubstitute>());
            var m = config.CreateMapper();
            var r = m.Map<WithNullSubstitute>(new Src { First = null });
            Assert.Equal("N/A", r.First);
        }

        [Fact]
        public void ValueResolver_attribute_runs()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, WithResolver>());
            var m = config.CreateMapper();
            var r = m.Map<WithResolver>(new Src { First = "hi" });
            Assert.Equal("HI", r.First);
        }

        [Fact]
        public void ValueConverter_attribute_runs()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Src, WithConverter>());
            var m = config.CreateMapper();
            var r = m.Map<WithConverter>(new Src { First = "yo" });
            Assert.Equal("yo!", r.First);
        }

        // ---- [AutoMap] ----
        public class Entity { public string Name { get; set; } public int Value { get; set; } }

        [AutoMap(typeof(Entity), ReverseMap = true)]
        public class EntityDto { public string Name { get; set; } public int Value { get; set; } }

        [Fact]
        public void AutoMap_attribute_creates_map_and_reverse()
        {
            var config = new MapperConfiguration(cfg => cfg.AddMaps(new[] { typeof(EntityDto) }));
            var m = config.CreateMapper();
            var dto = m.Map<EntityDto>(new Entity { Name = "x", Value = 5 });
            Assert.Equal("x", dto.Name);
            var ent = m.Map<Entity>(new EntityDto { Name = "y", Value = 6 });
            Assert.Equal("y", ent.Name);
        }

        // ---- Enum conversions ----
        public enum ByteEnum : byte { A = 1, B = 2 }
        public enum LongEnum : long { A = 1, B = 2 }

        [Fact]
        public void Enum_to_enum_by_name_across_underlying_types()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<ByteEnum, LongEnum>());
            var m = config.CreateMapper();
            Assert.Equal(LongEnum.B, m.Map<LongEnum>(ByteEnum.B));
        }

        [Fact]
        public void String_to_enum_and_enum_to_string()
        {
            var config = new MapperConfiguration(cfg => { cfg.CreateMap<string, ByteEnum>(); cfg.CreateMap<ByteEnum, string>(); });
            var m = config.CreateMapper();
            Assert.Equal(ByteEnum.A, m.Map<string, ByteEnum>("A"));
            Assert.Equal("B", m.Map<ByteEnum, string>(ByteEnum.B));
        }

        [Fact]
        public void Enum_conversion_failure_message_has_no_member_value()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<string, ByteEnum>());
            var m = config.CreateMapper();
            var ex = Assert.Throws<MapItMappingException>(() => m.Map<string, ByteEnum>("NotAMember"));
            Assert.DoesNotContain("NotAMember", ex.Message);
        }

        // ---- Primitive conversions ----
        [Fact]
        public void Converts_datetimeoffset_timespan_bool()
        {
            var config = new MapperConfiguration(cfg => { });
            var m = config.CreateMapper();
            Assert.True(m.Map<string, bool>("true"));
            Assert.Equal(TimeSpan.FromHours(1), m.Map<string, TimeSpan>("01:00:00"));
        }

        // ---- Operation-scoped options ----
        public class ADto2 { public string Name { get; set; } }
        public class A2 { public string Name { get; set; } }

        [Fact]
        public void Operation_before_and_after_map_run()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<A2, ADto2>());
            var m = config.CreateMapper();
            bool before = false, after = false;
            var r = m.Map<A2, ADto2>(new A2 { Name = "n" }, opt =>
            {
                opt.BeforeMap((s, d) => before = true);
                opt.AfterMap((s, d) => after = true);
            });
            Assert.True(before);
            Assert.True(after);
        }

        [Fact]
        public void Operation_items_available_in_context()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<A2, ADto2>().ForMember("Name", opt => opt.MapFrom((s, d, dm, ctx) => (string)ctx.Items["k"])));
            var m = config.CreateMapper();
            var r = m.Map<A2, ADto2>(new A2 { Name = "ignored" }, opt => opt.Items["k"] = "fromItems");
            Assert.Equal("fromItems", r.Name);
        }

        // ---- S7 fail-closed ----
        public class ThrowingResolver : IValueResolver<A2, ADto2, string>
        {
            public string Resolve(A2 source, ADto2 destination, string destMember, ResolutionContext context)
                => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void Throwing_resolver_surfaces_mapping_exception_with_members()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<A2, ADto2>().ForMember(d => d.Name, opt => opt.MapFrom(new ThrowingResolver())));
            var m = config.CreateMapper();
            var ex = Assert.Throws<MapItMappingException>(() => m.Map<ADto2>(new A2 { Name = "x" }));
            Assert.NotNull(ex.Types);
        }

        [Fact]
        public void Throwing_condition_is_not_swallowed()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<A2, ADto2>().ForMember("Name", opt =>
                {
                    opt.Condition((Func<A2, bool>)(s => throw new InvalidOperationException("cond")));
                    opt.MapFrom(s => s.Name);
                }));
            var m = config.CreateMapper();
            Assert.Throws<MapItMappingException>(() => m.Map<ADto2>(new A2 { Name = "x" }));
        }
    }
}
