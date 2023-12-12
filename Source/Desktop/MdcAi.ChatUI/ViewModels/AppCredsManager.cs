namespace MdcAi.ChatUI.ViewModels;

using Windows.Security.Credentials;

public static class AppCredsManager
{
    private static readonly PasswordVault _vault;
    public static readonly string ResourceName = "MdcAi";

    static AppCredsManager() { _vault = new PasswordVault(); }

    public static void SetValue(string name, string value)
    {
        var resName = "MdcAi";
        var credential = string.IsNullOrEmpty(value) ? null : new PasswordCredential(resName, name, value);

        // Remove the old credential if it exists
        try
        {
            var oldCredential = _vault.Retrieve(resName, name);
            _vault.Remove(oldCredential);
        }
        catch
        {
            /* Ignored if there's no existing credential */
        }

        if (credential != null)
            // Add the new credential
            _vault.Add(credential);
    }

    public static string GetValue(string name)
    {
        try
        {
            var credential = _vault.Retrieve(ResourceName, name);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }
}