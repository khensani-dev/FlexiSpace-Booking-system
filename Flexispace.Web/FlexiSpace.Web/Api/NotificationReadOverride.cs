using Flexispace.Core.Models;
using Microsoft.JSInterop;

namespace Flexispace.Web.Api;

/// <summary>Persists read state for locally generated alerts in this browser.</summary>
public sealed class NotificationReadOverride(IJSRuntime js)
{
    private const string UnreadStorageKey = "flexispace.unreadOverrides.v1";
    private const string ReadStorageKey = "flexispace.readOverrides.v1";
    private readonly HashSet<Guid> _unread = [];
    private readonly HashSet<Guid> _read = [];
    private bool _loaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        await LoadIdsAsync(UnreadStorageKey, _unread);
        await LoadIdsAsync(ReadStorageKey, _read);
    }

    public async Task ForceUnreadAsync(Guid id)
    {
        await EnsureLoadedAsync();
        _read.Remove(id);
        _unread.Add(id);
        await PersistAsync();
    }

    public async Task ForceReadAsync(Guid id)
    {
        await EnsureLoadedAsync();
        _unread.Remove(id);
        _read.Add(id);
        await PersistAsync();
    }

    public async Task ClearAsync(Guid id)
    {
        await EnsureLoadedAsync();
        _unread.Remove(id);
        _read.Remove(id);
        await PersistAsync();
    }

    public async Task MarkAllReadAsync(IEnumerable<Guid> ids)
    {
        await EnsureLoadedAsync();
        _unread.Clear();
        _read.Clear();
        foreach (var id in ids)
            _read.Add(id);
        await PersistAsync();
    }

    public void Apply(IEnumerable<AppNotification> items)
    {
        foreach (var item in items)
        {
            if (_read.Contains(item.Id))
                item.IsRead = true;
            if (_unread.Contains(item.Id))
                item.IsRead = false;
        }
    }

    private async Task LoadIdsAsync(string key, HashSet<Guid> target)
    {
        try
        {
            var ids = await js.InvokeAsync<string[]>("flexispaceAlerts.getUnread", key);
            foreach (var id in ids ?? [])
            {
                if (Guid.TryParse(id, out var guid))
                    target.Add(guid);
            }
        }
        catch
        {
            // In-memory read state remains available if browser storage is blocked.
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            await js.InvokeVoidAsync("flexispaceAlerts.setUnread", UnreadStorageKey, _unread.Select(id => id.ToString()).ToArray());
            await js.InvokeVoidAsync("flexispaceAlerts.setUnread", ReadStorageKey, _read.Select(id => id.ToString()).ToArray());
        }
        catch
        {
            // Keep the in-memory state if browser storage is blocked.
        }
    }
}