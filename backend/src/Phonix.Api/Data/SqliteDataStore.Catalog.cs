using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Catalog: products, categories, subscription plans, delivery templates, plan types.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Products ───────────────────────────────────────────────────────────────────────────────────────

    private static void NumberPlans(List<ProductPlan> plans)
    {
        for (var i = 0; i < plans.Count; i++) plans[i].Id = i + 1;
    }

    private void UpsertProduct(SqliteConnection conn, SqliteTransaction? tx, Product p)
    {
        var json = Serialize(p);
        conn.Execute(@"
INSERT INTO Products (Id, CategoryId, IsActive, Stock, DataJson)
VALUES (@Id, @CategoryId, @IsActive, @Stock, @DataJson)
ON CONFLICT(Id) DO UPDATE SET
    CategoryId=excluded.CategoryId, IsActive=excluded.IsActive, Stock=excluded.Stock, DataJson=excluded.DataJson;",
            new { p.Id, p.CategoryId, IsActive = p.IsActive ? 1 : 0, p.Stock, DataJson = json }, tx);
        if (tx is not null) AppendOutbox(conn, tx, "Products", p.Id, SyncOp.Upsert, json);
    }

    public Product? GetProduct(int id)
    {
        using var conn = OpenConnection();
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @id", new { id });
        return json is null ? null : Deserialize<Product>(json);
    }

    public IReadOnlyList<Product> GetProducts(int? categoryId = null, string? search = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM Products WHERE 1=1";
        if (categoryId is not null) sql += " AND CategoryId = @categoryId";
        sql += " ORDER BY Id;";
        var products = conn.Query<string>(sql, new { categoryId }).Select(j => Deserialize<Product>(j)!).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            products = products.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return products;
    }

    public Product AddProduct(Product product) =>
        WriteTx((conn, tx) =>
        {
            NumberPlans(product.Plans);
            var id = conn.ExecuteScalar<long>(@"
INSERT INTO Products (CategoryId, IsActive, Stock, DataJson) VALUES (@CategoryId, @IsActive, @Stock, @DataJson);
SELECT last_insert_rowid();",
                new { product.CategoryId, IsActive = product.IsActive ? 1 : 0, product.Stock, DataJson = Serialize(product) }, tx);
            product.Id = (int)id;
            var json = Serialize(product);
            conn.Execute("UPDATE Products SET DataJson = @DataJson WHERE Id = @Id",
                new { DataJson = json, product.Id }, tx);
            AppendOutbox(conn, tx, "Products", product.Id, SyncOp.Upsert, json);
            return product;
        });

    public bool UpdateProduct(Product product) =>
        WriteTx((conn, tx) =>
        {
            var exists = conn.ExecuteScalar<long>("SELECT COUNT(1) FROM Products WHERE Id = @Id", new { product.Id }, tx) > 0;
            if (!exists) return false;
            NumberPlans(product.Plans);
            UpsertProduct(conn, tx, product);
            return true;
        });

    public bool DeleteProduct(int id) =>
        WriteTx((conn, tx) =>
        {
            var deleted = conn.Execute("DELETE FROM Products WHERE Id = @id", new { id }, tx) > 0;
            if (deleted) AppendOutbox(conn, tx, "Products", id, SyncOp.Delete, null);
            return deleted;
        });


    // ── Categories ──────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Category> GetCategories() => AllJson<Category>("Categories").OrderBy(c => c.SortOrder).ToList();
    public Category? GetCategory(int id) => OneJson<Category>("Categories", id);
    public int CountProducts(int categoryId)
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Products WHERE CategoryId = @categoryId AND IsActive = 1", new { categoryId });
    }
    public Category AddCategory(Category category) { InsertJson("Categories", category, (c, id) => c.Id = id); return category; }
    public bool UpdateCategory(Category category)
    {
        var existing = GetCategory(category.Id);
        if (existing is null) return false;
        existing.Name = category.Name; existing.Slug = category.Slug; existing.Icon = category.Icon;
        existing.Description = category.Description;
        existing.IsActive = category.IsActive; existing.SortOrder = category.SortOrder;
        return UpdateJson("Categories", existing.Id, existing);
    }
    public bool DeleteCategory(int id) => DeleteRow("Categories", id);

    // ── Subscription plans ──────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<SubscriptionPlan> GetPlans() => AllJson<SubscriptionPlan>("Plans").OrderBy(p => p.Months).ToList();
    public SubscriptionPlan AddPlan(SubscriptionPlan plan) { InsertJson("Plans", plan, (p, id) => p.Id = id); return plan; }
    public bool UpdatePlan(SubscriptionPlan plan)
    {
        var existing = OneJson<SubscriptionPlan>("Plans", plan.Id);
        if (existing is null) return false;
        existing.Label = plan.Label; existing.Months = plan.Months; existing.Price = plan.Price;
        existing.PriceUsd = plan.PriceUsd; existing.DiscountPercent = plan.DiscountPercent;
        return UpdateJson("Plans", existing.Id, existing);
    }
    public bool DeletePlan(int id) => DeleteRow("Plans", id);

    // ── Product delivery templates ──────────────────────────────────────────────────────────────────────
    public IReadOnlyList<ProductDeliveryTemplate> GetDeliveryTemplates(int productId) =>
        GetProduct(productId)?.DeliveryTemplates.ToList() ?? (IReadOnlyList<ProductDeliveryTemplate>)Array.Empty<ProductDeliveryTemplate>();

    public ProductDeliveryTemplate? AddDeliveryTemplate(int productId, string title, string content) =>
        WriteTx<ProductDeliveryTemplate?>((conn, tx) =>
        {
            var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @productId", new { productId }, tx);
            if (pj is null) return null;
            var p = Deserialize<Product>(pj)!;
            var tpl = new ProductDeliveryTemplate
            {
                Id = (p.DeliveryTemplates.Count == 0 ? 0 : p.DeliveryTemplates.Max(x => x.Id)) + 1,
                ProductId = productId, Title = title.Trim(), TemplateContent = content,
            };
            p.DeliveryTemplates.Add(tpl);
            UpsertProduct(conn, tx, p);
            return tpl;
        });

    public bool DeleteDeliveryTemplate(int productId, int templateId) =>
        WriteTx((conn, tx) =>
        {
            var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @productId", new { productId }, tx);
            if (pj is null) return false;
            var p = Deserialize<Product>(pj)!;
            var removed = p.DeliveryTemplates.RemoveAll(x => x.Id == templateId) > 0;
            if (removed) UpsertProduct(conn, tx, p);
            return removed;
        });


    // ── Plan types (singleton list) ─────────────────────────────────────────────────────────────────────
    public IReadOnlyList<string> GetPlanTypes() => GetSingleton<List<string>>(PlanTypesKey);
    public bool AddPlanType(string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return false;
        return WriteTx((conn, tx) =>
        {
            var types = ReadSingleton<List<string>>(conn, tx, PlanTypesKey);
            if (types.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))) return false;
            types.Add(name);
            WriteSingleton(conn, tx, PlanTypesKey, types);
            return true;
        });
    }
    public bool RenamePlanType(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim(); newName = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return false;
        return WriteTx((conn, tx) =>
        {
            var types = ReadSingleton<List<string>>(conn, tx, PlanTypesKey);
            var index = types.FindIndex(t => string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            if (types.Any(t => string.Equals(t, newName, StringComparison.OrdinalIgnoreCase) && !string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase))) return false;
            types[index] = newName;
            WriteSingleton(conn, tx, PlanTypesKey, types);
            // cascade: update every product plan that referenced the old type name.
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Products", transaction: tx).ToList())
            {
                var p = Deserialize<Product>((string)row.DataJson)!;
                var touched = false;
                foreach (var plan in p.Plans)
                    if (string.Equals(plan.Type, oldName, StringComparison.OrdinalIgnoreCase)) { plan.Type = newName; touched = true; }
                if (touched) UpsertProduct(conn, tx, p);
            }
            return true;
        });
    }
    public bool RemovePlanType(string name)
    {
        name = (name ?? "").Trim();
        return WriteTx((conn, tx) =>
        {
            var types = ReadSingleton<List<string>>(conn, tx, PlanTypesKey);
            var existing = types.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null) return false;
            types.Remove(existing);
            WriteSingleton(conn, tx, PlanTypesKey, types);
            return true;
        });
    }
}
