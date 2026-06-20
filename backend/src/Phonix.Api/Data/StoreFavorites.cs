namespace Phonix.Api.Data;

public partial class StoreData
{
    private readonly Dictionary<int, HashSet<int>> _favorites = new();

    public IReadOnlyList<int> GetFavorites(int userId)
    {
        lock (_gate) return _favorites.TryGetValue(userId, out var set) ? set.ToList() : new List<int>();
    }

    public bool ToggleFavorite(int userId, int productId)
    {
        lock (_gate)
        {
            if (!_favorites.TryGetValue(userId, out var set))
            {
                set = new HashSet<int>();
                _favorites[userId] = set;
            }
            if (set.Remove(productId)) return false;
            set.Add(productId);
            return true;
        }
    }
}
