namespace MapIt.ProductApi;

/// <summary>
/// DTOs exposed by the API. These are mapped from the domain entities by MapIt.
/// </summary>

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string FullDescription { get; set; } = null!;
    public decimal Price { get; set; }
    public bool InStock { get; set; }
}

public class ProductCreateDto
{
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal Price { get; set; }
    public bool InStock { get; set; } = true;
}

public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = null!;
    public List<OrderLineDto> Items { get; set; } = new();
    public decimal Total { get; set; }
}

public class OrderLineDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
