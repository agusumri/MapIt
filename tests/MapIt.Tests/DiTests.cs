using System;
using MapIt;
using MapIt.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MapIt.Tests
{
    public class DiTests
    {
        public class Order
        {
            public int Id { get; set; }
            public string Item { get; set; }
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public string Item { get; set; }
        }

        [Fact]
        public void AddMapIt_registers_IMapper()
        {
            var services = new ServiceCollection();
            services.AddMapIt(cfg => cfg.CreateMap<Order, OrderDto>());
            var provider = services.BuildServiceProvider();

            var mapper = provider.GetRequiredService<IMapper>();
            var result = mapper.Map<OrderDto>(new Order { Id = 1, Item = "Widget" });

            Assert.Equal(1, result.Id);
            Assert.Equal("Widget", result.Item);
        }

        [Fact]
        public void AddMapIt_registers_IConfigurationProvider()
        {
            var services = new ServiceCollection();
            services.AddMapIt(cfg => cfg.CreateMap<Order, OrderDto>());
            var provider = services.BuildServiceProvider();

            var config = provider.GetRequiredService<IConfigurationProvider>();
            Assert.NotNull(config);
        }

        [Fact]
        public void AddMapIt_registers_IRuntimeMapper()
        {
            var services = new ServiceCollection();
            services.AddMapIt(cfg => cfg.CreateMap<Order, OrderDto>());
            var provider = services.BuildServiceProvider();

            var mapper = provider.GetRequiredService<IRuntimeMapper>();
            Assert.NotNull(mapper);
        }
    }
}
