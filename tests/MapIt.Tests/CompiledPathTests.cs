using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    /// <summary>
    /// Edge cases for the compiled whole-graph path used by plain <c>mapper.Map</c> calls. Each test
    /// asserts the documented engine behavior, so the compiled path cannot silently diverge.
    /// </summary>
    public class CompiledPathTests
    {
        public class Address { public string City { get; set; } }
        public class Line { public int Qty { get; set; } public string Sku { get; set; } }
        public class Order
        {
            public int Id { get; set; }
            public int? Discount { get; set; }
            public int Count { get; set; }
            public Address Ship { get; set; }
            public List<Line> Lines { get; set; }
        }

        public class AddressDto { public string City { get; set; } }
        public class LineDto { public int Qty { get; set; } public string Sku { get; set; } }
        public class OrderDto
        {
            public int Id { get; set; }
            public int Discount { get; set; }          // int? -> int
            public long Count { get; set; }            // int -> long (widening)
            public string ShipCity { get; set; }       // flattened Ship.City
            public AddressDto Ship { get; set; }
            public List<LineDto> Lines { get; set; }
        }

        private static IMapper CreateMapper(Action<IMapperConfigurationExpression> extra = null) =>
            new MapperConfiguration(cfg =>
            {
                extra?.Invoke(cfg);
                cfg.CreateMap<Order, OrderDto>();
                cfg.CreateMap<Address, AddressDto>();
                cfg.CreateMap<Line, LineDto>();
            }).CreateMapper();

        [Fact]
        public void Maps_full_graph()
        {
            var dto = CreateMapper().Map<OrderDto>(new Order
            {
                Id = 7, Discount = 3, Count = 2,
                Ship = new Address { City = "Oslo" },
                Lines = new List<Line> { new Line { Qty = 1, Sku = "a" }, new Line { Qty = 2, Sku = "b" } }
            });

            Assert.Equal(7, dto.Id);
            Assert.Equal(3, dto.Discount);
            Assert.Equal(2L, dto.Count);
            Assert.Equal("Oslo", dto.ShipCity);
            Assert.Equal("Oslo", dto.Ship.City);
            Assert.Equal(new[] { "a", "b" }, dto.Lines.Select(l => l.Sku));
            Assert.Equal(new[] { 1, 2 }, dto.Lines.Select(l => l.Qty));
        }

        [Fact]
        public void Null_members_follow_engine_rules()
        {
            var dto = CreateMapper().Map<OrderDto>(new Order { Discount = null, Ship = null, Lines = null });

            Assert.Equal(0, dto.Discount);     // null int? -> default int
            Assert.Null(dto.ShipCity);         // null intermediate in a flattened chain
            Assert.Null(dto.Ship);             // null nested object stays null
            Assert.NotNull(dto.Lines);         // null collection -> empty by default
            Assert.Empty(dto.Lines);
        }

        [Fact]
        public void Null_collection_stays_null_when_allowed()
        {
            var dto = CreateMapper(cfg => cfg.AllowNullCollections = true).Map<OrderDto>(new Order { Lines = null });
            Assert.Null(dto.Lines);
        }

        [Fact]
        public void Null_elements_stay_null()
        {
            var dto = CreateMapper().Map<OrderDto>(new Order { Lines = new List<Line> { null, new Line { Sku = "x" } } });
            Assert.Null(dto.Lines[0]);
            Assert.Equal("x", dto.Lines[1].Sku);
        }

        public class ArraySource { public Line[] Items { get; set; } public IEnumerable<Line> Lazy { get; set; } public List<string> Tags { get; set; } }
        public class ArrayDest { public LineDto[] Items { get; set; } public IReadOnlyList<LineDto> Lazy { get; set; } public string[] Tags { get; set; } }

        private static IEnumerable<Line> Yield()
        {
            yield return new Line { Sku = "y1" };
            yield return new Line { Sku = "y2" };
        }

        [Fact]
        public void Maps_arrays_enumerables_and_leaf_lists()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<ArraySource, ArrayDest>();
                cfg.CreateMap<Line, LineDto>();
            }).CreateMapper();

            var dto = mapper.Map<ArrayDest>(new ArraySource
            {
                Items = new[] { new Line { Sku = "i1" } },
                Lazy = Yield(),
                Tags = new List<string> { "t1", "t2" }
            });

            Assert.Equal("i1", Assert.Single(dto.Items).Sku);
            Assert.Equal(new[] { "y1", "y2" }, dto.Lazy.Select(l => l.Sku));
            Assert.Equal(new[] { "t1", "t2" }, dto.Tags);
        }

        public class Shared { public string Name { get; set; } }
        public class Holder { public Shared Item { get; set; } }
        public class HolderDto { public Shared Item { get; set; } }

        [Fact]
        public void Same_type_member_is_copied_by_reference()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Holder, HolderDto>()).CreateMapper();
            var shared = new Shared { Name = "s" };
            Assert.Same(shared, mapper.Map<HolderDto>(new Holder { Item = shared }).Item);
        }

        public class BaseItem { public string Name { get; set; } }
        public class DerivedItem : BaseItem { public string Extra { get; set; } }
        public class BaseItemDto { public string Name { get; set; } }
        public class Box { public BaseItem Item { get; set; } }
        public class BoxDto { public BaseItemDto Item { get; set; } }

        [Fact]
        public void Derived_runtime_instance_uses_the_engine()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Box, BoxDto>();
                cfg.CreateMap<BaseItem, BaseItemDto>();
            }).CreateMapper();

            // Runtime type differs from the static member type: the base-type fallback must still apply.
            var dto = mapper.Map<BoxDto>(new Box { Item = new DerivedItem { Name = "d", Extra = "e" } });
            Assert.Equal("d", dto.Item.Name);
        }

        public class Upper : IValueResolver<Address, AddressDto, string>
        {
            public string Resolve(Address source, AddressDto destination, string destMember, ResolutionContext context)
            {
                // Resolvers get a working context and nested mapper even on the compiled path.
                var nested = context.Mapper.Map<LineDto>(new Line { Sku = "n" });
                return source.City.ToUpperInvariant() + nested.Sku;
            }
        }

        [Fact]
        public void Resolver_members_fall_back_with_a_working_context()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Order, OrderDto>();
                cfg.CreateMap<Address, AddressDto>().ForMember(d => d.City, o => o.MapFrom(new Upper()));
                cfg.CreateMap<Line, LineDto>();
            }).CreateMapper();

            var dto = mapper.Map<OrderDto>(new Order { Ship = new Address { City = "oslo" } });
            Assert.Equal("OSLOn", dto.Ship.City);
        }

        public class Tracked { public string Name { get; set; } }
        public class TrackedDto { public string Name { get; set; } public string SeenBefore { get; set; } public string SeenAfter { get; set; } }

        [Fact]
        public void Before_and_after_map_actions_see_the_right_state()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Tracked, TrackedDto>()
                    .ForMember(d => d.SeenBefore, o => o.Ignore())
                    .ForMember(d => d.SeenAfter, o => o.Ignore())
                    .BeforeMap((s, d) => d.SeenBefore = d.Name ?? "<unmapped>")
                    .AfterMap((s, d) => d.SeenAfter = d.Name)).CreateMapper();

            var dto = mapper.Map<TrackedDto>(new Tracked { Name = "n" });
            Assert.Equal("<unmapped>", dto.SeenBefore);
            Assert.Equal("n", dto.SeenAfter);
        }

        public class L1 { public L2 Next { get; set; } }
        public class L2 { public L3 Next { get; set; } }
        public class L3 { public string Value { get; set; } }
        public class L1Dto { public L2Dto Next { get; set; } }
        public class L2Dto { public L3Dto Next { get; set; } }
        public class L3Dto { public string Value { get; set; } }

        [Fact]
        public void Depth_cap_applies_to_compiled_graphs()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.MaxGraphDepth = 2;
                cfg.CreateMap<L1, L1Dto>();
                cfg.CreateMap<L2, L2Dto>();
                cfg.CreateMap<L3, L3Dto>();
            }).CreateMapper();

            var ex = Assert.Throws<MapItMappingException>(() =>
                mapper.Map<L1Dto>(new L1 { Next = new L2 { Next = new L3 { Value = "v" } } }));
            Assert.Contains("depth", ex.Message, StringComparison.OrdinalIgnoreCase);

            // A graph that fits under the cap still maps.
            var shallow = mapper.Map<L1Dto>(new L1 { Next = new L2() });
            Assert.NotNull(shallow.Next);
        }

        [Fact]
        public void Collection_budget_applies_to_member_collections()
        {
            var mapper = CreateMapper(cfg => cfg.MaxCollectionItems = 3);
            var order = new Order { Lines = Enumerable.Range(0, 5).Select(i => new Line()).ToList() };

            var ex = Assert.Throws<MapItMappingException>(() => mapper.Map<OrderDto>(order));
            Assert.Contains("budget", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        public class Throwing { public string Name => throw new InvalidOperationException("getter failed"); }
        public class ThrowingDto { public string Name { get; set; } }

        [Fact]
        public void Getter_exceptions_surface_as_mapping_exceptions()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Throwing, ThrowingDto>()).CreateMapper();
            var ex = Assert.Throws<MapItMappingException>(() => mapper.Map<ThrowingDto>(new Throwing()));
            Assert.NotNull(ex.Types);
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public void Interceptors_and_options_still_apply()
        {
            var seen = 0;
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.AddInterceptor(new CountingInterceptor(() => seen++));
                cfg.CreateMap<Line, LineDto>();
            }).CreateMapper();

            mapper.Map<LineDto>(new Line { Sku = "a" });
            Assert.Equal(1, seen);

            var after = false;
            mapper.Map<Line, LineDto>(new Line(), o => o.AfterMap((s, d) => after = true));
            Assert.True(after);
        }

        private sealed class CountingInterceptor : MappingInterceptor
        {
            private readonly Action _onBefore;
            public CountingInterceptor(Action onBefore) => _onBefore = onBefore;
            public override object OnBeforeMap(MappingInterceptionContext context) { _onBefore(); return null; }
        }

        [Fact]
        public void Compiled_path_is_thread_safe()
        {
            var mapper = CreateMapper();
            Parallel.For(0, 2000, i =>
            {
                var dto = mapper.Map<OrderDto>(new Order
                {
                    Id = i,
                    Ship = new Address { City = "c" + i },
                    Lines = new List<Line> { new Line { Qty = i } }
                });
                Assert.Equal(i, dto.Id);
                Assert.Equal("c" + i, dto.Ship.City);
                Assert.Equal(i, dto.Lines[0].Qty);
            });
        }
    }
}
