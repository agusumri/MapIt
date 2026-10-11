using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MapIt;
using Xunit;

namespace MapIt.Tests
{
    public class ProjectToTests
    {
        public class Order { public int Id { get; set; } public string Code { get; set; } public Customer Customer { get; set; } }
        public class Customer { public string Name { get; set; } }
        public class OrderDto { public int Id { get; set; } public string Code { get; set; } public CustomerDto Customer { get; set; } }
        public class CustomerDto { public string Name { get; set; } }

        private static IQueryable<Order> SampleOrders() => new[]
        {
            new Order { Id = 1, Code = "A", Customer = new Customer { Name = "Alice" } },
            new Order { Id = 2, Code = "B", Customer = null },
        }.AsQueryable();

        [Fact]
        public void ProjectTo_produces_translatable_select_memberinit()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Order, OrderDto>();
                cfg.CreateMap<Customer, CustomerDto>();
            });
            var mapper = config.CreateMapper();

            var projected = mapper.ProjectTo<OrderDto>(SampleOrders());

            // The projection is a Select over a MemberInit (translatable), not in-memory mapping.
            Assert.IsAssignableFrom<IQueryable<OrderDto>>(projected);
            var methodCall = projected.Expression as MethodCallExpression;
            Assert.NotNull(methodCall);
            Assert.Equal("Select", methodCall.Method.Name);

            var results = projected.OrderBy(o => o.Id).ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal("A", results[0].Code);
            Assert.Equal("Alice", results[0].Customer.Name);
            Assert.Null(results[1].Customer); // null-propagation
        }

        [Fact]
        public void ProjectTo_runtime_type_overload_works()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Order, OrderDto>();
                cfg.CreateMap<Customer, CustomerDto>();
            });
            var mapper = config.CreateMapper();

            var projected = mapper.ProjectTo(SampleOrders(), typeof(OrderDto));
            var results = projected.Cast<OrderDto>().OrderBy(o => o.Id).ToList();
            Assert.Equal("A", results[0].Code);
        }

        public class WithResolverDto
        {
            public int Id { get; set; }
            public string Computed { get; set; }
        }

        public class OrderResolver : IValueResolver<Order, WithResolverDto, string>
        {
            public string Resolve(Order source, WithResolverDto destination, string destMember, ResolutionContext context) => "x";
        }

        [Fact]
        public void ProjectTo_throws_on_resolver_member()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Order, WithResolverDto>()
                   .ForMember(d => d.Computed, opt => opt.MapFrom(new OrderResolver())));
            var mapper = config.CreateMapper();

            Assert.Throws<MapItMappingException>(() => mapper.ProjectTo<WithResolverDto>(SampleOrders()).ToList());
        }

        [Fact]
        public void ProjectTo_throws_on_converting_map()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Order, OrderDto>().ConvertUsing((s, d) => new OrderDto { Id = s.Id }));
            var mapper = config.CreateMapper();

            Assert.Throws<MapItMappingException>(() => mapper.ProjectTo<OrderDto>(SampleOrders()).ToList());
        }
    }
}
