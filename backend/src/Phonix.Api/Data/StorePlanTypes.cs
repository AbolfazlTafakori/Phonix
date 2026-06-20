namespace Phonix.Api.Data;

public partial class StoreData
{
    // global, admin-managed list of plan/service types (e.g. اشتراکی / اختصاصی) available to every product.
    private readonly List<string> _planTypes = new();

    public IReadOnlyList<string> GetPlanTypes()
    {
        lock (_gate) return _planTypes.ToList();
    }

    public bool AddPlanType(string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return false;
        lock (_gate)
        {
            if (_planTypes.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))) return false;
            _planTypes.Add(name);
            return true;
        }
    }

    // renames a type and updates every product plan that referenced the old name.
    public bool RenamePlanType(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return false;
        lock (_gate)
        {
            var index = _planTypes.FindIndex(t => string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            if (_planTypes.Any(t => string.Equals(t, newName, StringComparison.OrdinalIgnoreCase) && !string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase)))
                return false;

            _planTypes[index] = newName;
            foreach (var product in _products)
                foreach (var plan in product.Plans)
                    if (string.Equals(plan.Type, oldName, StringComparison.OrdinalIgnoreCase))
                        plan.Type = newName;
            return true;
        }
    }

    public bool RemovePlanType(string name)
    {
        name = (name ?? "").Trim();
        lock (_gate)
        {
            var existing = _planTypes.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null) return false;
            _planTypes.Remove(existing);
            return true;
        }
    }
}
