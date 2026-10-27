using Microsoft.JSInterop;

namespace Flexispace.Web.Services;

// Stores POPIA privacy-policy acceptance in the browser so it is required before sign-in.
public class PrivacyConsentService(IJSRuntime js)
{
    public const string StorageKey = "flexispace.privacyAccepted.v1";

    private bool? _accepted;

    public async Task<bool> HasAcceptedAsync()
    {
        if (_accepted.HasValue)
            return _accepted.Value;

        try
        {
            var accepted = await js.InvokeAsync<string?>("window.localStorage.getItem", StorageKey);
            _accepted = string.Equals(accepted, "1", StringComparison.Ordinal);
        }
        catch
        {
            _accepted = false;
        }

        return _accepted.Value;
    }

    public async Task AcceptAsync()
    {
        try
        {
            await js.InvokeVoidAsync("window.localStorage.setItem", StorageKey, "1");
        }
        finally
        {
            // The user explicitly accepted the policy in this browser session.
            // Keep the current circuit usable even if persistent storage is blocked.
            _accepted = true;
        }
    }
}
