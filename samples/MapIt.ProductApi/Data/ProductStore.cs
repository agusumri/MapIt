namespace MapIt.ProductApi.Data;

/// <summary>
/// A simple in-memory data store for the sample API.
/// In a real app this would be an EF Core DbContext or repository.
/// </summary>
public class ProductStore
{
    private readonly List<Product> _products = new();
    private readonly List<Order> _orders = new();
    private int _nextProductId = 1;

    public void Seed()
    {
        _products.Add(new Product
        {
            Id = 1,
            Name = "Widget",
            Description = "A standard widget",
            Price = 9.99m,
            InStock = true
        });
        _products.Add(new Product
        {
            Id = 2,
            Name = "Gadget",
            Description = "An advanced gadget",
            Price = 49.99m,
            InStock = false
        });

        _orders.Add(new Order
        {
            Id = 1,
            Customer = new Customer { Name = "Alice" },
            Items = new List<OrderLine>
            {
                new OrderLine { ProductId = 1, Quantity = 2, Price = 9.99m },
                new OrderLine { ProductId = 2, Quantity = 1, Price = 49.99m }
            }
        });
    }

    public List<Product> GetProducts() => _products.ToList();

    public Product? GetProduct(int id) => _products.FirstOrDefault(p => p.Id == id);

    public List<Order> GetOrders() => _orders.ToList();

    public void AddProduct(Product product) => _products.Add(product);

    public int NextProductId() => _nextProductId++;
}
