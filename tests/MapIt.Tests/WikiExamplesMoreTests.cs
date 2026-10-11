using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapIt;
using MapIt.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MapIt.Tests.Wiki
{
    // Second batch of wiki examples: the less common options on each page.

    public class MemberOptionDetails
    {
        public class Child { public string Value { get; set; } }
        public class Parent { public string Name { get; set; } public Child Child { get; set; } }
        public class ChildDto { public string Value { get; set; } }
        public class ParentDto { public string Name { get; set; } public ChildDto Child { get; set; } }

        [Fact]
        public void Use_destination_value_keeps_existing_instance()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Parent, ParentDto>().ForMember(d => d.Child, o => o.UseDestinationValue());
                cfg.CreateMap<Child, ChildDto>();
            }).CreateMapper();

            var existingChild = new ChildDto { Value = "old" };
            var dest = new ParentDto { Child = existingChild };
            mapper.Map(new Parent { Name = "p", Child = new Child { Value = "new" } }, dest);

            Assert.Same(existingChild, dest.Child);
            Assert.Equal("new", existingChild.Value);
        }

        public class Ordered
        {
            public static readonly List<string> Log = new List<string>();
            private string _a, _b;
            public string A { get => _a; set { _a = value; Log.Add("A"); } }
            public string B { get => _b; set { _b = value; Log.Add("B"); } }
        }
        public class OrderedSource { public string A { get; set; } public string B { get; set; } }

        [Fact]
        public void Set_mapping_order()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<OrderedSource, Ordered>()
                    .ForMember(d => d.A, o => o.SetMappingOrder(2))
                    .ForMember(d => d.B, o => o.SetMappingOrder(1))).CreateMapper();

            lock (Ordered.Log)
            {
                Ordered.Log.Clear();
                mapper.Map<Ordered>(new OrderedSource { A = "a", B = "b" });
                Assert.Equal(new[] { "B", "A" }, Ordered.Log);
            }
        }

        public class Lazy
        {
            public bool IsLoaded { get; set; }
            private string _details;
            public string Details { get => IsLoaded ? _details : throw new InvalidOperationException("not loaded"); set => _details = value; }
        }
        public class LazyDto { public string Details { get; set; } }

        [Fact]
        public void Pre_condition_skips_reading_the_value()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Lazy, LazyDto>().ForMember(d => d.Details, o => o.PreCondition(s => s.IsLoaded))).CreateMapper();

            Assert.Null(mapper.Map<LazyDto>(new Lazy { IsLoaded = false }).Details);
            Assert.Equal("x", mapper.Map<LazyDto>(new Lazy { IsLoaded = true, Details = "x" }).Details);
        }

        public class Patch { public string Name { get; set; } public string Email { get; set; } }
        public class Profile2 { public string Name { get; set; } public string Email { get; set; } }

        [Fact]
        public void For_all_members_copy_only_non_null()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Patch, Profile2>().ForAllMembers(o => o.Condition((src, dest, value) => value != null))).CreateMapper();

            var existing = new Profile2 { Name = "Ana", Email = "old@example.com" };
            mapper.Map(new Patch { Name = null, Email = "new@example.com" }, existing);

            Assert.Equal("Ana", existing.Name);
            Assert.Equal("new@example.com", existing.Email);
        }

        public class Ticket { public int Id { get; set; } public string InternalNotes { get; set; } }
        public class TicketDto { public int Id { get; set; } }

        [Fact]
        public void For_source_member_do_not_validate()
        {
            var bad = new MapperConfiguration(cfg => cfg.CreateMap<Ticket, TicketDto>(MemberList.Source));
            Assert.Throws<MapItConfigurationException>(() => bad.AssertConfigurationIsValid());

            var ok = new MapperConfiguration(cfg =>
                cfg.CreateMap<Ticket, TicketDto>(MemberList.Source)
                   .ForSourceMember(s => s.InternalNotes, o => o.DoNotValidate()));
            ok.AssertConfigurationIsValid();
        }

        public class Customer2 { public string Name { get; set; } }
        public class Sale { public Customer2 Customer { get; set; } }
        public class SaleDto { public string Buyer { get; set; } }

        [Fact]
        public void Map_from_string_path()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Sale, SaleDto>().ForMember(d => d.Buyer, o => o.MapFrom("Customer.Name"))).CreateMapper();
            Assert.Equal("Ana", mapper.Map<SaleDto>(new Sale { Customer = new Customer2 { Name = "Ana" } }).Buyer);
        }

        public class Person { public string First { get; set; } }
        public class Audit { public string First { get; set; } public string Source { get; set; } public string Stamp { get; set; } }

        public interface IClock { string Now(); }
        public class FixedClock : IClock { public string Now() => "2026-01-01"; }

        public class StampAction : IMappingAction<Person, Audit>
        {
            private readonly IClock _clock;
            public StampAction(IClock clock) => _clock = clock;
            public void Process(Person source, Audit destination, ResolutionContext context) => destination.Stamp = _clock.Now();
        }

        public class SimpleStamp : IMappingAction<Person, Audit>
        {
            public void Process(Person source, Audit destination, ResolutionContext context) => destination.Stamp = "plain";
        }

        [Fact]
        public void After_map_action_types_and_per_call_actions()
        {
            var plain = new MapperConfiguration(cfg =>
                cfg.CreateMap<Person, Audit>().AfterMap<SimpleStamp>()).CreateMapper();
            Assert.Equal("plain", plain.Map<Audit>(new Person()).Stamp);

            var services = new ServiceCollection();
            services.AddSingleton<IClock, FixedClock>();
            services.AddMapIt(cfg => cfg.CreateMap<Person, Audit>().AfterMap<StampAction>());
            using var provider = services.BuildServiceProvider();
            var mapper = provider.GetRequiredService<IMapper>();

            var audit = mapper.Map<Person, Audit>(new Person { First = "A" }, opt => opt.AfterMap((s, d) => d.Source = "import"));
            Assert.Equal("2026-01-01", audit.Stamp);
            Assert.Equal("import", audit.Source);
        }

        public class Entity { public string Name { get; set; } public string Code { get; set; } }
        public class EntityDto { public string Name { get; set; } public string Label { get; set; } }

        [Fact]
        public void Reverse_map_can_be_configured()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Entity, EntityDto>()
                   .ForMember(d => d.Label, o => o.MapFrom(s => s.Code))
                   .ReverseMap()
                   .ForMember(s => s.Code, o => o.MapFrom(d => d.Label))).CreateMapper();
            Assert.Equal("L", mapper.Map<Entity>(new EntityDto { Label = "L" }).Code);
        }

        public class Boom { public string Name { get; set; } }
        public class BoomDto { public string Name { get; set; } }

        [Fact]
        public void Errors_are_wrapped_with_the_original_inside()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Boom, BoomDto>().ForMember(d => d.Name, o => o.MapFrom<string>(s => Fail()))).CreateMapper();
            var ex = Assert.Throws<MapItMappingException>(() => mapper.Map<BoomDto>(new Boom()));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        private static string Fail() => throw new InvalidOperationException("boom");
    }

    public class ConverterAndProfileDetails
    {
        public class CsvConverter : ITypeConverter<string, List<int>>
        {
            public List<int> Convert(string source, List<int> destination, ResolutionContext context)
                => source.Split(',').Select(int.Parse).ToList();
        }

        public class Holder { public string Numbers { get; set; } }
        public class HolderDto { public List<int> Numbers { get; set; } }

        [Fact]
        public void Convert_using_type_and_used_for_nested_members()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<string, List<int>>().ConvertUsing<CsvConverter>();
                cfg.CreateMap<Holder, HolderDto>();
            }).CreateMapper();
            Assert.Equal(new[] { 4, 5 }, mapper.Map<HolderDto>(new Holder { Numbers = "4,5" }).Numbers);
        }

        public class OrderRow { public int Id { get; set; } }
        public class ReportLine { public int Id { get; set; } }

        public class OrderProfile : Profile
        {
            public OrderProfile() { CreateMap<Order, ReportLine>(); }
        }

        [Fact]
        public void Profiles_by_instance_inline_and_partial_validation()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile(new OrderProfile());
                cfg.CreateProfile("Reports", p => p.CreateMap<Order, OrderRow>());
            });
            config.AssertConfigurationIsValid<OrderProfile>();
            config.AssertConfigurationIsValid("Reports");
            Assert.Equal(7, config.CreateMapper().Map<OrderRow>(new Order { Id = 7 }).Id);
        }

        public class Box { public List<int> Items { get; set; } public string Label { get; set; } public Address Address { get; set; } }
        public class BoxDto { public List<int> Items { get; set; } public string Label { get; set; } public AddressDto Address { get; set; } }
        public class AddressDto { public string City { get; set; } }

        [Fact]
        public void Null_defaults_and_switches()
        {
            var defaults = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Box, BoxDto>();
                cfg.CreateMap<Address, AddressDto>();
            }).CreateMapper();
            var d1 = defaults.Map<BoxDto>(new Box());
            Assert.NotNull(d1.Items);
            Assert.Empty(d1.Items);
            Assert.Null(d1.Label);
            Assert.Null(d1.Address);

            var switched = new MapperConfiguration(cfg =>
            {
                cfg.AllowNullCollections = true;
                cfg.AllowNullDestinationValues = false;
                cfg.CreateMap<Box, BoxDto>();
                cfg.CreateMap<Address, AddressDto>();
            }).CreateMapper();
            var d2 = switched.Map<BoxDto>(new Box());
            Assert.Null(d2.Items);
            Assert.Equal("", d2.Label);
            Assert.NotNull(d2.Address);
        }

        [Fact]
        public void Duplicate_maps_and_additive_creation()
        {
            Assert.Throws<DuplicateTypeMapConfigurationException>(() => new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Order, OrderDto>();
                cfg.CreateMap<Order, OrderDto>();
            }));

            var config = new MapperConfiguration(cfg =>
            {
                cfg.AllowAdditiveTypeMapCreation = true;
                cfg.CreateMap<Order, OrderDto>();
                cfg.CreateMap<Order, OrderDto>().ForMember(d => d.Status, o => o.MapFrom(s => "merged"));
            });
            Assert.Equal("merged", config.CreateMapper().Map<OrderDto>(new Order()).Status);
        }

        [Fact]
        public void Same_type_without_map_returns_same_instance()
        {
            var mapper = new MapperConfiguration(cfg => { }).CreateMapper();
            var order = new Order();
            Assert.Same(order, mapper.Map<Order>(order));
        }
    }

    public class InheritanceDetails
    {
        public class Animal { public string Name { get; set; } }
        public class Dog : Animal { public bool GoodBoy { get; set; } }
        public class Cat : Animal { public int Lives { get; set; } }
        public class AnimalDto { public string Name { get; set; } }
        public class DogDto : AnimalDto { public bool GoodBoy { get; set; } }
        public class CatDto : AnimalDto { public int Lives { get; set; } }

        [Fact]
        public void Include_all_derived()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Animal, AnimalDto>().IncludeAllDerived();
                cfg.CreateMap<Dog, DogDto>();
                cfg.CreateMap<Cat, CatDto>();
            }).CreateMapper();

            var dtos = mapper.Map<List<AnimalDto>>(new List<Animal> { new Dog { GoodBoy = true }, new Cat { Lives = 9 } });
            Assert.True(Assert.IsType<DogDto>(dtos[0]).GoodBoy);
            Assert.Equal(9, Assert.IsType<CatDto>(dtos[1]).Lives);
        }

        [Fact]
        public void Derived_source_without_own_map_uses_base_map()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Animal, AnimalDto>()).CreateMapper();
            Assert.Equal("Rex", mapper.Map<AnimalDto>(new Dog { Name = "Rex" }).Name);
        }

        [Fact]
        public void As_overrides_destination_type()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Animal, AnimalDto>().As<DogDto>();
                cfg.CreateMap<Animal, DogDto>();
            }).CreateMapper();
            var dto = mapper.Map<AnimalDto>(new Animal { Name = "Rex" });
            Assert.IsType<DogDto>(dto);
            Assert.Equal("Rex", dto.Name);
        }

        public class Page<T> { public List<T> Items { get; set; } public int Total { get; set; } }
        public class PageDto<T> { public List<T> Items { get; set; } public int Total { get; set; } }

        public class TotalResolver<T> : IValueResolver<Page<T>, PageDto<T>, int>
        {
            public int Resolve(Page<T> source, PageDto<T> destination, int destMember, ResolutionContext context)
                => source.Items?.Count ?? 0;
        }

        [Fact]
        public void Open_generic_resolver_type()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap(typeof(Page<>), typeof(PageDto<>))
                   .ForMember("Total", o => o.MapFrom(typeof(TotalResolver<>)))).CreateMapper();

            var dto = mapper.Map<PageDto<int>>(new Page<int> { Items = new List<int> { 1, 2, 3 }, Total = 0 });
            Assert.Equal(3, dto.Total);
        }
    }

    public class QueryDetails
    {
        public class Blog { public string Title { get; set; } public List<Post> Posts { get; set; } }
        public class Post { public string Heading { get; set; } }
        public class BlogDto { public string Title { get; set; } public List<PostDto> Posts { get; set; } }
        public class PostDto { public string Heading { get; set; } }

        [Fact]
        public void Explicit_expansion()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Blog, BlogDto>().ForMember(d => d.Posts, o => o.ExplicitExpansion());
                cfg.CreateMap<Post, PostDto>();
            }).CreateMapper();

            var blogs = new[] { new Blog { Title = "A", Posts = new List<Post> { new Post { Heading = "p" } } } }.AsQueryable();

            Assert.Null(mapper.ProjectTo<BlogDto>(blogs).Single().Posts);
            Assert.Equal("p", mapper.ProjectTo<BlogDto>(blogs, null, d => d.Posts).Single().Posts[0].Heading);
        }
    }

    public class ValidationDetails
    {
        public class Source { public string Name { get; set; } }
        public class NoDefaultCtor
        {
            public NoDefaultCtor(Guid token) { }
            public string Name { get; set; }
        }

        [Fact]
        public void Disable_ctor_validation()
        {
            var bad = new MapperConfiguration(cfg => cfg.CreateMap<Source, NoDefaultCtor>());
            var ex = Assert.Throws<MapItConfigurationException>(() => bad.AssertConfigurationIsValid());
            Assert.False(ex.Errors.Single().CanConstruct);
            Assert.Equal(typeof(Source), ex.Errors.Single().TypeMap.SourceType);

            var ok = new MapperConfiguration(cfg => cfg.CreateMap<Source, NoDefaultCtor>().DisableCtorValidation());
            ok.AssertConfigurationIsValid();
        }

        [Fact]
        public void Custom_validator()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.Validator(ctx =>
                {
                    if (ctx.TypeMap.DestinationType.Name.EndsWith("Entity"))
                        throw new InvalidOperationException("Map to entities only from commands.");
                });
                cfg.CreateMap<Source, SourceEntity>();
            });
            var ex = Assert.Throws<MapItConfigurationException>(() => config.AssertConfigurationIsValid());
            Assert.Contains(ex.Errors, e => e.UnmappedPropertyNames.Any(n => n.Contains("entities")));
        }

        public class SourceEntity { public string Name { get; set; } }
    }

    public class ExtraFeatureDetails
    {
        [Fact]
        public void Map_or_default_with_fallback()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()).CreateMapper();
            var empty = new OrderDto();
            Assert.Same(empty, mapper.MapOrDefault((Order)null, empty));
            Assert.Equal(3, mapper.MapOrDefault(new Order { Id = 3 }, empty).Id);
        }

        public class OrderView { public int Id { get; set; } public string ShippingEstimate { get; set; } }

        public class EstimateResolver : IAsyncValueResolver<Order, OrderView, string>
        {
            public async Task<string> ResolveAsync(Order source, OrderView destination, string destMember, ResolutionContext context, CancellationToken cancellationToken)
            {
                await Task.Delay(1, cancellationToken);
                return "3 days";
            }
        }

        [Fact]
        public async Task Async_members_are_default_with_plain_map()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Order, OrderView>()
                   .ForMember(d => d.ShippingEstimate, o => o.MapFromAsync(new EstimateResolver()))).CreateMapper();

            Assert.Null(mapper.Map<OrderView>(new Order { Id = 1 }).ShippingEstimate);
            var view = await mapper.MapAsync<Order, OrderView>(new Order { Id = 1 }, CancellationToken.None);
            Assert.Equal("3 days", view.ShippingEstimate);
        }

        public class CacheInterceptor : MappingInterceptor
        {
            public static readonly OrderDto Cached = new OrderDto { Id = 42 };
            public override object OnBeforeMap(MappingInterceptionContext context)
                => context.DestinationType == typeof(OrderDto) ? Cached : null;
        }

        [Fact]
        public void Interceptor_before_map_short_circuits()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.AddInterceptor(new CacheInterceptor());
                cfg.CreateMap<Order, OrderDto>();
            }).CreateMapper();
            Assert.Same(CacheInterceptor.Cached, mapper.Map<OrderDto>(new Order { Id = 1 }));
        }

        public class Category { public string Name { get; set; } public List<Category> Children { get; set; } = new List<Category>(); }
        public class CategoryDto { public string Name { get; set; } public List<CategoryDto> Children { get; set; } }

        [Fact]
        public void Max_depth_cuts_off_deeper_levels()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Category, CategoryDto>().MaxDepth(2)).CreateMapper();
            var root = new Category { Name = "1" };
            var level2 = new Category { Name = "2" };
            root.Children.Add(level2);
            level2.Children.Add(new Category { Name = "3" });

            var dto = mapper.Map<CategoryDto>(root);
            var child = Assert.Single(dto.Children);
            Assert.Equal("2", child.Name);
            Assert.True(child.Children == null || child.Children.Count == 0 || child.Children.All(c => c == null));
        }

        public class Pair { public Address First { get; set; } public Address Second { get; set; } }
        public class PairDto { public AddressDto First { get; set; } public AddressDto Second { get; set; } }
        public class AddressDto { public string City { get; set; } }

        [Fact]
        public void Preserve_references_keeps_shared_objects_shared()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Pair, PairDto>();
                cfg.CreateMap<Address, AddressDto>().PreserveReferences();
            }).CreateMapper();

            var shared = new Address { City = "Porto" };
            var dto = mapper.Map<PairDto>(new Pair { First = shared, Second = shared });
            Assert.Same(dto.First, dto.Second);
        }
    }

    public class DependencyInjectionDetails
    {
        public class MySettings { public int MaxItems { get; set; } = 5; }

        public class ScannedDto { public int Id { get; set; } }

        public class OrderProfile : Profile
        {
            public OrderProfile() { CreateMap<Order, ScannedDto>(); }
        }

        [Fact]
        public void Assembly_scanning_lifetimes_and_service_aware_config()
        {
            var scanned = new ServiceCollection();
            scanned.AddMapIt(typeof(OrderProfile).Assembly);
            using (var p = scanned.BuildServiceProvider())
                Assert.Equal(1, p.GetRequiredService<IMapper>().Map<ScannedDto>(new Order { Id = 1 }).Id);

            var scoped = new ServiceCollection();
            scoped.AddMapIt(cfg => { }, new[] { typeof(OrderProfile).Assembly }, ServiceLifetime.Scoped);
            using (var p = scoped.BuildServiceProvider())
            using (var scope = p.CreateScope())
                Assert.Equal(2, scope.ServiceProvider.GetRequiredService<IMapper>().Map<ScannedDto>(new Order { Id = 2 }).Id);

            var withServices = new ServiceCollection();
            withServices.AddSingleton(new MySettings());
            withServices.AddMapIt((sp, cfg) =>
            {
                var settings = sp.GetRequiredService<MySettings>();
                cfg.MaxCollectionItems = settings.MaxItems;
                cfg.CreateMap<Order, OrderDto>();
            });
            using (var p = withServices.BuildServiceProvider())
            {
                var mapper = p.GetRequiredService<IMapper>();
                Assert.Equal(3, mapper.Map<OrderDto>(new Order { Id = 3 }).Id);
                Assert.Throws<MapItMappingException>(() => mapper.Map<List<OrderDto>>(Enumerable.Range(0, 10).Select(i => new Order()).ToList()));
            }
        }
    }
}
