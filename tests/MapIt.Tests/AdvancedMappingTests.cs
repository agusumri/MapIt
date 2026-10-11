using System;
using System.Collections.Generic;
using System.Linq;
using MapIt;
using Xunit;
using Xunit.Sdk;

namespace MapIt.Tests
{
    public class AdvancedMappingTests
    {
        public class Product
        {
            public string Name { get; set; }
            public decimal Price { get; set; }
        }

        public class ProductDto
        {
            public string Name { get; set; }
            public string PriceDisplay { get; set; }
        }

        [Fact]
        public void ITypeConverter_converts_whole_map()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Product, ProductDto>()
                    .ConvertUsing<ProductConverter>();
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<ProductDto>(new Product { Name = "Widget", Price = 9.99m });
            Assert.Equal("Widget", result.Name);
            Assert.Equal("9.99", result.PriceDisplay);
        }

        [Fact]
        public void IValueResolver_resolves_member()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Product, ProductDto>()
                    .ForMember(d => d.PriceDisplay, opt => opt.MapFrom(new PriceResolver()));
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<ProductDto>(new Product { Name = "Widget", Price = 9.99m });
            Assert.Equal("9.99", result.PriceDisplay);
        }

        [Fact]
        public void IValueConverter_converts_member()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Product, ProductDto>()
                    .ForMember(d => d.PriceDisplay, opt =>
                        opt.ConvertUsing(new DecimalToStringConverter(), "Price"));
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<ProductDto>(new Product { Name = "Widget", Price = 9.99m });
            Assert.Equal("9.99", result.PriceDisplay);
        }

        [Fact]
        public void BeforeMap_runs_before_mapping()
        {
            // Before-map runs before member mapping; convention mapping overwrites Name afterward.
            // We verify before-map ran by setting a flag and a non-mapped member.
            var beforeCalled = false;
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Product, ProductDto>()
                    .BeforeMap((s, d) => { beforeCalled = true; d.Name = "PREFIX"; });
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<ProductDto>(new Product { Name = "Widget" });
            Assert.True(beforeCalled);
            // Name is overwritten by convention mapping after before-map
            Assert.Equal("Widget", result.Name);
        }

        [Fact]
        public void AfterMap_runs_after_mapping()
        {
            // After-map runs AFTER member mapping, so it can modify the final value.
            var afterCalled = false;
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Product, ProductDto>()
                    .AfterMap((s, d) => { afterCalled = true; d.Name = "SUFFIX-" + d.Name; });
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<ProductDto>(new Product { Name = "Widget" });
            Assert.True(afterCalled);
            Assert.Equal("SUFFIX-Widget", result.Name);
        }

        [Fact]
        public void ReverseMap_creates_reverse_type_map()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Product, ProductDto>().ReverseMap());
            var mapper = config.CreateMapper();

            var dto = new ProductDto { Name = "Widget" };
            var result = mapper.Map<Product>(dto);
            Assert.Equal("Widget", result.Name);
        }

        [Fact]
        public void ConstructUsing_creates_destination_custom()
        {
            // ConstructUsing controls construction; convention mapping still applies to members on top.
            // So we verify the custom ctor was invoked by checking a non-mapped member.
            var ctorInvoked = false;
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Product, ProductDto>()
                    .ConstructUsing((s, ctx) => { ctorInvoked = true; return new ProductDto { Name = s.Name }; }));
            var mapper = config.CreateMapper();

            var result = mapper.Map<ProductDto>(new Product { Name = "Widget" });
            Assert.True(ctorInvoked);
            Assert.Equal("Widget", result.Name);
        }

        [Fact]
        public void Profile_adds_maps()
        {
            var config = new MapperConfiguration(cfg =>
            {
                // Product->ProductDto is configured both here and in MyProfile; opt into the
                // additive merge so the duplicate pair does not throw (the default is false).
                cfg.AllowAdditiveTypeMapCreation = true;
                cfg.CreateMap<Product, ProductDto>();
                cfg.AddProfile(new MyProfile());
            });
            var mapper = config.CreateMapper();

            var result = mapper.Map<ProductDto>(new Product { Name = "Widget" });
            Assert.Equal("Widget", result.Name);
        }

        [Fact]
        public void AssertConfigurationIsValid_passes_for_valid_map()
        {
            // All destination members are mappable by convention
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Product, ProductDto>()
                .ForMember(d => d.PriceDisplay, opt => opt.MapFrom(s => s.Price.ToString())));
            var ex = Record.Exception(() => config.AssertConfigurationIsValid());
            Assert.Null(ex);
        }

        [Fact]
        public void AssertConfigurationIsValid_fails_for_unmapped_member()
        {
            // PriceDisplay is not mappable by convention
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Product, ProductDto>());
            var ex = Record.Exception(() => config.AssertConfigurationIsValid());
            Assert.IsType<MapItConfigurationException>(ex);
        }

        [Fact]
        public void MaxDepth_limits_recursion()
        {
            var config = new MapperConfiguration(cfg =>
                cfg.CreateMap<Node, NodeDto>().MaxDepth(2));
            var mapper = config.CreateMapper();

            var root = new Node { Name = "root", Next = new Node { Name = "child", Next = new Node { Name = "grandchild" } } };
            var result = mapper.Map<NodeDto>(root);
            Assert.Equal("root", result.Name);
            Assert.NotNull(result.Next);
            Assert.Equal("child", result.Next.Name);
        }

        public class MyProfile : Profile
        {
            public MyProfile()
            {
                CreateMap<Product, ProductDto>();
            }
        }

        public class Node
        {
            public string Name { get; set; }
            public Node Next { get; set; }
        }

        public class NodeDto
        {
            public string Name { get; set; }
            public NodeDto Next { get; set; }
        }

        public class ProductConverter : ITypeConverter<Product, ProductDto>
        {
            public ProductDto Convert(Product source, ProductDto destination, ResolutionContext context)
            {
                return new ProductDto
                {
                    Name = source.Name,
                    PriceDisplay = source.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)

                };
            }
        }

        public class PriceResolver : IValueResolver<Product, ProductDto, string>
        {
            public string Resolve(Product source, ProductDto destination, string destMember, ResolutionContext context)
            {
                return source.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
;
            }
        }

        public class DecimalToStringConverter : IValueConverter<decimal, string>
        {
            public string Convert(decimal sourceMember, ResolutionContext context)
            {
                return sourceMember.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
;
            }
        }
    }
}


