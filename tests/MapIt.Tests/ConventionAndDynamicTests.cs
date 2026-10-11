using System;
using System.Collections.Generic;
using System.Dynamic;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class ConventionAndDynamicTests
    {
        // ---- Flattening ----
        public class Customer { public string Name { get; set; } public Address Address { get; set; } }
        public class Address { public string City { get; set; } }
        public class Order { public Customer Customer { get; set; } public decimal Amount { get; set; } }
        public class OrderFlatDto
        {
            public string CustomerName { get; set; }
            public string CustomerAddressCity { get; set; }
            public decimal Amount { get; set; }
        }

        [Fact]
        public void Flattens_nested_members_by_pascalcase_convention()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderFlatDto>());
            var mapper = config.CreateMapper();

            var order = new Order
            {
                Customer = new Customer { Name = "Alice", Address = new Address { City = "Oslo" } },
                Amount = 42m
            };
            var dto = mapper.Map<OrderFlatDto>(order);

            Assert.Equal("Alice", dto.CustomerName);
            Assert.Equal("Oslo", dto.CustomerAddressCity);
            Assert.Equal(42m, dto.Amount);
        }

        [Fact]
        public void Flattening_null_intermediate_yields_default()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderFlatDto>());
            var mapper = config.CreateMapper();

            var dto = mapper.Map<OrderFlatDto>(new Order { Customer = null, Amount = 1m });
            Assert.Null(dto.CustomerName);
        }

        // ---- Get-prefixed methods ----
        public class Calc { public int X { get; set; } public int GetDoubled() => X * 2; }
        public class CalcDto { public int X { get; set; } public int Doubled { get; set; } }

        [Fact]
        public void Maps_from_get_prefixed_method()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Calc, CalcDto>());
            var mapper = config.CreateMapper();
            var dto = mapper.Map<CalcDto>(new Calc { X = 5 });
            Assert.Equal(10, dto.Doubled);
        }

        // ---- Naming conventions (snake_case source -> PascalCase dest) ----
        public class SnakeSource { public string first_name { get; set; } public string last_name { get; set; } }
        public class PascalDest { public string FirstName { get; set; } public string LastName { get; set; } }

        [Fact]
        public void Maps_snake_case_source_to_pascal_destination()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.SourceMemberNamingConvention = new LowerUnderscoreNamingConvention();
                cfg.DestinationMemberNamingConvention = new PascalCaseNamingConvention();
                cfg.CreateMap<SnakeSource, PascalDest>();
            });
            var mapper = config.CreateMapper();
            var dto = mapper.Map<PascalDest>(new SnakeSource { first_name = "Jane", last_name = "Doe" });
            Assert.Equal("Jane", dto.FirstName);
            Assert.Equal("Doe", dto.LastName);
        }

        // ---- Prefix/postfix recognition ----
        public class PrefixSource { public string frmName { get; set; } }
        public class PrefixDest { public string Name { get; set; } }

        [Fact]
        public void Recognizes_source_prefix()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.RecognizePrefixes(new[] { "frm" });
                cfg.CreateMap<PrefixSource, PrefixDest>();
            });
            var mapper = config.CreateMapper();
            var dto = mapper.Map<PrefixDest>(new PrefixSource { frmName = "hi" });
            Assert.Equal("hi", dto.Name);
        }

        // ---- IncludeMembers ----
        public class Inner { public string Detail { get; set; } }
        public class Composite { public string Title { get; set; } public Inner Inner { get; set; } }
        public class FlatDto { public string Title { get; set; } public string Detail { get; set; } }

        [Fact]
        public void IncludeMembers_flattens_relation()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Composite, FlatDto>().IncludeMembers(s => s.Inner);
                cfg.CreateMap<Inner, FlatDto>();
            });
            var mapper = config.CreateMapper();
            var dto = mapper.Map<FlatDto>(new Composite { Title = "t", Inner = new Inner { Detail = "d" } });
            Assert.Equal("t", dto.Title);
            Assert.Equal("d", dto.Detail);
        }

        // ---- Unflattening via ReverseMap ----
        [Fact]
        public void ReverseMap_unflattens_nested_path()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Order, OrderFlatDto>().ReverseMap());
            var mapper = config.CreateMapper();

            var dto = new OrderFlatDto { CustomerName = "Bob", CustomerAddressCity = "Rome", Amount = 9m };
            var order = mapper.Map<OrderFlatDto, Order>(dto);

            Assert.NotNull(order.Customer);
            Assert.Equal("Bob", order.Customer.Name);
            Assert.NotNull(order.Customer.Address);
            Assert.Equal("Rome", order.Customer.Address.City);
        }

        // ---- Dynamic / ExpandoObject ----
        public class Poco { public string Name { get; set; } public int Value { get; set; } }

        [Fact]
        public void Maps_poco_to_expando()
        {
            var config = new MapperConfiguration(cfg => { });
            var mapper = config.CreateMapper();

            var expando = mapper.Map<Poco, ExpandoObject>(new Poco { Name = "x", Value = 3 });
            var dict = (IDictionary<string, object>)expando;
            Assert.Equal("x", dict["Name"]);
            Assert.Equal(3, dict["Value"]);
        }

        [Fact]
        public void Maps_expando_to_poco()
        {
            var config = new MapperConfiguration(cfg => { });
            var mapper = config.CreateMapper();

            IDictionary<string, object> source = new ExpandoObject();
            source["Name"] = "y";
            source["Value"] = 7;

            var poco = mapper.Map<ExpandoObject, Poco>((ExpandoObject)source);
            Assert.Equal("y", poco.Name);
            Assert.Equal(7, poco.Value);
        }
    }
}
