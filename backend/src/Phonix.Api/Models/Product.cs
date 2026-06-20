namespace Phonix.Api.Models;

public class ProductFeature
{
    public string Text { get; set; } = "";
    public bool Included { get; set; } = true;
}

public class ProductPlan
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int Months { get; set; }
    public long Price { get; set; }
    public int DiscountPercent { get; set; }
    public bool IsActive { get; set; } = true;

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public long Price { get; set; }
    public int DiscountPercent { get; set; }
    public long Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Featured { get; set; }
    public string Image { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Description { get; set; } = "";
    public string Warning { get; set; } = "";
    public List<ProductFeature> Features { get; set; } = new();
    public List<ProductPlan> Plans { get; set; } = new();

    public long FinalPrice => DiscountPercent <= 0
        ? Price
        : (long)Math.Round(Price * (1 - DiscountPercent / 100.0));
}
