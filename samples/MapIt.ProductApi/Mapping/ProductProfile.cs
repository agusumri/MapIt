using MapIt;
using MapIt.ProductApi.Data;

namespace MapIt.ProductApi.Mapping;

/// <summary>
/// A MapIt profile that groups related maps. Demonstrates the Profile API.
/// (The same maps are also registered inline in Program.cs; this profile is
/// an alternative, organized way to define them.)
/// </summary>
public class ProductProfile : Profile
{
    public ProductProfile()
    {
        CreateMap<Product, ProductDto>()
            .ForMember(d => d.FullDescription, opt => opt.MapFrom(s => s.Description));

        CreateMap<Order, OrderDto>()
            .ForMember(d => d.CustomerName, opt => opt.MapFrom((s, dest, member) => s.Customer?.Name ?? string.Empty))
            .ForMember(d => d.Total, opt => opt.MapFrom((s, dest, member) => s.Items.Sum(i => i.Price * i.Quantity)));

        CreateMap<OrderLine, OrderLineDto>();
    }
}
