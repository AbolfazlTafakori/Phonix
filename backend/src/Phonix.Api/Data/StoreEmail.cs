using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private EmailSettings _emailSettings = new();

    public EmailSettings GetEmailSettings()
    {
        lock (_gate) return _emailSettings;
    }

    public void UpdateEmailSettings(EmailSettings settings)
    {
        lock (_gate) _emailSettings = settings;
    }
}
