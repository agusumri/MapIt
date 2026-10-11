namespace MapIt.ProductApi.Data;

/// <summary>
/// Domain entities (these would normally come from your data layer / EF Core).
/// </summary>

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal Price { get; set; }
    public bool InStock { get; set; }
}

public class Customer
{
    public string Name { get; set; } = null!;
}

public class Order
{
    public int Id { get; set; }
    public Customer Customer { get; set; } = null!;
    public List<OrderLine> Items { get; set; } = new();
}

public class OrderLine
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
