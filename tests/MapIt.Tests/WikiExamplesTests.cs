using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapIt;
using MapIt.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MapIt.Tests.Wiki
{
    // Every example in the GitHub wiki is mirrored here so the documentation stays correct.

    // ---------- Shared model ----------
    public class Customer
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime BirthDate { get; set; }
        public Address Address { get; set; }
        public List<Order> Orders { get; set; } = new List<Order>();
        public string GetFullName() => FirstName + " " + LastName;
    }

    public class Address { public string Street { get; set; } public string City { get; set; } }
    public class Order { public int Id { get; set; } public decimal Total { get; set; } public OrderStatus Status { get; set; } }
    public enum OrderStatus { New, Paid, Shipped }

    public class CustomerDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName { get; set; }        // from GetFullName()
        public string AddressCity { get; set; }     // flattened from Address.City
        public List<OrderDto> Orders { get; set; }
    }

    public class OrderDto { public int Id { get; set; } public decimal Total { get; set; } public string Status { get; set; } }

    public class GettingStarted
    {
        [Fact]
        public void First_mapping()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Customer, CustomerDto>();
                cfg.CreateMap<Order, OrderDto>();
            });
            config.AssertConfigurationIsValid();
            IMapper mapper = config.CreateMapper();

            var customer = new Customer
            {
                Id = 1, FirstName = "Ana", LastName = "Silva",
                Address = new Address { City = "Lisbon" },
                Orders = { new Order { Id = 10, Total = 99.5m, Status = OrderStatus.Paid } }
            };
            CustomerDto dto = mapper.Map<CustomerDto>(customer);

            Assert.Equal("Ana Silva", dto.FullName);
            Assert.Equal("Lisbon", dto.AddressCity);
            Assert.Equal("Paid", dto.Orders[0].Status);
        }

        [Fact]
        public void Map_onto_existing_object()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()).CreateMapper();
            var existing = new OrderDto { Id = 0 };
            var result = mapper.Map(new Order { Id = 5, Total = 1m }, existing);
            Assert.Same(existing, result);
            Assert.Equal(5, existing.Id);
        }

        [Fact]
        public void Dependency_injection()
        {
            var services = new ServiceCollection();
            services.AddMapIt(cfg => cfg.CreateMap<Order, OrderDto>());
            using var provider = services.BuildServiceProvider();

            var mapper = provider.GetRequiredService<IMapper>();
            Assert.Equal(3, mapper.Map<OrderDto>(new Order { Id = 3 }).Id);
        }

        public interface IExchangeRates { decimal ToUsd(decimal eur); }
        public class FixedRates : IExchangeRates { public decimal ToUsd(decimal eur) => eur * 2; }
        public class OrderUsd { public decimal Total { get; set; } }

        // Gets its dependency through constructor injection.
        public class UsdResolver : IValueResolver<Order, OrderUsd, decimal>
        {
            private readonly IExchangeRates _rates;
            public UsdResolver(IExchangeRates rates) => _rates = rates;
            public decimal Resolve(Order source, OrderUsd destination, decimal destMember, ResolutionContext context)
                => _rates.ToUsd(source.Total);
        }

        [Fact]
        public void Resolvers_get_services_from_the_container()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IExchangeRates, FixedRates>();
            services.AddMapIt(cfg => cfg.CreateMap<Order, OrderUsd>().ForMember(d => d.Total, o => o.MapFrom<UsdResolver>()));
            using var provider = services.BuildServiceProvider();

            var mapper = provider.GetRequiredService<IMapper>();
            Assert.Equal(20m, mapper.Map<OrderUsd>(new Order { Total = 10m }).Total);
        }

        public class OrderProfile : Profile
        {
            public OrderProfile()
            {
                CreateMap<Order, OrderDto>();
            }
        }

        [Fact]
        public void Profiles_and_assembly_scanning()
        {
            var byType = new MapperConfiguration(cfg => cfg.AddProfile<OrderProfile>()).CreateMapper();
            Assert.Equal(1, byType.Map<OrderDto>(new Order { Id = 1 }).Id);

            var scanned = new MapperConfiguration(cfg => cfg.AddMaps(new[] { typeof(OrderProfile) })).CreateMapper();
            Assert.Equal(2, scanned.Map<OrderDto>(new Order { Id = 2 }).Id);
        }
    }

    public class Conventions
    {
        public class SnakeSource { public string first_name { get; set; } }
        public class PascalDest { public string FirstName { get; set; } }

        [Fact]
        public void Naming_conventions()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.SourceMemberNamingConvention = new LowerUnderscoreNamingConvention();
                cfg.DestinationMemberNamingConvention = new PascalCaseNamingConvention();
                cfg.CreateMap<SnakeSource, PascalDest>();
            }).CreateMapper();
            Assert.Equal("Ana", mapper.Map<PascalDest>(new SnakeSource { first_name = "Ana" }).FirstName);
        }

        public class LegacySource { public string strName { get; set; } }
        public class ModernDest { public string Name { get; set; } }

        [Fact]
        public void Prefixes()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.RecognizePrefixes("str");
                cfg.CreateMap<LegacySource, ModernDest>();
            }).CreateMapper();
            Assert.Equal("x", mapper.Map<ModernDest>(new LegacySource { strName = "x" }).Name);
        }

        public class Flight { public string Airlina { get; set; } }
        public class FlightDto { public string Airline { get; set; } }

        [Fact]
        public void Replace_member_name()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.ReplaceMemberName("Airline", "Airlina");
                cfg.CreateMap<Flight, FlightDto>();
            }).CreateMapper();
            Assert.Equal("TAP", mapper.Map<FlightDto>(new Flight { Airlina = "TAP" }).Airline);
        }

        public class Shipment { public string Code { get; set; } public Address Destination { get; set; } }
        public class ShipmentDto { public string Code { get; set; } public string City { get; set; } public string Street { get; set; } }

        [Fact]
        public void Include_members()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Shipment, ShipmentDto>().IncludeMembers(s => s.Destination);
                cfg.CreateMap<Address, ShipmentDto>();
            }).CreateMapper();
            var dto = mapper.Map<ShipmentDto>(new Shipment { Code = "S1", Destination = new Address { City = "Porto", Street = "Rua 1" } });
            Assert.Equal("Porto", dto.City);
            Assert.Equal("Rua 1", dto.Street);
        }

        [Fact]
        public void Global_ignore_and_member_filter()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.AddGlobalIgnore("Last");
                cfg.ShouldMapProperty = p => p.Name != "Id";
                cfg.CreateMap<Customer, CustomerDto>().ForMember(d => d.Orders, o => o.Ignore());
            }).CreateMapper();
            var dto = mapper.Map<CustomerDto>(new Customer { Id = 9, FirstName = "A", LastName = "B" });
            Assert.Equal(0, dto.Id);
            Assert.Null(dto.LastName);
            Assert.Equal("A", dto.FirstName);
        }
    }

    public static class CustomerExtensions
    {
        public static int OrderCount(this Customer c) => c.Orders.Count;
    }

    public class ExtensionMethodSource
    {
        public class Summary { public int OrderCount { get; set; } }

        [Fact]
        public void Source_extension_methods()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.IncludeSourceExtensionMethods(typeof(CustomerExtensions));
                cfg.CreateMap<Customer, Summary>();
            }).CreateMapper();
            var c = new Customer { Orders = { new Order(), new Order() } };
            Assert.Equal(2, mapper.Map<Summary>(c).OrderCount);
        }
    }

    public class CustomizingMembers
    {
        public class Person { public string First { get; set; } public string Last { get; set; } public int Age { get; set; } public string Nickname { get; set; } public string Notes { get; set; } }
        public class PersonDto
        {
            public string Name { get; set; }
            public bool IsAdult { get; set; }
            public string Nickname { get; set; }
            public string Notes { get; set; }
            public string Internal { get; set; }
        }

        [Fact]
        public void For_member_options()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Person, PersonDto>()
                    .ForMember(d => d.Name, o => o.MapFrom(s => s.First + " " + s.Last))
                    .ForMember(d => d.IsAdult, o => o.MapFrom(s => s.Age >= 18))
                    .ForMember(d => d.Nickname, o => o.NullSubstitute("(none)"))
                    .ForMember(d => d.Notes, o => o.Condition(s => s.Notes != null && s.Notes.Length < 50))
                    .ForMember(d => d.Internal, o => o.Ignore())).CreateMapper();

            var dto = mapper.Map<PersonDto>(new Person { First = "Ana", Last = "Silva", Age = 20, Nickname = null, Notes = "ok" });
            Assert.Equal("Ana Silva", dto.Name);
            Assert.True(dto.IsAdult);
            Assert.Equal("(none)", dto.Nickname);
            Assert.Equal("ok", dto.Notes);
            Assert.Null(dto.Internal);
        }

        [Fact]
        public void Add_transform()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Person, PersonDto>()
                    .ForMember(d => d.Name, o => { o.MapFrom(s => s.First); o.AddTransform(v => v.Trim()); })
                    .ForMember(d => d.Internal, o => o.Ignore())).CreateMapper();
            Assert.Equal("Ana", mapper.Map<PersonDto>(new Person { First = "  Ana  " }).Name);
        }

        public class Wrapper { public Address Address { get; set; } }
        public class WrapperDto { public AddressView Address { get; set; } }
        public class AddressView { public string City { get; set; } }

        [Fact]
        public void Do_not_allow_null()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Wrapper, WrapperDto>().ForMember(d => d.Address, o => o.DoNotAllowNull());
                cfg.CreateMap<Address, AddressView>();
            }).CreateMapper();
            Assert.NotNull(mapper.Map<WrapperDto>(new Wrapper { Address = null }).Address);
        }

        public class Flat { public string City { get; set; } }
        public class Nested { public AddressView Location { get; set; } }

        [Fact]
        public void For_path()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Flat, Nested>()
                    .ForPath(d => d.Location.City, o => o.MapFrom(s => s.City))
                    .ForMember(d => d.Location, o => o.Ignore())).CreateMapper();
            Assert.Equal("Faro", mapper.Map<Nested>(new Flat { City = "Faro" }).Location.City);
        }

        public class Money
        {
            public Money(decimal amount, string currency) { Amount = amount; Currency = currency; }
            public decimal Amount { get; }
            public string Currency { get; }
        }
        public class Price { public decimal Value { get; set; } public string Code { get; set; } }

        [Fact]
        public void Constructors()
        {
            var viaCtorParam = new MapperConfiguration(cfg =>
                cfg.CreateMap<Price, Money>()
                    .ForCtorParam("amount", o => o.MapFrom(s => s.Value))
                    .ForCtorParam("currency", o => o.MapFrom(s => s.Code))).CreateMapper();
            var m1 = viaCtorParam.Map<Money>(new Price { Value = 5, Code = "EUR" });
            Assert.Equal(5, m1.Amount);
            Assert.Equal("EUR", m1.Currency);

            var viaConstructUsing = new MapperConfiguration(cfg =>
                cfg.CreateMap<Price, Money>().ConstructUsing((s, ctx) => new Money(s.Value, s.Code))).CreateMapper();
            Assert.Equal("EUR", viaConstructUsing.Map<Money>(new Price { Value = 1, Code = "EUR" }).Currency);
        }

        public class Audit { public string Name { get; set; } public DateTime MappedAt { get; set; } }

        [Fact]
        public void Before_and_after_map()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Person, Audit>()
                    .ForMember(d => d.Name, o => o.MapFrom(s => s.First))
                    .ForMember(d => d.MappedAt, o => o.Ignore())
                    .BeforeMap((s, d) => s.First = s.First?.Trim())
                    .AfterMap((s, d) => d.MappedAt = new DateTime(2026, 1, 1))).CreateMapper();
            var audit = mapper.Map<Audit>(new Person { First = " Ana " });
            Assert.Equal("Ana", audit.Name);
            Assert.Equal(2026, audit.MappedAt.Year);
        }

        [Fact]
        public void Reverse_map_and_unflattening()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Customer, CustomerDto>().ReverseMap();
                cfg.CreateMap<Order, OrderDto>().ReverseMap();
            }).CreateMapper();
            var customer = mapper.Map<Customer>(new CustomerDto { FirstName = "Ana", AddressCity = "Braga" });
            Assert.Equal("Ana", customer.FirstName);
            Assert.Equal("Braga", customer.Address.City);
        }
    }

    public class ResolversAndConverters
    {
        public class Invoice { public decimal Net { get; set; } public decimal TaxRate { get; set; } public string CustomerCode { get; set; } public DateTime IssuedOn { get; set; } }
        public class InvoiceDto { public decimal Gross { get; set; } public string CustomerName { get; set; } public string IssuedOn { get; set; } }

        public class GrossResolver : IValueResolver<Invoice, InvoiceDto, decimal>
        {
            public decimal Resolve(Invoice source, InvoiceDto destination, decimal destMember, ResolutionContext context)
                => source.Net * (1 + source.TaxRate);
        }

        public class CustomerNameResolver : IMemberValueResolver<Invoice, InvoiceDto, string, string>
        {
            public string Resolve(Invoice source, InvoiceDto destination, string sourceMember, string destMember, ResolutionContext context)
                => sourceMember == "C1" ? "Contoso" : "Unknown";
        }

        public class IsoDateConverter : IValueConverter<DateTime, string>
        {
            public string Convert(DateTime sourceMember, ResolutionContext context) => sourceMember.ToString("yyyy-MM-dd");
        }

        [Fact]
        public void Resolvers_and_value_converters()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Invoice, InvoiceDto>()
                    .ForMember(d => d.Gross, o => o.MapFrom<GrossResolver>())
                    .ForMember(d => d.CustomerName, o => o.MapFrom<CustomerNameResolver, string>(s => s.CustomerCode))
                    .ForMember(d => d.IssuedOn, o => o.ConvertUsing(new IsoDateConverter(), s => s.IssuedOn))).CreateMapper();

            var dto = mapper.Map<InvoiceDto>(new Invoice { Net = 100, TaxRate = 0.2m, CustomerCode = "C1", IssuedOn = new DateTime(2026, 3, 4) });
            Assert.Equal(120m, dto.Gross);
            Assert.Equal("Contoso", dto.CustomerName);
            Assert.Equal("2026-03-04", dto.IssuedOn);
        }

        public class CsvConverter : ITypeConverter<string, List<int>>
        {
            public List<int> Convert(string source, List<int> destination, ResolutionContext context)
                => source.Split(',').Select(int.Parse).ToList();
        }

        [Fact]
        public void Type_converters()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<string, List<int>>().ConvertUsing(new CsvConverter());
                cfg.CreateMap<string, Guid>().ConvertUsing(s => Guid.Parse(s));
            }).CreateMapper();
            Assert.Equal(new[] { 1, 2, 3 }, mapper.Map<string, List<int>>("1,2,3"));
            Assert.Equal(Guid.Empty, mapper.Map<string, Guid>(Guid.Empty.ToString()));
        }

        [Fact]
        public void Items_passed_per_call()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Invoice, InvoiceDto>()
                    .ForMember(d => d.CustomerName, o => o.MapFrom((s, d, m, ctx) => (string)ctx.Items["user"]))
                    .ForMember(d => d.IssuedOn, o => o.Ignore())).CreateMapper();
            var dto = mapper.Map<Invoice, InvoiceDto>(new Invoice(), opt => opt.Items["user"] = "admin");
            Assert.Equal("admin", dto.CustomerName);
        }
    }

    public class CollectionsAndEnums
    {
        [Fact]
        public void Lists_arrays_and_dictionaries()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>()).CreateMapper();
            var orders = new List<Order> { new Order { Id = 1 }, new Order { Id = 2 } };

            OrderDto[] array = mapper.Map<OrderDto[]>(orders);
            List<OrderDto> list = mapper.Map<List<OrderDto>>(orders);
            var dict = mapper.Map<Dictionary<string, OrderDto>>(new Dictionary<string, Order> { ["a"] = orders[0] });

            Assert.Equal(2, array.Length);
            Assert.Equal(2, list[1].Id);
            Assert.Equal(1, dict["a"].Id);
        }

        public class Cart { public List<Line> Lines { get; set; } }
        public class Line { public int ProductId { get; set; } public int Qty { get; set; } }
        public class CartEntity { public List<LineEntity> Lines { get; set; } = new List<LineEntity>(); }
        public class LineEntity { public int ProductId { get; set; } public int Qty { get; set; } }

        [Fact]
        public void Update_existing_collection_by_key()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Line, LineEntity>().EqualityComparison((src, dest) => src.ProductId == dest.ProductId)).CreateMapper();

            var existing = new List<LineEntity> { new LineEntity { ProductId = 1, Qty = 1 }, new LineEntity { ProductId = 2, Qty = 1 } };
            var kept = existing[0];
            var incoming = new List<Line> { new Line { ProductId = 1, Qty = 5 }, new Line { ProductId = 3, Qty = 1 } };

            mapper.Map(incoming, existing);

            Assert.Same(kept, existing.Single(l => l.ProductId == 1));
            Assert.Equal(5, kept.Qty);
            Assert.DoesNotContain(existing, l => l.ProductId == 2);
            Assert.Contains(existing, l => l.ProductId == 3);
        }

        public enum Status { Active, Inactive }
        public enum State { Enabled, Disabled }

        [Fact]
        public void Enums()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Status, State>().ConvertUsingEnumMapping(o => o
                    .MapValue(Status.Active, State.Enabled)
                    .MapValue(Status.Inactive, State.Disabled))).CreateMapper();

            Assert.Equal(State.Disabled, mapper.Map<State>(Status.Inactive));
            Assert.Equal(OrderStatus.Paid, mapper.Map<string, OrderStatus>("paid"));
            Assert.Equal("Shipped", mapper.Map<OrderStatus, string>(OrderStatus.Shipped));
        }

        [Fact]
        public void Dynamic_objects()
        {
            var mapper = new MapperConfiguration(cfg => { }).CreateMapper();
            IDictionary<string, object> bag = mapper.Map<Order, ExpandoObject>(new Order { Id = 4 });
            Assert.Equal(4, bag["Id"]);

            dynamic input = new ExpandoObject();
            input.Id = 8;
            input.Total = 2.5m;
            Order order = mapper.Map<ExpandoObject, Order>((ExpandoObject)input);
            Assert.Equal(8, order.Id);
        }
    }

    public class InheritanceAndGenerics
    {
        public class Animal { public string Name { get; set; } }
        public class Dog : Animal { public bool GoodBoy { get; set; } }
        public class AnimalDto { public string Name { get; set; } }
        public class DogDto : AnimalDto { public bool GoodBoy { get; set; } }

        [Fact]
        public void Include_derived_types()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Animal, AnimalDto>().Include<Dog, DogDto>();
                cfg.CreateMap<Dog, DogDto>();
            }).CreateMapper();

            var animals = new List<Animal> { new Animal { Name = "Cat" }, new Dog { Name = "Rex", GoodBoy = true } };
            var dtos = mapper.Map<List<AnimalDto>>(animals);

            Assert.IsType<AnimalDto>(dtos[0]);
            var dog = Assert.IsType<DogDto>(dtos[1]);
            Assert.True(dog.GoodBoy);
        }

        public class PetDto { public string Name { get; set; } public string Kind { get; set; } }
        public class GoodDogDto : PetDto { public bool GoodBoy { get; set; } }

        [Fact]
        public void Include_base_inherits_configuration()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Animal, PetDto>().ForMember(d => d.Kind, o => o.MapFrom(s => "animal"));
                cfg.CreateMap<Dog, GoodDogDto>().IncludeBase<Animal, PetDto>();
            }).CreateMapper();

            var dto = mapper.Map<GoodDogDto>(new Dog { Name = "Rex", GoodBoy = true });
            Assert.Equal("animal", dto.Kind);
            Assert.True(dto.GoodBoy);
        }

        public class Page<T> { public List<T> Items { get; set; } public int Total { get; set; } }
        public class PageDto<T> { public List<T> Items { get; set; } public int Total { get; set; } }

        [Fact]
        public void Open_generics()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap(typeof(Page<>), typeof(PageDto<>));
                cfg.CreateMap<Order, OrderDto>();
            }).CreateMapper();

            var dto = mapper.Map<PageDto<OrderDto>>(new Page<Order> { Items = new List<Order> { new Order { Id = 1 } }, Total = 1 });
            Assert.Equal(1, dto.Total);
            Assert.Equal(1, dto.Items[0].Id);
        }
    }

    public class Attributes
    {
        public class Product { public string Name { get; set; } public decimal Price { get; set; } public string Sku { get; set; } }

        [AutoMap(typeof(Product), ReverseMap = true)]
        public class ProductDto
        {
            public string Name { get; set; }
            [SourceMember("Price")] public decimal Cost { get; set; }
            [Ignore] public string Sku { get; set; }
            [NullSubstitute("n/a")] public string Description { get; set; }
        }

        [Fact]
        public void Attribute_mapping()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddMaps(new[] { typeof(ProductDto) })).CreateMapper();
            var dto = mapper.Map<ProductDto>(new Product { Name = "Pen", Price = 2m, Sku = "X" });
            Assert.Equal(2m, dto.Cost);
            Assert.Null(dto.Sku);
            Assert.Equal("Pen", mapper.Map<Product>(dto).Name);
        }
    }

    public class Queries
    {
        public class Blog { public int Id { get; set; } public string Title { get; set; } public Author Author { get; set; } public List<Post> Posts { get; set; } }
        public class Author { public string Name { get; set; } }
        public class Post { public string Heading { get; set; } }
        public class BlogDto { public string Title { get; set; } public string AuthorName { get; set; } public List<PostDto> Posts { get; set; } }
        public class PostDto { public string Heading { get; set; } }

        private static IQueryable<Blog> Blogs() => new[]
        {
            new Blog { Id = 1, Title = "A", Author = new Author { Name = "Ana" }, Posts = new List<Post> { new Post { Heading = "p" } } },
            new Blog { Id = 2, Title = "B", Author = new Author { Name = "Rui" }, Posts = new List<Post>() },
        }.AsQueryable();

        [Fact]
        public void Project_to()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Blog, BlogDto>();
                cfg.CreateMap<Post, PostDto>();
            }).CreateMapper();

            List<BlogDto> dtos = mapper.ProjectTo<BlogDto>(Blogs()).Where(b => b.AuthorName == "Ana").ToList();
            Assert.Single(dtos);
            Assert.Equal("p", dtos[0].Posts[0].Heading);
        }

        public class BlogSummary { public string Name { get; set; } public int Number { get; set; } }

        [Fact]
        public void Use_as_data_source()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Blog, BlogSummary>()
                    .ForMember(d => d.Name, o => o.MapFrom(s => s.Title))
                    .ForMember(d => d.Number, o => o.MapFrom(s => s.Id))).CreateMapper();

            var summaries = Blogs().UseAsDataSource(mapper).For<BlogSummary>().Where(s => s.Number > 1).ToQueryable().ToList();
            Assert.Equal("B", Assert.Single(summaries).Name);

            var predicate = mapper.TranslateExpression<BlogSummary, Blog>(s => s.Name == "A");
            Assert.Equal(1, Blogs().Single(predicate).Id);
        }
    }

    public class ExtraFeatures
    {
        private static IMapper Mapper() => new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Order, OrderDto>();
            cfg.CreateMap<Order, Order>();
        }).CreateMapper();

        [Fact]
        public void Try_map_clone_and_map_or_default()
        {
            var mapper = Mapper();
            Assert.True(mapper.TryMap<Order, OrderDto>(new Order { Id = 1 }, out var dto));
            Assert.Equal(1, dto.Id);
            Assert.False(mapper.TryMap<Order, CustomerDto>(new Order(), out _));

            var original = new Order { Id = 2 };
            var copy = mapper.Clone(original);
            Assert.NotSame(original, copy);
            Assert.Equal(2, copy.Id);

            Assert.Null(mapper.MapOrDefault<OrderDto>(null));
        }

        [Fact]
        public void Map_changes()
        {
            var mapper = Mapper();
            var current = new OrderDto { Id = 1, Total = 10m, Status = "New" };
            var result = mapper.MapChanges(new Order { Id = 1, Total = 12m, Status = OrderStatus.New }, current);

            Assert.True(result.HasChanges);
            var change = Assert.Single(result.Changes);
            Assert.Equal("Total", change.MemberName);
            Assert.Equal(10m, change.OldValue);
            Assert.Equal(12m, change.NewValue);
        }

        public class OrderView { public int Id { get; set; } public string ShippingEstimate { get; set; } }

        [Fact]
        public async Task Map_async()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Order, OrderView>()
                    .ForMember(d => d.ShippingEstimate, o => o.MapFromAsync(async s =>
                    {
                        await Task.Delay(1);
                        return "2 days";
                    }))).CreateMapper();

            var view = await mapper.MapAsync<Order, OrderView>(new Order { Id = 3 });
            Assert.Equal("2 days", view.ShippingEstimate);
        }

        public class TimingInterceptor : MappingInterceptor
        {
            public int Count;
            public override object OnAfterMap(MappingInterceptionContext context, object result)
            {
                Interlocked.Increment(ref Count);
                return result;
            }
        }

        [Fact]
        public void Interceptors()
        {
            var timing = new TimingInterceptor();
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.AddInterceptor(timing);
                cfg.CreateMap<Order, OrderDto>();
            }).CreateMapper();
            mapper.Map<OrderDto>(new Order());
            Assert.Equal(1, timing.Count);
        }
    }

    public class ValidationAndSafety
    {
        public class Source { public string Name { get; set; } }
        public class Target { public string Name { get; set; } public string Missing { get; set; } }

        [Fact]
        public void Validation()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Source, Target>());
            var ex = Assert.Throws<MapItConfigurationException>(() => config.AssertConfigurationIsValid());
            Assert.Contains("Missing", ex.Errors.Single().UnmappedPropertyNames);

            var ok = new MapperConfiguration(cfg => cfg.CreateMap<Source, Target>(MemberList.None));
            ok.AssertConfigurationIsValid();
        }

        public class Node { public string Name { get; set; } public Node Parent { get; set; } }
        public class NodeDto { public string Name { get; set; } public NodeDto Parent { get; set; } }

        [Fact]
        public void Safety_limits()
        {
            var mapper = new MapperConfiguration(cfg =>
            {
                cfg.MaxGraphDepth = 100;
                cfg.MaxCollectionItems = 10_000;
                cfg.CreateMap<Node, NodeDto>();
            }).CreateMapper();

            var node = new Node { Name = "self" };
            node.Parent = node;   // cycle: handled automatically (reference preserved, no stack overflow)
            var dto = mapper.Map<NodeDto>(node);
            Assert.Same(dto, dto.Parent);
        }
    }
}
