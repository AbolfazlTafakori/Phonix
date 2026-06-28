namespace Phonix.Api;

// Single source of truth for WHERE durable state lives. Everything that must survive a restart or a redeploy
// — store.json, the audit log, uploaded media, and the Data Protection key ring — is resolved from one base
// directory here, so they can never drift apart.
//
// The bug this fixes: the upload/keys/audit defaults used to hang off AppContext.BaseDirectory (the folder
// the app binary runs from). That is fine for the Docker deploy (the volume happens to mount there), but the
// native systemd deploy publishes each release into a NEW timestamped folder (/opt/phoenix/releases/<ts>/api)
// and flips a symlink. With the default tied to BaseDirectory, uploads landed inside the release folder and
// vanished on the next deploy — while store.json (explicitly pointed at /var/lib/phoenix) survived. The fix
// is to co-locate media/keys/audit with store.json, the one path every operator already configures.
public static class PersistentPaths
{
    // Resolution order:
    //   1. PHONIX_DATA_DIR        — explicit override for the whole state base.
    //   2. directory of PHONIX_DATA_FILE — co-locate with the store the operator already persists.
    //   3. AppContext.BaseDirectory/App_Data — dev fallback (NOT durable across a native redeploy).
    public static string BaseDir()
    {
        var explicitDir = Environment.GetEnvironmentVariable("PHONIX_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;

        var dataFile = Environment.GetEnvironmentVariable("PHONIX_DATA_FILE");
        if (!string.IsNullOrWhiteSpace(dataFile))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dataFile));
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }

        return Path.Combine(AppContext.BaseDirectory, "App_Data");
    }

    // Convenience: a named subdirectory/file under the persistent base.
    public static string Combine(string name) => Path.Combine(BaseDir(), name);
}
