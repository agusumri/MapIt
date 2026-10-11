using MapIt;
using MapIt.Extensions.Microsoft.DependencyInjection;
using MapIt.ProductApi;
using MapIt.ProductApi.Data;
using MapIt.ProductApi.Mapping;

var builder = WebApplication.CreateBuilder(args);

// Register MapIt with the entity -> DTO maps
builder.Services.AddMapIt(cfg =>
{
    cfg.CreateMap<Product, ProductDto>()
        .ForMember(d => d.FullDescription, opt => opt.MapFrom(s => s.Description));

    cfg.CreateMap<Order, OrderDto>()
        .ForMember(d => d.CustomerName, opt => opt.MapFrom((s, dest, member) => s.Customer?.Name ?? string.Empty))
        .ForMember(d => d.Total, opt => opt.MapFrom((s, dest, member) => s.Items.Sum(i => i.Price * i.Quantity)));

    cfg.CreateMap<OrderLine, OrderLineDto>();

    // Reverse maps so we can create entities from DTOs (write path)
    cfg.CreateMap<ProductCreateDto, Product>()
        .ReverseMap();
});

// Register and seed the in-memory store
var store = new ProductStore();
store.Seed();
builder.Services.AddSingleton(store);

var app = builder.Build();

app.UseRouting();

// GET /products — list all products (entity -> DTO)
app.MapGet("/products", (IMapper mapper) =>
{
    var products = store.GetProducts();
    return Results.Ok(mapper.Map<List<ProductDto>>(products));
});

// GET /products/{id} — single product
app.MapGet("/products/{id:int}", (int id, IMapper mapper) =>
{
    var product = store.GetProduct(id);
    return product is null ? Results.NotFound() : Results.Ok(mapper.Map<ProductDto>(product));
});

// POST /products — create a product (DTO -> entity)
app.MapPost("/products", (ProductCreateDto input, IMapper mapper, ProductStore store) =>
{
    var product = mapper.Map<Product>(input);
    product.Id = store.NextProductId();
    store.AddProduct(product);
    return Results.Created($"/products/{product.Id}", mapper.Map<ProductDto>(product));
});

// GET /orders — list orders (nested entity -> DTO)
app.MapGet("/orders", (IMapper mapper) =>
{
    var orders = store.GetOrders();
    return Results.Ok(mapper.Map<List<OrderDto>>(orders));
});

app.Run();
