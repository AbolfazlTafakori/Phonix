using System.Collections.Concurrent;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Proves the SqliteDataStore oversell guard under genuine parallelism: many buyers hit a Stock=1 product at
// once, and the BEGIN IMMEDIATE transaction must let EXACTLY ONE win — never two, never negative stock.
public class SqliteConcurrencyTests
{
    private static SqliteDataStore FreshStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests");
        Directory.CreateDirectory(dir);
        return new SqliteDataStore(Path.Combine(dir, Guid.NewGuid() + ".db"));
    }

    [Fact]
    public void PlaceOrder_under_parallel_load_never_oversells_a_Stock1_product()
    {
        const int buyers = 50;
        var store = FreshStore();

        var product = store.AddProduct(new Product
        {
            Name = "محصول محدود", CategoryId = 1, Price = 100_000, Stock = 1, RequiredLevel = 1, IsActive = true,
        });

        // One distinct, level-1 account per buyer so the contention is on the PRODUCT row, not a shared user.
        var users = new List<AppUser>(buyers);
        for (var i = 0; i < buyers; i++)
            users.Add(store.RegisterUser(new AppUser { Username = $"buyer{i}", Name = $"Buyer {i}", VerificationLevel = 1 }));

        var results = new ConcurrentBag<PlaceOrderResult>();

        // Fire all buyers as simultaneously as possible. If any call threw (deadlock / SQLITE_BUSY beyond the
        // timeout / corruption), Parallel.For surfaces it as an AggregateException and the test fails — which
        // is itself part of the stability proof.
        Parallel.For(0, buyers, new ParallelOptions { MaxDegreeOfParallelism = buyers }, i =>
        {
            var r = store.PlaceOrder(users[i], new[] { (product.Id, 1, (int?)null) }, "test", fromWallet: false);
            results.Add(r);
        });

        var succeeded = results.Count(r => r.Error is null);
        var failed = results.Count(r => r.Error is not null);

        Assert.Equal(buyers, results.Count);           // every call returned a result (no swallowed exception)
        Assert.Equal(1, succeeded);                    // EXACTLY one buyer got the unit
        Assert.Equal(buyers - 1, failed);              // everyone else was rejected
        Assert.Equal(0, store.GetProduct(product.Id)!.Stock); // stock landed at 0 — never negative
        Assert.Single(store.GetOrders());              // exactly one order row was written
    }
}
