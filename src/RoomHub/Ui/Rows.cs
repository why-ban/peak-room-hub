using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>View-level facts computed once per refresh instead of once per row (audit-ui UI-14).</summary>
internal readonly struct RowContext
{
    public readonly bool CanJoin;
    public readonly bool CanInvite;

    public RowContext(bool canJoin, bool canInvite)
    {
        CanJoin = canJoin;
        CanInvite = canInvite;
    }
}

/// <summary>Callbacks a row needs. Implemented by the page controller so rows hold no state of their own.</summary>
internal interface IRowHost
{
    void OnRoomClicked(RoomRecord room);

    void OnRoomJoin(RoomRecord room);

    void OnRoomView(RoomRecord room);

    void OnRoomDelete(RoomRecord room);

    void OnPlayerInvite(string steamId);

    void OnPlayerAddFriend(string steamId);

    void OnFriendJoin(FriendRoomInfo friend);

    void OnFriendInvite(ulong steamId);
}

/// <summary>
/// One pooled list row. Rows are created once per key and then only re-bound, so a refresh never
/// destroys and rebuilds the list (audit-ui UI-05) and never re-creates avatar sprites.
/// </summary>
internal abstract class RowView
{
    public readonly string Key;
    public readonly RectTransform Root;

    protected readonly Image Border;
    protected readonly Image Fill;
    protected readonly UiAvatar Avatar;
    protected readonly RectTransform TextColumn;
    protected readonly RectTransform Line1;
    protected readonly RectTransform Line2;
    protected readonly TextMeshProUGUI Line1Text;
    protected readonly TextMeshProUGUI Line2Text;
    protected readonly Image Line2Dot;

    private readonly List<UiChip> _line1Chips = new();
    private readonly List<RectTransform> _actionSlots = new();
    private Button? _rowButton;
    private string? _avatarId;

    protected RowView(string key, Transform parent, string name)
    {
        Key = key;
        Root = UiKit.Rect(parent, name);

        Border = UiKit.Panel(Root, "Border", UiTheme.RowBorder, UiTheme.RowRadius);
        UiKit.Stretch(Border.rectTransform);
        Border.raycastTarget = true;

        Fill = UiKit.Panel(Root, "Fill", UiTheme.RowBg, UiTheme.RowRadius - 1f);
        UiKit.Stretch(Fill.rectTransform);
        Fill.rectTransform.offsetMin = new Vector2(2f, 2f);
        Fill.rectTransform.offsetMax = new Vector2(-2f, -2f);

        Avatar = UiAvatar.Create(Root, "Avatar", UiTheme.AvatarSize);
        UiKit.Anchor(Avatar.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(UiTheme.RowPad, 0f), new Vector2(UiTheme.AvatarSize, UiTheme.AvatarSize));

        TextColumn = UiKit.Rect(Root, "Text");
        TextColumn.anchorMin = Vector2.zero;
        TextColumn.anchorMax = Vector2.one;
        TextColumn.offsetMin = new Vector2(UiTheme.RowPad + UiTheme.AvatarSize + UiTheme.AvatarGap, 0f);
        TextColumn.offsetMax = new Vector2(-UiTheme.RowPad, 0f);
        // labels overflow on purpose, so the column clips them instead of TMP truncating them
        TextColumn.gameObject.AddComponent<RectMask2D>();

        float block = UiTheme.Line1Height + UiTheme.LineSpacing + UiTheme.Line2Height;
        float top = (UiTheme.RowHeight - block) * 0.5f;

        Line1 = UiKit.Rect(TextColumn, "Line1");
        AnchorLine(Line1, top, UiTheme.Line1Height);
        Line1Text = UiKit.Label(Line1, "Text", "", UiTheme.Line1Size, UiTheme.Paper, FontStyles.Normal);
        UiKit.Stretch(Line1Text.rectTransform);

        Line2 = UiKit.Rect(TextColumn, "Line2");
        AnchorLine(Line2, top + UiTheme.Line1Height + UiTheme.LineSpacing, UiTheme.Line2Height);
        Line2Dot = UiKit.Dot(Line2, "Dot");
        UiKit.Anchor(Line2Dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(UiTheme.DotSize, UiTheme.DotSize));
        Line2Dot.gameObject.SetActive(false);
        Line2Text = UiKit.Label(Line2, "Text", "", UiTheme.Line2Size, UiTheme.TextDim, FontStyles.Normal);
        UiKit.Stretch(Line2Text.rectTransform);
    }

    public void SetActive(bool active)
    {
        if (Root.gameObject.activeSelf != active)
            Root.gameObject.SetActive(active);
    }

    public virtual void Dispose()
    {
        Avatar.Dispose();
        if (Root == null)
            return;
        // detach first: Object.Destroy is deferred, and a still-parented row would take part in
        // this frame's row layout after its replacement was already positioned
        Root.SetParent(null, false);
        UnityEngine.Object.Destroy(Root.gameObject);
    }

    /// <summary>First interactive element of the row, used as the page's initial selection.</summary>
    public Selectable? FirstSelectable()
    {
        for (int i = 0; i < _actionSlots.Count; i++)
        {
            var slot = _actionSlots[i];
            if (!slot.gameObject.activeSelf)
                continue;
            var selectable = slot.GetComponent<Selectable>();
            if (selectable != null && selectable.IsInteractable())
                return selectable;
        }
        return null;
    }

    /// <summary>Visible, interactable actions in visual left-to-right order.</summary>
    public List<Selectable> ActionSelectables()
    {
        var selectables = new List<Selectable>();
        for (int i = 0; i < _actionSlots.Count; i++)
        {
            var slot = _actionSlots[i];
            if (!slot.gameObject.activeSelf)
                continue;
            var selectable = slot.GetComponent<Selectable>();
            if (selectable != null && selectable.IsInteractable())
                selectables.Add(selectable);
        }
        return selectables;
    }

    /// <summary>Re-reads the cached texture; Steam swaps it underneath live rows (audit-ui UI-12).</summary>
    public void RefreshAvatar()
    {
        if (_avatarId == null)
            return;
        Avatar.SetTexture(SteamAvatarService.GetAvatar(_avatarId));
    }

    protected void SetAvatar(string? steamId, string? displayName)
    {
        string? id = string.IsNullOrEmpty(steamId) ? null : steamId;
        _avatarId = id;
        Avatar.SetName(displayName);
        Avatar.SetTexture(id == null ? null : SteamAvatarService.GetAvatar(id));
    }

    protected void MakeClickable(Action onClick)
    {
        var button = Root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = Border;
        button.onClick.AddListener(() => onClick());
        _rowButton = button;
    }

    protected UiButton AddButton(string name, UiRole role)
    {
        var button = UiButton.Create(Root, name, "", role);
        AnchorSlot(button.Rect);
        return button;
    }

    protected UiChip AddActionChip(string name, ChipKind kind)
    {
        var chip = UiChip.Create(Root, name, "", kind);
        AnchorSlot(chip.Rect);
        chip.SetVisible(false);
        return chip;
    }

    protected UiChip AddLine1Chip(string name, ChipKind kind)
    {
        var chip = UiChip.Create(Line1, name, "", kind);
        UiKit.Anchor(chip.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, UiTheme.ChipHeight));
        chip.SetVisible(false);
        _line1Chips.Add(chip);
        return chip;
    }

    protected void SetLine2Dot(bool visible)
    {
        Line2Dot.gameObject.SetActive(visible);
        Line2Text.rectTransform.offsetMin = new Vector2(visible ? UiTheme.DotSize + 10f : 0f, 0f);
    }

    protected void SetSurface(bool danger)
    {
        if (danger)
        {
            var fill = UiTheme.PanelColor(UiRole.Danger);
            Fill.color = new Color(fill.r, fill.g, fill.b, 0.55f);
            var line = UiTheme.BorderColor(UiRole.Danger);
            Border.color = new Color(line.r, line.g, line.b, 0.55f);
        }
        else
        {
            Fill.color = UiTheme.RowBg;
            Border.color = UiTheme.RowBorder;
        }
    }

    /// <summary>Right-aligns visible actions, then reserves their width out of the text column.</summary>
    protected void ApplyLayout()
    {
        float x = UiTheme.RowPad;
        for (int i = _actionSlots.Count - 1; i >= 0; i--)
        {
            var slot = _actionSlots[i];
            if (!slot.gameObject.activeSelf)
                continue;
            slot.anchoredPosition = new Vector2(-x, 0f);
            x += slot.sizeDelta.x + UiTheme.ButtonGap;
        }

        float actions = x > UiTheme.RowPad ? x - UiTheme.RowPad - UiTheme.ButtonGap : 0f;
        float inset = UiTheme.RowPad + (actions > 0f ? actions + UiTheme.ActionGap : 0f);
        TextColumn.offsetMax = new Vector2(-inset, 0f);

        float chips = 0f;
        for (int i = _line1Chips.Count - 1; i >= 0; i--)
        {
            var chip = _line1Chips[i];
            if (!chip.Rect.gameObject.activeSelf)
                continue;
            chip.Rect.anchoredPosition = new Vector2(-chips, 0f);
            chips += chip.Width + UiTheme.ButtonGap;
        }
        Line1Text.rectTransform.offsetMax = new Vector2(chips > 0f ? -chips : 0f, 0f);

        if (_rowButton != null)
        {
            var first = FirstSelectable();
            UiNav.Link(_rowButton, first, first, first, first);
        }
    }

    private void AnchorSlot(RectTransform rect)
    {
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        _actionSlots.Add(rect);
    }

    private static void AnchorLine(RectTransform rect, float topOffset, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, -topOffset);
    }
}

internal sealed class RoomRow : RowView
{
    private readonly IRowHost _host;
    private readonly UiButton _primary;
    private readonly UiButton _secondary;
    private RoomRecord? _room;
    private bool _deleteMode;

    private RoomRow(string key, Transform parent, IRowHost host)
        : base(key, parent, "Room_" + key)
    {
        _host = host;
        MakeClickable(() =>
        {
            if (_room == null)
                return;
            if (_deleteMode)
                _host.OnRoomDelete(_room);
            else
                _host.OnRoomClicked(_room);
        });

        _primary = AddButton("Join", UiRole.Success);
        _primary.Control.onClick.AddListener(() =>
        {
            if (_room == null)
                return;
            if (_deleteMode)
                _host.OnRoomDelete(_room);
            else
                _host.OnRoomJoin(_room);
        });

        _secondary = AddButton("View", UiRole.Info);
        _secondary.Control.onClick.AddListener(() =>
        {
            if (_room != null)
                _host.OnRoomView(_room);
        });
    }

    public static RoomRow Create(Transform parent, string key, IRowHost host)
    {
        return new RoomRow(key, parent, host);
    }

    public void Bind(RoomRecord room, bool deleteMode, RowContext context)
    {
        _room = room;
        _deleteMode = deleteMode;

        string host = string.IsNullOrWhiteSpace(room.HostName) ? Loc.T(Loc.UnknownHost) : room.HostName;
        Line1Text.text = Loc.Tf(Loc.RoomTitle, host);
        Line2Text.text = Loc.Tf(Loc.JoinedAgo, Loc.Relative(room.JoinedAtUtc))
                         + "  ·  "
                         + Loc.Tf(Loc.PlayerCount, room.Players.Count);
        SetLine2Dot(false);

        SetAvatar(room.HostSteamId, host);
        SetSurface(deleteMode);

        if (deleteMode)
        {
            _primary.SetVisible(true);
            _primary.SetRole(UiRole.Danger);
            _primary.SetLabel(Loc.T(Loc.DeleteRoom));
            _primary.SetInteractable(true);
            _secondary.SetVisible(false);
        }
        else
        {
            bool canJoin = context.CanJoin && JoinGate.IsJoinAllowed(room.LobbyId, room.JoinedAtUtcTicks);
            _primary.SetRole(UiRole.Success);
            _primary.SetLabel(Loc.T(Loc.Join));
            _primary.SetInteractable(true);
            _primary.SetVisible(canJoin);
            _secondary.SetVisible(true);
            _secondary.SetLabel(Loc.T(Loc.View));
        }

        ApplyLayout();
    }
}

internal sealed class PlayerRow : RowView
{
    private readonly IRowHost _host;
    private readonly UiButton _primary;
    private readonly UiButton _secondary;
    private readonly UiChip _hostChip;
    private readonly UiChip _friendChip;
    private string? _steamId;
    private bool _friend;

    private PlayerRow(string key, Transform parent, IRowHost host)
        : base(key, parent, "Player_" + key)
    {
        _host = host;
        _hostChip = AddLine1Chip("HostChip", ChipKind.Host);
        _friendChip = AddLine1Chip("FriendChip", ChipKind.Friend);

        _primary = AddButton("Invite", UiRole.Success);
        _primary.Control.onClick.AddListener(() =>
        {
            if (_steamId != null)
                _host.OnPlayerInvite(_steamId);
        });

        _secondary = AddButton("AddFriend", UiRole.Ghost);
        _secondary.Control.onClick.AddListener(() =>
        {
            if (_steamId != null && !_friend)
                _host.OnPlayerAddFriend(_steamId);
        });
    }

    public static PlayerRow Create(Transform parent, string key, IRowHost host)
    {
        return new PlayerRow(key, parent, host);
    }

    public void Bind(PlayerSearchHit hit, RowContext context)
    {
        string name = string.IsNullOrWhiteSpace(hit.DisplayName) ? Loc.T(Loc.UnknownPlayer) : hit.DisplayName;
        BindCore(
            hit.SteamId,
            name,
            hit.WasHost,
            Loc.Tf(Loc.MetAgo, Loc.Relative(hit.LastSeenUtc)),
            Loc.Tf(Loc.MetInRoom, string.IsNullOrWhiteSpace(hit.RoomHostName) ? Loc.T(Loc.UnknownHost) : hit.RoomHostName),
            context);
    }

    private void BindCore(string steamId, string name, bool wasHost, string metLine, string? roomLine, RowContext context)
    {
        _steamId = steamId;

        Line1Text.text = name;
        _hostChip.SetVisible(wasHost);
        if (wasHost)
            _hostChip.SetText(Loc.T(Loc.HostBadge), ChipKind.Host);

        _friend = SteamAvatarService.IsFriend(steamId);
        _friendChip.SetVisible(_friend);
        if (_friend)
            _friendChip.SetText(Loc.T(Loc.AlreadyFriend), ChipKind.Friend);

        Line2Text.text = roomLine == null ? metLine : metLine + "  ·  " + roomLine;
        SetLine2Dot(false);

        SetAvatar(steamId, name);
        SetSurface(false);

        string self = SteamAvatarService.LocalSteamIdString() ?? "";
        bool isSelf = string.Equals(self, steamId, StringComparison.Ordinal);

        bool canInvite = context.CanInvite && !isSelf;
        _primary.SetRole(UiRole.Success);
        _primary.SetLabel(Loc.T(Loc.Invite));
        _primary.SetInteractable(true);
        _primary.SetVisible(canInvite);

        bool showFriend = !isSelf;
        _secondary.SetVisible(showFriend);
        _secondary.SetLabel(_friend ? Loc.T(Loc.AlreadyFriend) : Loc.T(Loc.AddFriend));
        _secondary.SetRole(_friend ? UiRole.Disabled : UiRole.Ghost);
        _secondary.SetInteractable(!_friend);

        ApplyLayout();
    }
}

internal sealed class FriendRow : RowView
{
    private readonly IRowHost _host;
    private readonly UiButton _action;
    private readonly UiChip _statusChip;
    private FriendRoomInfo _friend;

    private FriendRow(string key, Transform parent, IRowHost host)
        : base(key, parent, "Friend_" + key)
    {
        _host = host;

        _action = AddButton("Action", UiRole.Success);
        _action.Control.onClick.AddListener(() =>
        {
            if (_friend.IsJoinable)
                _host.OnFriendJoin(_friend);
            else if (_friend.IsInvitable)
                _host.OnFriendInvite(_friend.SteamId);
        });

        _statusChip = AddActionChip("Status", ChipKind.Mute);
    }

    public static FriendRow Create(Transform parent, string key, IRowHost host)
    {
        return new FriendRow(key, parent, host);
    }

    public void Bind(FriendRoomInfo friend, RowContext context)
    {
        _friend = friend;

        string name = string.IsNullOrWhiteSpace(friend.Name) ? Loc.T(Loc.UnknownPlayer) : friend.Name;
        string status = FriendLabels.Status(friend.Mode, friend.Area);

        Line1Text.text = name;
        Line2Text.text = status;
        SetLine2Dot(friend.Mode == FriendGameMode.Joinable);

        SetAvatar(friend.SteamId.ToString(), name);
        SetSurface(false);

        bool canJoin = friend.IsJoinable && context.CanJoin;
        bool canInvite = friend.IsInvitable && context.CanInvite;

        if (canJoin)
        {
            _action.SetVisible(true);
            _action.SetRole(UiRole.Success);
            _action.SetLabel(Loc.T(Loc.Join));
            _action.SetInteractable(true);
            _statusChip.SetVisible(false);
        }
        else if (canInvite)
        {
            _action.SetVisible(true);
            _action.SetRole(UiRole.Success);
            _action.SetLabel(SteamInviteService.IsInvited(friend.SteamId.ToString())
                ? Loc.T(Loc.Invited)
                : Loc.T(Loc.Invite));
            _action.SetInteractable(!SteamInviteService.IsInvited(friend.SteamId.ToString()));
            _statusChip.SetVisible(false);
        }
        else
        {
            // the status is already on the second line; repeating it as a capsule squeezed the
            // name down to a few pixels on the narrow rail
            _action.SetVisible(false);
            _statusChip.SetVisible(false);
        }

        ApplyLayout();
    }
}

/// <summary>Key-indexed row store: same key reuses the same row, vanished keys are destroyed.</summary>
internal sealed class RowPool
{
    private readonly Func<string, RowView> _factory;
    private readonly Dictionary<string, RowView> _live = new();

    public RowPool(Func<string, RowView> factory)
    {
        _factory = factory;
    }

    public int Count => _live.Count;

    public IEnumerable<RowView> Live => _live.Values;

    public RowView Take(string key)
    {
        if (_live.TryGetValue(key, out var row))
        {
            if (row.Root != null)
                return row;
            // a destroyed row would leak the avatar sprite it owns
            row.Dispose();
            _live.Remove(key);
        }

        var created = _factory(key);
        _live[key] = created;
        return created;
    }

    /// <summary>Drops rows whose key is no longer present and returns the rows in display order.</summary>
    public List<RowView> Commit(List<RowView> ordered)
    {
        var keep = new HashSet<string>();
        foreach (var row in ordered)
            keep.Add(row.Key);

        List<string>? dead = null;
        foreach (var kv in _live)
        {
            if (keep.Contains(kv.Key))
                continue;
            dead ??= new List<string>();
            dead.Add(kv.Key);
        }

        if (dead != null)
        {
            foreach (var key in dead)
            {
                _live[key].Dispose();
                _live.Remove(key);
            }
        }

        foreach (var row in ordered)
            row.SetActive(true);

        return ordered;
    }

    public void Clear()
    {
        foreach (var row in _live.Values)
            row.Dispose();
        _live.Clear();
    }
}
