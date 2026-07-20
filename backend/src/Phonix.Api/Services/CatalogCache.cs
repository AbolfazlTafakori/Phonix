using Phonix.Api.Dtos;

namespace Phonix.Api.Services;

// A short-lived cache for the two reads every anonymous visitor makes: the product listing and the category
// list. Both were hitting the store on every request — including every crawler and every page of a browsing
// session — to rebuild an answer that changes a few times a day.
//
// Two things keep it honest:
//   • Writers call Invalidate(), so an admin's edit shows up on the next request rather than after a delay.
//   • A short TTL backstops that, because the catalogue also moves without anyone editing it — the USD rate
//     service repices products in the background and every order decrements stock. The TTL bounds how stale a
//     listing can get if a writer is ever missed; it is deliberately seconds, not minutes.
//
// Only LIST reads are cached. A single product is fetched by id straight from the store, since that is the
// page a buyer acts on and it should never show a stale price or stock count.
public sealed class CatalogCache
{
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();

    private List<ProductDto>? _products;
    private List<CategoryDto>? _categories;
    private DateTime _filledUtc;

    public CatalogCache()
    {
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("PHONIX_CATALOG_CACHE_SECONDS"), out var s)
            ? s
            : 15;
        _ttl = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 300)); // 0 disables caching entirely
    }

    public bool Enabled => _ttl > TimeSpan.Zero;

    private bool Fresh => Enabled && DateTime.UtcNow - _filledUtc < _ttl;

    // Returns the cached listing, or builds it with `load` and keeps it. `load` runs outside the lock's
    // critical decision but under it for simplicity — the build is a millisecond-scale read and contention
    // here is far cheaper than the repeated store reads this replaces.
    public List<ProductDto> Products(Func<List<ProductDto>> load)
    {
        if (!Enabled) return load();
        lock (_gate)
        {
            if (_products is not null && Fresh) return _products;
            _products = load();
            _filledUtc = DateTime.UtcNow;
            return _products;
        }
    }

    public List<CategoryDto> Categories(Func<List<CategoryDto>> load)
    {
        if (!Enabled) return load();
        lock (_gate)
        {
            if (_categories is not null && Fresh) return _categories;
            _categories = load();
            _filledUtc = DateTime.UtcNow;
            return _categories;
        }
    }

    // Drops both lists. Called by every path that edits the catalogue, so an admin never has to wait out the
    // TTL to see their own change.
    public void Invalidate()
    {
        lock (_gate)
        {
            _products = null;
            _categories = null;
        }
    }
}
