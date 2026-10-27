using System.Net.Http.Json;
using System.Security.Claims;
using Flexispace.Core.Models;
using Flexispace.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Flexispace.Web.Api;

public sealed class ApiAuthService(
    FlexiSpaceApiClient api,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigation,
    IJSRuntime js) : IAuthService
{
    public User? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public bool IsGuest { get; private set; }
    public bool IsSessionReady { get; private set; }
    public string? SessionError { get; private set; }
    public event EventHandler? AuthStateChanged;
    private readonly SemaphoreSlim _sessionInitialization = new(1, 1);
    private string? _loadedIdentityKey;

    public async Task EnsureSessionAsync()
    {
        await _sessionInitialization.WaitAsync();
        try
        {
            await EnsureSessionCoreAsync();
        }
        finally
        {
            _sessionInitialization.Release();
        }
    }

    private async Task EnsureSessionCoreAsync()
    {
        SessionError = null;
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true)
        {
            if (IsGuest && CurrentUser is not null)
            {
                IsSessionReady = true;
                return;
            }

            var guest = false;
            try { guest = await js.InvokeAsync<bool>("flexispaceGuest.is"); } catch { }
            if (guest)
            {
                EnterGuest();
                IsSessionReady = true;
                AuthStateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            CurrentUser = null;
            _loadedIdentityKey = null;
            IsGuest = false;
            IsSessionReady = true;
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var identityKey = AccessTokenCache.UserKey(state.User)
                          ?? state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (CurrentUser is not null &&
            string.Equals(_loadedIdentityKey, identityKey, StringComparison.OrdinalIgnoreCase))
        {
            IsSessionReady = true;
            return;
        }

        // The same scoped service can survive an account switch in a Blazor circuit.
        CurrentUser = null;
        _loadedIdentityKey = null;
        IsSessionReady = false;

        // /me resolves the authenticated Entra object ID and provisions/links
        // the FlexiSpace row. There is no separate /user/register endpoint.
        var dto = await api.GetAsync<UserDto>("api/user/me");
        if (dto is null)
        {
            SessionError = "Microsoft signed you in, but FlexiSpace could not load your profile. Start the API and try again.";
            IsSessionReady = true;
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        CurrentUser = CatalogMapper.ToUser(dto);
        _loadedIdentityKey = identityKey;
        IsGuest = false;
        SessionError = null;
        var eventId = state.User.FindFirst("flexispace_login_event_id")?.Value
                      ?? state.User.FindFirst("jti")?.Value
                      ?? Guid.NewGuid().ToString("N");
        try { using var audit = await api.PostAsJsonAsync("api/sessionaudit/login", new { EventId = eventId }); } catch { }
        IsSessionReady = true;
        try { await js.InvokeVoidAsync("flexispaceGuest.set", false); } catch { }
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task MicrosoftSignInAsync()
    {
        navigation.NavigateTo("signin-microsoft?redirectUri=/auth/complete", forceLoad: true);
        return Task.CompletedTask;
    }

    public async Task ContinueAsGuestAsync()
    {
        EnterGuest();
        try { await js.InvokeVoidAsync("flexispaceGuest.set", true); } catch { }
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        navigation.NavigateTo("/home");
    }

    private void EnterGuest()
    {
        IsGuest = true;
        SessionError = null;
        CurrentUser = new User
        {
            Name = "Guest",
            FirstName = "Guest",
            Role = UserRole.Client,
            Email = string.Empty,
            IsActive = true
        };
    }

    public Task<bool> LoginAsync(string email, string password)
    {
        _ = email;
        _ = password;
        return Task.FromResult(false);
    }

    public async Task LogoutAsync()
    {
        var guest = IsGuest;
        if (!guest && CurrentUser is not null)
        {
            try { using var audit = await api.PostAsJsonAsync("api/sessionaudit/logout", new { }); } catch { }
        }
        CurrentUser = null;
        _loadedIdentityKey = null;
        IsGuest = false;
        IsSessionReady = true;
        SessionError = null;
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        try { await js.InvokeVoidAsync("flexispaceGuest.set", false); } catch { }
        navigation.NavigateTo(guest ? "/" : "signout-microsoft", forceLoad: true);
    }

    public Task SwitchDemoUserAsync(string email)
    {
        _ = email;
        return Task.CompletedTask;
    }

    public IReadOnlyList<User> GetDemoUsers() => [];
}
