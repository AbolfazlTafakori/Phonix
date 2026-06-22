namespace Phonix.Api.Models;

// A named, reusable delivery message saved on a product. Staff write these once per product (e.g. a
// "نتفلیکس استاندارد" template and a "نتفلیکس پریمیوم" template) and then pick one from a dropdown in the
// deliver modal instead of retyping. Stored as a nested list on the product, so it persists with it.
public class ProductDeliveryTemplate
{
    public int Id { get; set; }          // unique within its product (stable across deletes)
    public int ProductId { get; set; }
    public string Title { get; set; } = "";
    public string TemplateContent { get; set; } = "";
}
