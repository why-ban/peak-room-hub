using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoomHub.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoomHub;

/// <summary>
/// Room Hub page shell: responsive chrome, three views, pooled rows. The panel never clones game UI
/// widgets and never reloads game art (docs/ui-spec.md §0).
/// </summary>
internal sealed class HistoryPageController : IRowHost
{
    private const string PlayersViewId = "__players__";
    private const string SearchViewId = "__search__";
    private const float DoubleClickSeconds = 0.4f;
    private const float RailGap = 24f;

    private enum RowKind
    {
        None,
        Rooms,
        Players,
    }

    private RoomHubPage? _page;
    private RectTransform? _card;
    private RectTransform? _body;
    private RectTransform? _mainColumn;
    private RectTransform? _listRoot;
    private RectTransform? _emptyRoot;
    private RectTransform? _rail;
    private RectTransform? _banner;

    private UiScrollView? _list;
    private UiScrollView? _railList;
    private TextMeshProUGUI? _titleText;
    private TextMeshProUGUI? _subtitleText;
    private TextMeshProUGUI? _hintText;
    private TextMeshProUGUI? _emptyTitle;
    private TextMeshProUGUI? _emptyHint;
    private TextMeshProUGUI? _bannerText;
    private TextMeshProUGUI? _railTitle;
    private TextMeshProUGUI? _railHint;
    private TextMeshProUGUI? _railEmpty;
    private UiChip? _statChip;
    private UiButton? _tabRooms;
    private UiButton? _tabPlayers;
    private UiButton? _searchButton;
    private UiButton? _clearButton;
    private UiButton? _deleteButton;
    private UiButton? _closeButton;
    private UiTextInput? _searchInput;

    private RowPool? _mainPool;
    private RowPool? _railPool;
    private List<RowView> _mainRows = new();
    private List<RowView> _railRows = new();
    private RowKind _mainKind = RowKind.None;
    private RowContext _context;
    private int _playerTotal;

    private bool _deleteMode;
    private string? _viewingLobbyId;
    private string? _searchQuery;
    private string? _lastClickLobbyId;
    private float _lastClickTime;
    private GameObject? _lastSelection;
    private bool _openedViaMenuWindow;

    private static readonly MethodInfo? MenuWindowOpen =
        typeof(MenuWindow).GetMethod("Open", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo? MenuWindowClose =
        typeof(MenuWindow).GetMethod("Close", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public bool HasLivePage
    {
        get
        {
            if (_page == null)
                return false;
            var component = (Component)_page;
            return component != null;
        }
    }

    public bool IsPageOpen
    {
        get
        {
            if (!HasLivePage || !_openedViaMenuWindow)
                return false;
            try
            {
                return ((Component)_page!).gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Open()
    {
        if (IsPageOpen)
            return;

        _deleteMode = false;
        _viewingLobbyId = null;
        _searchQuery = null;
        _lastClickLobbyId = null;

        HistoryUiFont.Resolve();
        EnsurePage();
        if (!HasLivePage)
            return;

        if (_searchInput != null)
            _searchInput.Text = "";

        try
        {
            // flags must be set before Open so GUIManager.UpdateWindowStatus sees them
            _page!.BlocksPlayerInput = true;
            _page.ShowCursorWhileOpen = true;
            _page.CloseOnUICancel = false;
            _page.CloseOnPause = false;
            _page.SelectOnOpen = true;

            // card size first: the chrome layout derives the search width from it
            _page.Layout();
            RefreshAll();
            _page.SetInitialSelection(FirstInteractive());

            if (!OpenMenuWindow())
            {
                if (HistoryUi.IsTitleScene)
                {
                    // title only: no look lock to preserve, and StartClosed must not undo this
                    _page.SkipStartClosed();
                    ((MenuWindow)_page).Show();
                    ((MenuWindow)_page).SetInputActive(true);
                    _openedViaMenuWindow = true;
                    Plugin.Log.LogWarning("MenuWindow.Open unavailable — using Show on title.");
                }
                else
                {
                    Plugin.Log.LogError("MenuWindow.Open unavailable in-game — aborting open.");
                    Discard();
                    return;
                }
            }
            else
            {
                _openedViaMenuWindow = true;
            }

            _page.ApplySorting(apply: true);
            FocusInitial();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"History page Open failed: {ex}");
            Discard();
        }
    }

    public void Hide()
    {
        SteamInviteService.ResetInvited();
        ReleaseFocus();

        if (!HasLivePage)
        {
            _openedViaMenuWindow = false;
            return;
        }

        try { _page!.ApplySorting(apply: false); }
        catch { /* destroyed */ }

        try { CloseMenuWindow(); }
        catch { /* destroyed */ }

        _openedViaMenuWindow = false;

        try
        {
            if (HasLivePage && ((Component)_page!).gameObject.activeInHierarchy)
                ((MenuWindow)_page!).Hide();
        }
        catch { /* ignore */ }
    }

    public void Discard()
    {
        SteamInviteService.ResetInvited();
        ReleaseFocus();
        Unsubscribe();

        try { if (HasLivePage) CloseMenuWindow(); } catch { /* ignore */ }
        try { if (HasLivePage) UnityEngine.Object.Destroy(((Component)_page!).gameObject); } catch { /* ignore */ }

        _mainPool?.Clear();
        _railPool?.Clear();
        _mainRows = new List<RowView>();
        _railRows = new List<RowView>();
        _mainKind = RowKind.None;
        _lastSelection = null;

        _page = null;
        _card = null;
        _body = null;
        _mainColumn = null;
        _listRoot = null;
        _emptyRoot = null;
        _rail = null;
        _banner = null;
        _list = null;
        _railList = null;
        _titleText = null;
        _subtitleText = null;
        _hintText = null;
        _emptyTitle = null;
        _emptyHint = null;
        _bannerText = null;
        _railTitle = null;
        _railEmpty = null;
        _statChip = null;
        _tabRooms = null;
        _tabPlayers = null;
        _searchButton = null;
        _clearButton = null;
        _deleteButton = null;
        _closeButton = null;
        _searchInput = null;
        _mainPool = null;
        _railPool = null;
        _deleteMode = false;
        _viewingLobbyId = null;
        _searchQuery = null;
        _openedViaMenuWindow = false;
    }

    public void HandleBackOrClose()
    {
        if (!IsPageOpen)
            return;

        // ESC must always work: a focused field would otherwise swallow it for good (audit-ui UI-02)
        if (_searchInput != null && _searchInput.IsFocused)
        {
            _searchInput.Deactivate();
            FocusInitial();
        }

        if (_viewingLobbyId != null)
        {
            ShowRoomsView();
            return;
        }

        if (_deleteMode)
        {
            _deleteMode = false;
            RefreshAll();
            return;
        }

        Hide();
    }

    public void TickAvatarRefresh()
    {
        if (!IsPageOpen)
            return;
        if (Time.frameCount % 45 != 0)
            return;

        foreach (var row in _mainPool?.Live ?? Enumerable.Empty<RowView>())
            row.RefreshAvatar();
        foreach (var row in _railPool?.Live ?? Enumerable.Empty<RowView>())
            row.RefreshAvatar();
    }

    /// <summary>
    /// True only while the panel's own field has TMP focus. EventSystem selection is not a usable
    /// proxy: TMP_InputField.DeactivateInputField never clears it and NavigationContainerHandler
    /// deliberately keeps it, which used to leave the mod's hotkeys dead (audit R2-01).
    /// </summary>
    public bool IsSearchFocused => _searchInput != null && _searchInput.IsFocused;

    /// <summary>Scrolls the focused element into view when the gamepad selection moves (spec §6).</summary>
    public void TickSelection()
    {
        if (!IsPageOpen)
            return;

        var events = EventSystem.current;
        var selected = events == null ? null : events.currentSelectedGameObject;
        if (selected == _lastSelection)
            return;

        _lastSelection = selected;
        if (selected == null)
            return;

        var rect = selected.transform as RectTransform;
        if (rect == null)
            return;

        if (IsInside(rect, _list?.Content))
            _list!.EnsureVisible(rect);
        else if (IsInside(rect, _railList?.Content))
            _railList!.EnsureVisible(rect);
    }

    private static bool IsInside(Transform? child, RectTransform? container)
    {
        if (child == null || container == null)
            return false;

        for (var current = child; current != null; current = current.parent)
        {
            if (current == container)
                return true;
        }
        return false;
    }

    private void EnsurePage()
    {
        if (HasLivePage)
            return;
        if (_page != null)
            Discard();

        _page = new GameObject("Room Hub").AddComponent<RoomHubPage>();
        _page.CloseOnUICancel = false;
        _page.CloseOnPause = false;
        _page.BlocksPlayerInput = true;
        _page.ShowCursorWhileOpen = true;
        _page.SelectOnOpen = true;

        var scrim = UiKit.Panel(_page.transform, "Scrim", UiTheme.Scrim, 0f);
        UiKit.Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;

        BuildCard();
        BuildHeader();
        BuildTabs();
        BuildBody();
        BuildFooter();
        _page.Layout();
        _page.Resized += OnPageResized;

        JoinGate.StateChanged += OnJoinGateChanged;
        FriendRoomsService.Changed += OnFriendsChanged;
        LocalizationSync.Register(OnGameLanguageChanged);
    }

    private void Unsubscribe()
    {
        if (_page != null)
            _page.Resized -= OnPageResized;
        JoinGate.StateChanged -= OnJoinGateChanged;
        FriendRoomsService.Changed -= OnFriendsChanged;
        LocalizationSync.Unregister(OnGameLanguageChanged);
    }

    private void BuildCard()
    {
        _card = UiKit.Card(_page!.transform, "Card");
        UiKit.Anchor(_card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(UiTheme.CardMaxWidth, UiTheme.CardMaxHeight));
        _page.SetCard(_card);
    }

    private void BuildHeader()
    {
        var header = UiKit.Rect(_card!, "Header");
        AnchorTop(header, UiTheme.HeaderHeight, 0f);

        float titleTop = 12f;
        var titleBox = UiKit.ClipBox(header, "TitleBox");
        titleBox.anchorMin = new Vector2(0f, 1f);
        titleBox.anchorMax = new Vector2(1f, 1f);
        titleBox.pivot = new Vector2(0.5f, 1f);
        titleBox.offsetMin = new Vector2(UiTheme.Pad, -titleTop - UiTheme.TitleHeight);
        titleBox.offsetMax = new Vector2(-420f, -titleTop);
        _titleText = UiKit.Label(titleBox, "Title", Loc.T(Loc.PageTitle), UiTheme.TitleSize, UiTheme.Paper, FontStyles.UpperCase);
        UiKit.Stretch(_titleText.rectTransform);

        float subtitleTop = titleTop + UiTheme.TitleHeight;
        var subtitleBox = UiKit.ClipBox(header, "SubtitleBox");
        subtitleBox.anchorMin = new Vector2(0f, 1f);
        subtitleBox.anchorMax = new Vector2(1f, 1f);
        subtitleBox.pivot = new Vector2(0.5f, 1f);
        subtitleBox.offsetMin = new Vector2(UiTheme.Pad, -subtitleTop - UiTheme.SubtitleHeight);
        subtitleBox.offsetMax = new Vector2(-420f, -subtitleTop);
        _subtitleText = UiKit.Label(subtitleBox, "Subtitle", Loc.T(Loc.Subtitle), UiTheme.SubtitleSize, UiTheme.TextDim, FontStyles.Normal);
        UiKit.Stretch(_subtitleText.rectTransform);

        _statChip = UiChip.Create(header, "Stat", "", ChipKind.Mute);
        UiKit.Anchor(_statChip.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-UiTheme.Pad, 0f), new Vector2(0f, UiTheme.ChipHeight));

        var rule = UiKit.Divider(_card!, "Rule");
        var ruleRect = rule.rectTransform;
        ruleRect.anchorMin = new Vector2(0f, 1f);
        ruleRect.anchorMax = new Vector2(1f, 1f);
        ruleRect.pivot = new Vector2(0.5f, 1f);
        ruleRect.sizeDelta = new Vector2(-UiTheme.Pad * 2f, UiTheme.RuleHeight);
        ruleRect.anchoredPosition = new Vector2(0f, -UiTheme.HeaderHeight);
    }

    private void BuildTabs()
    {
        var tabs = UiKit.Rect(_card!, "Tabs");
        AnchorTop(tabs, UiTheme.TabsHeight, UiTheme.HeaderHeight + UiTheme.RuleHeight);

        _tabRooms = UiButton.Create(tabs, "TabRooms", Loc.T(Loc.TabRooms), UiRole.Primary);
        UiKit.Anchor(_tabRooms.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(UiTheme.Pad, -UiTheme.TabsTopPad), new Vector2(UiTheme.TabMinWidth, UiTheme.TabHeight));
        _tabRooms.Control.onClick.AddListener(ShowRoomsView);

        _tabPlayers = UiButton.Create(tabs, "TabPlayers", Loc.T(Loc.TabPlayers), UiRole.Ghost);
        UiKit.Anchor(_tabPlayers.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(UiTheme.Pad + UiTheme.TabMinWidth + UiTheme.TabGap, -UiTheme.TabsTopPad), new Vector2(UiTheme.TabMinWidth, UiTheme.TabHeight));
        _tabPlayers.Control.onClick.AddListener(ShowAllPlayersView);

        _searchButton = UiButton.Create(tabs, "SearchButton", Loc.T(Loc.Search), UiRole.Ghost);
        UiKit.Anchor(_searchButton.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiTheme.Pad, -UiTheme.TabsTopPad), new Vector2(150f, UiTheme.ButtonHeight));
        _searchButton.Control.onClick.AddListener(RunSearch);

        _clearButton = UiButton.Create(tabs, "ClearButton", Loc.T(Loc.SearchClear), UiRole.Ghost);
        _clearButton.SetVisible(false);
        _clearButton.Control.onClick.AddListener(ShowRoomsView);

        _searchInput = UiTextInput.Create(tabs, "SearchField", UiTheme.SearchWidth, UiTheme.SearchHeight, RunSearch);
        UiKit.Anchor(_searchInput.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiTheme.Pad, -UiTheme.TabsTopPad), new Vector2(UiTheme.SearchWidth, UiTheme.SearchHeight));
        _searchInput.SetPlaceholder(Loc.T(Loc.SearchPlaceholder));
    }

    private void BuildBody()
    {
        _body = UiKit.Rect(_card!, "Body");
        _body.anchorMin = Vector2.zero;
        _body.anchorMax = Vector2.one;
        _body.offsetMin = new Vector2(UiTheme.Pad, UiTheme.FooterHeight);
        _body.offsetMax = new Vector2(-UiTheme.Pad, -(UiTheme.HeaderHeight + UiTheme.RuleHeight + UiTheme.TabsHeight));

        _mainColumn = UiKit.Rect(_body, "Main");
        _mainColumn.anchorMin = Vector2.zero;
        _mainColumn.anchorMax = Vector2.one;
        _mainColumn.offsetMin = Vector2.zero;
        _mainColumn.offsetMax = new Vector2(-(UiTheme.RailWidth + RailGap), 0f);

        _banner = UiKit.Rect(_mainColumn, "Banner");
        var bannerLine = UiKit.Panel(_banner, "Line", UiTheme.BannerBorder, UiTheme.BannerRadius);
        UiKit.Stretch(bannerLine.rectTransform);
        var bannerFill = UiKit.Panel(_banner, "Fill", UiTheme.BannerBg, UiTheme.BannerRadius - 1f);
        UiKit.Stretch(bannerFill.rectTransform);
        bannerFill.rectTransform.offsetMin = new Vector2(2f, 2f);
        bannerFill.rectTransform.offsetMax = new Vector2(-2f, -2f);
        var bannerBox = UiKit.ClipBox(_banner, "TextBox");
        UiKit.Stretch(bannerBox);
        bannerBox.offsetMin = new Vector2(20f, 0f);
        bannerBox.offsetMax = new Vector2(-20f, 0f);
        _bannerText = UiKit.Label(bannerBox, "Text", Loc.T(Loc.DeleteBanner), UiTheme.BannerSize, UiTheme.BannerText, FontStyles.Normal);
        UiKit.Stretch(_bannerText.rectTransform);
        _bannerText.alignment = TextAlignmentOptions.MidlineLeft;
        _banner.anchorMin = new Vector2(0f, 1f);
        _banner.anchorMax = new Vector2(1f, 1f);
        _banner.pivot = new Vector2(0.5f, 1f);
        _banner.sizeDelta = new Vector2(0f, UiTheme.BannerHeight);
        _banner.anchoredPosition = Vector2.zero;
        _banner.gameObject.SetActive(false);

        _listRoot = UiKit.Rect(_mainColumn, "ListRoot");
        _listRoot.anchorMin = Vector2.zero;
        _listRoot.anchorMax = Vector2.one;
        _listRoot.offsetMin = Vector2.zero;
        _listRoot.offsetMax = Vector2.zero;

        _list = UiScrollView.Create(_listRoot, "RoomHubList");
        UiKit.Stretch(_list.Root);

        _emptyRoot = UiKit.Rect(_mainColumn, "Empty");
        UiKit.Stretch(_emptyRoot);
        var emptyTitleBox = UiKit.ClipBox(_emptyRoot, "TitleBox");
        emptyTitleBox.anchorMin = new Vector2(0f, 0.5f);
        emptyTitleBox.anchorMax = new Vector2(1f, 0.5f);
        emptyTitleBox.pivot = new Vector2(0.5f, 0.5f);
        emptyTitleBox.sizeDelta = new Vector2(0f, UiTheme.EmptyTitleHeight);
        emptyTitleBox.anchoredPosition = new Vector2(0f, 26f);
        _emptyTitle = UiKit.Label(emptyTitleBox, "Title", "", UiTheme.EmptyTitleSize, UiTheme.Paper, FontStyles.Normal, wrap: true);
        UiKit.Stretch(_emptyTitle.rectTransform);
        _emptyTitle.alignment = TextAlignmentOptions.Center;

        var emptyHintBox = UiKit.ClipBox(_emptyRoot, "HintBox");
        emptyHintBox.anchorMin = new Vector2(0f, 0.5f);
        emptyHintBox.anchorMax = new Vector2(1f, 0.5f);
        emptyHintBox.pivot = new Vector2(0.5f, 0.5f);
        emptyHintBox.sizeDelta = new Vector2(0f, UiTheme.EmptyHintHeight);
        emptyHintBox.anchoredPosition = new Vector2(0f, -28f);
        _emptyHint = UiKit.Label(emptyHintBox, "Hint", "", UiTheme.EmptyHintSize, UiTheme.TextFaint, FontStyles.Normal, wrap: true);
        UiKit.Stretch(_emptyHint.rectTransform);
        _emptyHint.alignment = TextAlignmentOptions.Center;
        _emptyRoot.gameObject.SetActive(false);

        BuildRail();

        _mainPool = new RowPool(CreateMainRow);
        _railPool = new RowPool(key => FriendRow.Create(_railList!.Content, key, this));
    }

    private void BuildRail()
    {
        _rail = UiKit.Rect(_body!, "Rail");
        _rail.anchorMin = new Vector2(1f, 0f);
        _rail.anchorMax = new Vector2(1f, 1f);
        _rail.pivot = new Vector2(1f, 0.5f);
        _rail.sizeDelta = new Vector2(UiTheme.RailWidth, 0f);
        _rail.anchoredPosition = Vector2.zero;

        var line = UiKit.Divider(_rail, "Line");
        var lineRect = line.rectTransform;
        lineRect.anchorMin = new Vector2(0f, 0f);
        lineRect.anchorMax = new Vector2(0f, 1f);
        lineRect.pivot = new Vector2(0f, 0.5f);
        lineRect.sizeDelta = new Vector2(UiTheme.RailLineWidth, 0f);
        lineRect.anchoredPosition = Vector2.zero;
        line.color = new Color(UiTheme.Divider.r, UiTheme.Divider.g, UiTheme.Divider.b, 0.5f);

        _railTitle = UiKit.Label(_rail, "Title", "", UiTheme.RailTitleSize, UiTheme.Gold, FontStyles.UpperCase);
        var railTitle = _railTitle.rectTransform;
        railTitle.anchorMin = new Vector2(0f, 1f);
        railTitle.anchorMax = new Vector2(1f, 1f);
        railTitle.pivot = new Vector2(0.5f, 1f);
        railTitle.offsetMin = new Vector2(UiTheme.RailInset, -UiTheme.RailTitleHeight);
        railTitle.offsetMax = new Vector2(0f, 0f);

        _railHint = UiKit.Label(_rail, "Hint", Loc.T(Loc.RailHint), UiTheme.RailHintSize, UiTheme.TextFaint, FontStyles.Normal);
        var railHint = _railHint.rectTransform;
        railHint.anchorMin = new Vector2(0f, 1f);
        railHint.anchorMax = new Vector2(1f, 1f);
        railHint.pivot = new Vector2(0.5f, 1f);
        railHint.offsetMin = new Vector2(UiTheme.RailInset, -UiTheme.RailTitleHeight - UiTheme.RailHintHeight);
        railHint.offsetMax = new Vector2(0f, -UiTheme.RailTitleHeight);

        var railRoot = UiKit.Rect(_rail, "ListRoot");
        railRoot.anchorMin = Vector2.zero;
        railRoot.anchorMax = Vector2.one;
        railRoot.offsetMin = new Vector2(UiTheme.RailInset, 0f);
        railRoot.offsetMax = new Vector2(0f, -(UiTheme.RailTitleHeight + UiTheme.RailHintHeight + 6f));

        _railList = UiScrollView.Create(railRoot, "RoomHubFriendList");
        UiKit.Stretch(_railList.Root);

        var railEmptyBox = UiKit.ClipBox(railRoot, "EmptyBox");
        railEmptyBox.anchorMin = new Vector2(0f, 1f);
        railEmptyBox.anchorMax = new Vector2(1f, 1f);
        railEmptyBox.pivot = new Vector2(0.5f, 1f);
        railEmptyBox.sizeDelta = new Vector2(0f, 64f);
        railEmptyBox.anchoredPosition = new Vector2(0f, -12f);
        _railEmpty = UiKit.Label(railEmptyBox, "Empty", Loc.T(Loc.FriendsEmpty), UiTheme.RailEmptySize, UiTheme.TextFaint, FontStyles.Normal, wrap: true);
        UiKit.Stretch(_railEmpty.rectTransform);
    }

    private void BuildFooter()
    {
        var footer = UiKit.Rect(_card!, "Footer");
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.sizeDelta = new Vector2(0f, UiTheme.FooterHeight);
        footer.anchoredPosition = Vector2.zero;

        var hintBox = UiKit.ClipBox(footer, "HintBox");
        hintBox.anchorMin = new Vector2(0f, 0.5f);
        hintBox.anchorMax = new Vector2(1f, 0.5f);
        hintBox.pivot = new Vector2(0.5f, 0.5f);
        hintBox.offsetMin = new Vector2(UiTheme.Pad, -UiTheme.HintHeight * 0.5f);
        hintBox.offsetMax = new Vector2(-UiTheme.Pad - 340f, UiTheme.HintHeight * 0.5f);
        _hintText = UiKit.Label(hintBox, "Hint", "", UiTheme.HintSize, UiTheme.TextFaint, FontStyles.Normal);
        UiKit.Stretch(_hintText.rectTransform);

        _closeButton = UiButton.Create(footer, "Close", Loc.T(Loc.Close), UiRole.Primary);
        UiKit.Anchor(_closeButton.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-UiTheme.Pad, 0f), new Vector2(150f, UiTheme.ButtonHeight));
        _closeButton.Control.onClick.AddListener(() =>
        {
            _deleteMode = false;
            Hide();
        });

        _deleteButton = UiButton.Create(footer, "DeleteMode", Loc.T(Loc.DeleteModeOn), UiRole.Ghost);
        UiKit.Anchor(_deleteButton.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(150f, UiTheme.ButtonHeight));
        _deleteButton.Control.onClick.AddListener(ToggleDeleteMode);
    }

    private RowView CreateMainRow(string key)
    {
        var content = _list!.Content;
        return _mainKind == RowKind.Players
            ? PlayerRow.Create(content, key, this)
            : RoomRow.Create(content, key, this);
    }

    private RowKind CurrentKind()
    {
        if (_viewingLobbyId == null)
            return RowKind.Rooms;
        return RowKind.Players;
    }

    private void RefreshAll()
    {
        if (_page == null || _list == null)
            return;

        _context = new RowContext(
            canJoin: HistoryUi.IsTitleScene || !HistoryUi.IsInLiveLobby,
            canInvite: HistoryUi.CanShowInvite);

        var rooms = Plugin.Store.GetRooms();
        var friends = FriendRoomsService.GetFriends();
        _playerTotal = AllMetPlayers().Count;

        RefreshChrome(rooms.Count, friends.Count);
        RefreshMainList(rooms);
        RefreshRail(friends);
        WireNavigation();
        RefreshInitialSelection();
    }

    private void RefreshChrome(int roomCount, int friendCount)
    {
        bool isSearch = _viewingLobbyId == SearchViewId;
        bool isPlayers = _viewingLobbyId != null && !isSearch;
        bool roomsRoot = _viewingLobbyId == null;

        _titleText?.SetText(Loc.T(Loc.PageTitle));
        if (_subtitleText != null)
        {
            if (isSearch)
            {
                _subtitleText.text = Loc.Tf(Loc.SearchQuery, _searchQuery ?? "");
            }
            else if (_viewingLobbyId == PlayersViewId)
            {
                _subtitleText.text = Loc.T(Loc.PlayersTitle);
            }
            else if (isPlayers)
            {
                var room = Plugin.Store.GetRoom(_viewingLobbyId!);
                string host = room == null || string.IsNullOrWhiteSpace(room.HostName)
                    ? Loc.T(Loc.UnknownHost)
                    : room.HostName;
                _subtitleText.text = Loc.Tf(Loc.RoomTitle, host)
                                     + "  ·  "
                                     + Loc.Tf(Loc.PlayerCount, room?.Players.Count ?? 0);
            }
            else
            {
                _subtitleText.text = Loc.T(Loc.Subtitle);
            }
        }

        _statChip?.SetText(Loc.Tf(Loc.StatPlayers, _playerTotal), ChipKind.Mute);

        if (_tabRooms != null)
        {
            _tabRooms.SetMinWidth(UiTheme.TabMinWidth);
            _tabRooms.SetLabel(Loc.Tf(Loc.TabRoomsCount, roomCount));
            _tabRooms.SetTabActive(roomsRoot);
        }

        if (_tabPlayers != null)
        {
            _tabPlayers.SetMinWidth(UiTheme.TabMinWidth);
            _tabPlayers.SetLabel(Loc.T(Loc.TabPlayers));
            _tabPlayers.Rect.anchoredPosition = new Vector2(UiTheme.Pad + _tabRooms!.Width + UiTheme.TabGap, -UiTheme.TabsTopPad);
            _tabPlayers.SetTabActive(!roomsRoot);
        }

        // the tabs row stops at the rail, so the search cluster never overlaps it (audit-ui UI-04)
        float railInset = UiTheme.Pad + UiTheme.RailWidth + RailGap;
        float cardWidth = _card != null ? _card.rect.width : UiTheme.CardMaxWidth;
        float mainWidth = cardWidth - UiTheme.Pad * 2f - UiTheme.RailWidth - RailGap;
        float tabsWidth = (_tabRooms?.Width ?? 0f) + UiTheme.TabGap + (_tabPlayers?.Width ?? 0f);
        float searchWidth = _searchButton?.Width ?? 0f;
        float clearWidth = isSearch && _clearButton != null ? _clearButton.Width + 10f : 0f;
        float available = mainWidth - tabsWidth - 24f - searchWidth - 10f;
        // on narrow windows the clear button gives up its slot before the field does
        bool clearFits = available - clearWidth >= 120f;
        float fieldWidth = Mathf.Clamp(available - (clearFits ? clearWidth : 0f), 120f, UiTheme.SearchWidth);

        if (_searchButton != null)
        {
            _searchButton.SetLabel(Loc.T(Loc.Search));
            _searchButton.Rect.anchoredPosition = new Vector2(-railInset, -(UiTheme.TabsTopPad + 1f));
        }

        if (_searchInput != null)
        {
            _searchInput.SetVisible(true);
            _searchInput.SetPlaceholder(Loc.T(Loc.SearchPlaceholder));
            _searchInput.Rect.sizeDelta = new Vector2(fieldWidth, UiTheme.SearchHeight);
            _searchInput.Rect.anchoredPosition = new Vector2(-(railInset + searchWidth + 10f), -UiTheme.TabsTopPad);
        }

        if (_clearButton != null)
        {
            _clearButton.SetLabel(Loc.T(Loc.SearchClear));
            _clearButton.SetVisible(isSearch && clearFits);
            _clearButton.Rect.anchoredPosition = new Vector2(-(railInset + searchWidth + 10f + fieldWidth + 10f), -UiTheme.TabsTopPad);
        }

        // close first: the delete button is placed from its width
        if (_closeButton != null)
        {
            _closeButton.SetLabel(Loc.T(Loc.Close));
            _closeButton.Rect.anchoredPosition = new Vector2(-UiTheme.Pad, 0f);
        }

        if (_deleteButton != null)
        {
            _deleteButton.SetLabel(Loc.T(_deleteMode ? Loc.DeleteModeOff : Loc.DeleteModeOn));
            _deleteButton.SetRole(_deleteMode ? UiRole.Danger : UiRole.Ghost);
            _deleteButton.SetVisible(roomsRoot);
            _deleteButton.Rect.anchoredPosition = new Vector2(-(UiTheme.Pad + (_closeButton?.Width ?? 0f) + UiTheme.ButtonGap), 0f);
        }

        if (_hintText != null)
        {
            if (_deleteMode)
            {
                _hintText.text = Loc.T(Loc.HintEscDelete) + "  ·  " + Loc.T(Loc.DeleteConfirm);
            }
            else
            {
                string hint = Loc.T(Loc.HintEsc);
                if (roomsRoot)
                    hint += "  ·  " + Loc.T(Loc.HintDoubleClick);
                hint += "  ·  " + Loc.Tf(Loc.HintHotkeyShort, Plugin.ModConfig.ModMenuHotkey.Value);
                _hintText.text = hint;
            }

            float reserved = UiTheme.Pad + (_closeButton?.Width ?? 0f) + 24f;
            if (roomsRoot && _deleteButton != null)
                reserved += _deleteButton.Width + UiTheme.ButtonGap;
            _hintText.rectTransform.offsetMax = new Vector2(-reserved, 14f);
        }

        bool showBanner = _deleteMode && roomsRoot;
        if (_banner != null)
        {
            _banner.gameObject.SetActive(showBanner);
            if (_bannerText != null)
                _bannerText.text = Loc.T(Loc.DeleteBanner);
            _listRoot!.offsetMax = new Vector2(0f, showBanner ? -(UiTheme.BannerHeight + UiTheme.RowGap) : 0f);
        }

        if (_railTitle != null)
            _railTitle.text = Loc.Tf(Loc.RailTitle, friendCount);
        if (_railHint != null)
            _railHint.text = Loc.T(Loc.RailHint);
    }

    private void RefreshMainList(IReadOnlyList<RoomRecord>? rooms = null)
    {
        if (_mainPool == null || _list == null)
            return;

        var kind = CurrentKind();
        if (kind != _mainKind)
        {
            _mainPool.Clear();
            _mainKind = kind;
        }

        var ordered = new List<RowView>();
        switch (kind)
        {
            case RowKind.Rooms:
                rooms ??= Plugin.Store.GetRooms();
                foreach (var room in rooms)
                {
                    var row = (RoomRow)_mainPool.Take(room.LobbyId);
                    row.Bind(room, _deleteMode, _context);
                    ordered.Add(row);
                }
                break;

            case RowKind.Players:
                foreach (var hit in CurrentPlayerHits())
                {
                    var row = (PlayerRow)_mainPool.Take(hit.SteamId);
                    row.Bind(hit, _context);
                    ordered.Add(row);
                }
                break;
        }

        _mainRows = _mainPool.Commit(ordered);
        _list.LayoutRows(Rects(_mainRows), UiTheme.RowHeight, UiTheme.RowGap);

        bool empty = _mainRows.Count == 0;
        _list.Root.gameObject.SetActive(!empty);
        if (_emptyRoot != null)
            _emptyRoot.gameObject.SetActive(empty);
        if (empty)
            SetEmptyState(kind);
    }

    private void RefreshRail(IReadOnlyList<FriendRoomInfo>? friends = null)
    {
        if (_railPool == null || _railList == null)
            return;

        friends ??= FriendRoomsService.GetFriends();

        var ordered = new List<RowView>();
        foreach (var friend in friends)
        {
            var row = (FriendRow)_railPool.Take(friend.SteamId.ToString());
            row.Bind(friend, _context);
            ordered.Add(row);
        }

        _railRows = _railPool.Commit(ordered);
        _railList.LayoutRows(Rects(_railRows), UiTheme.RowHeight, UiTheme.RowGap);
        _railList.Root.gameObject.SetActive(_railRows.Count > 0);
        if (_railEmpty != null)
        {
            _railEmpty.text = Loc.T(Loc.FriendsEmpty);
            _railEmpty.gameObject.SetActive(_railRows.Count == 0);
        }
    }

    private List<PlayerSearchHit> CurrentPlayerHits()
    {
        if (_viewingLobbyId == SearchViewId)
            return Plugin.Store.SearchPlayers(_searchQuery ?? "").ToList();

        if (_viewingLobbyId == PlayersViewId)
            return AllMetPlayers();

        var room = _viewingLobbyId == null ? null : Plugin.Store.GetRoom(_viewingLobbyId);
        if (room == null)
            return new List<PlayerSearchHit>();

        return room.Players
            .OrderByDescending(p => p.WasHost)
            .ThenByDescending(p => p.LastSeenUtcTicks)
            .Select(p => new PlayerSearchHit
            {
                SteamId = p.SteamId,
                DisplayName = p.DisplayName,
                WasHost = p.WasHost,
                LastSeenUtcTicks = p.LastSeenUtcTicks,
                LobbyId = room.LobbyId,
                RoomHostName = room.HostName,
            })
            .ToList();
    }

    /// <summary>Flat "everyone you met" list, deduplicated per SteamId by the newest sighting.</summary>
    private List<PlayerSearchHit> AllMetPlayers()
    {
        var map = new Dictionary<string, PlayerSearchHit>(StringComparer.Ordinal);
        foreach (var room in Plugin.Store.GetRooms())
        {
            foreach (var player in room.Players)
            {
                if (string.IsNullOrWhiteSpace(player.SteamId))
                    continue;
                if (map.TryGetValue(player.SteamId, out var existing) && existing.LastSeenUtcTicks >= player.LastSeenUtcTicks)
                    continue;
                map[player.SteamId] = new PlayerSearchHit
                {
                    SteamId = player.SteamId,
                    DisplayName = player.DisplayName,
                    WasHost = player.WasHost,
                    LastSeenUtcTicks = player.LastSeenUtcTicks,
                    LobbyId = room.LobbyId,
                    RoomHostName = room.HostName,
                };
            }
        }

        return map.Values.OrderByDescending(h => h.LastSeenUtcTicks).ToList();
    }

    private void SetEmptyState(RowKind kind)
    {
        if (_emptyTitle == null || _emptyHint == null)
            return;

        switch (kind)
        {
            case RowKind.Players when _viewingLobbyId == SearchViewId:
                _emptyTitle.text = Loc.T(Loc.SearchEmpty);
                _emptyHint.text = Loc.T(Loc.SearchEmptyHint);
                break;
            case RowKind.Players when _viewingLobbyId == PlayersViewId:
                _emptyTitle.text = Loc.T(Loc.EmptyAllPlayers);
                _emptyHint.text = Loc.T(Loc.EmptyPlayersHint);
                break;
            case RowKind.Players:
                _emptyTitle.text = Loc.T(Loc.EmptyPlayers);
                _emptyHint.text = Loc.T(Loc.EmptyPlayersHint);
                break;
            default:
                _emptyTitle.text = Loc.T(Loc.EmptyRoomsTitle);
                _emptyHint.text = Loc.T(Loc.EmptyRoomsHint);
                break;
        }
    }

    /// <summary>Explicit up/down/left/right graph; automatic navigation cannot see pooled rows.</summary>
    private void WireNavigation()
    {
        var top = new List<Selectable>();
        if (_tabRooms != null)
            top.Add(_tabRooms.Control);
        if (_tabPlayers != null)
            top.Add(_tabPlayers.Control);
        if (_searchInput != null)
            top.Add(_searchInput.Field);
        if (_searchButton != null)
            top.Add(_searchButton.Control);
        if (_clearButton != null && _clearButton.GameObject.activeSelf)
            top.Add(_clearButton.Control);

        var rows = new List<List<Selectable>>();
        foreach (var row in _mainRows)
        {
            var actions = row.ActionSelectables();
            if (actions.Count > 0)
                rows.Add(actions);
        }

        var foot = new List<Selectable>();
        if (_deleteButton != null && _deleteButton.GameObject.activeSelf)
            foot.Add(_deleteButton.Control);
        if (_closeButton != null)
            foot.Add(_closeButton.Control);

        UiNav.Row(top);
        foreach (var row in rows)
            UiNav.Row(row);
        UiNav.Row(foot);

        for (int i = 0; i < rows.Count; i++)
        {
            Selectable? up = i == 0 ? (top.Count > 0 ? top[0] : null) : rows[i - 1][0];
            Selectable? down = i == rows.Count - 1 ? (foot.Count > 0 ? foot[0] : null) : rows[i + 1][0];
            foreach (var selectable in rows[i])
            {
                var navigation = selectable.navigation;
                UiNav.Link(selectable, up, down, navigation.selectOnLeft, navigation.selectOnRight);
            }
        }

        Selectable? firstRow = rows.Count > 0 ? rows[0][0] : null;
        Selectable? lastRow = rows.Count > 0 ? rows[rows.Count - 1][0] : null;

        foreach (var selectable in top)
        {
            var navigation = selectable.navigation;
            Selectable? down = firstRow ?? (foot.Count > 0 ? foot[0] : null);
            UiNav.Link(selectable, null, down, navigation.selectOnLeft, navigation.selectOnRight);
        }

        foreach (var selectable in foot)
        {
            var navigation = selectable.navigation;
            Selectable? up = lastRow ?? (top.Count > 0 ? top[0] : null);
            UiNav.Link(selectable, up, null, navigation.selectOnLeft, navigation.selectOnRight);
        }

        var rail = new List<Selectable>();
        foreach (var row in _railRows)
        {
            var selectable = row.FirstSelectable();
            if (selectable != null)
                rail.Add(selectable);
        }

        UiNav.Column(rail);

        for (int i = 0; i < rail.Count; i++)
        {
            List<Selectable>? target = rows.Count == 0 ? null : rows[Math.Min(i, rows.Count - 1)];
            Selectable? left = target == null ? null : target[target.Count - 1];
            var navigation = rail[i].navigation;
            UiNav.Link(rail[i], navigation.selectOnUp, navigation.selectOnDown, left, navigation.selectOnRight);
        }

        for (int i = 0; i < rows.Count && rail.Count > 0; i++)
        {
            Selectable right = rail[Math.Min(i, rail.Count - 1)];
            var selectable = rows[i][rows[i].Count - 1];
            var navigation = selectable.navigation;
            UiNav.Link(selectable, navigation.selectOnUp, navigation.selectOnDown, navigation.selectOnLeft, right);
        }
    }

    private Selectable? FirstInteractive()
    {
        foreach (var row in _mainRows)
        {
            var selectable = row.FirstSelectable();
            if (selectable != null)
                return selectable;
        }

        return _closeButton?.Control;
    }

    /// <summary>Hands the EventSystem selection back so the game's hotkeys keep working.</summary>
    private void ReleaseFocus()
    {
        try
        {
            var events = EventSystem.current;
            if (events == null)
                return;
            var selected = events.currentSelectedGameObject;
            if (selected == null)
                return;
            if (IsInside(selected.transform, _card))
                events.SetSelectedGameObject(null);
        }
        catch
        {
            // selection can be torn down mid-frame
        }
    }

    private void FocusInitial()
    {
        if (_page == null)
            return;

        var first = FirstInteractive();
        _page.SetInitialSelection(first);
        if (first == null)
            return;

        try
        {
            // the game only honours this while the active scheme is gamepad, which is exactly right
            UIInputHandler.SetSelectedObject(first.gameObject);
        }
        catch
        {
            // UIInputHandler can be missing on the title scene
        }
    }

    /// <summary>
    /// Keeps a gamepad selection alive across a refresh. A refresh can destroy the row that was
    /// selected (deleting the first room, or a friend dropping out of a game), and the engine has
    /// no fallback: it only re-selects when nothing is selected and the scheme is gamepad.
    /// </summary>
    private void RefreshInitialSelection()
    {
        if (_page == null || !IsPageOpen)
            return;

        var first = FirstInteractive();
        _page.SetInitialSelection(first);

        var events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null
            && events.currentSelectedGameObject.activeInHierarchy)
            return;
        if (first == null)
            return;

        try
        {
            UIInputHandler.SetSelectedObject(first.gameObject);
        }
        catch
        {
            // UIInputHandler can be missing on the title scene
        }
    }

    private void OnPageResized()
    {
        if (!IsPageOpen)
            return;
        try
        {
            RefreshChrome(Plugin.Store.GetRooms().Count, FriendRoomsService.GetFriends().Count);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"OnPageResized: {ex.Message}");
        }
    }

    private void OnGameLanguageChanged()
    {
        try
        {
            if (IsPageOpen)
                RefreshAll();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"OnGameLanguageChanged: {ex.Message}");
        }
    }

    private void OnJoinGateChanged()
    {
        if (!IsPageOpen)
            return;
        try
        {
            RefreshMainList();
            WireNavigation();
            RefreshInitialSelection();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"OnJoinGateChanged: {ex.Message}");
        }
    }

    private void OnFriendsChanged()
    {
        if (!IsPageOpen)
            return;
        try
        {
            var friends = FriendRoomsService.GetFriends();
            if (_railTitle != null)
                _railTitle.text = Loc.Tf(Loc.RailTitle, friends.Count);
            _statChip?.SetText(Loc.Tf(Loc.StatPlayers, _playerTotal), ChipKind.Mute);
            RefreshRail(friends);
            WireNavigation();
            RefreshInitialSelection();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"OnFriendsChanged: {ex.Message}");
        }
    }

    private void ToggleDeleteMode()
    {
        if (_viewingLobbyId != null)
            return;
        _deleteMode = !_deleteMode;
        RefreshAll();
    }

    private void ShowRoomsView()
    {
        _viewingLobbyId = null;
        _searchQuery = null;
        _deleteMode = false;
        if (_searchInput != null)
            _searchInput.Text = "";
        RefreshAll();
        _list?.ResetScroll();
    }

    private void ShowAllPlayersView()
    {
        _viewingLobbyId = PlayersViewId;
        _searchQuery = null;
        _deleteMode = false;
        RefreshAll();
        _list?.ResetScroll();
    }

    private void ShowPlayersView(string lobbyId)
    {
        _viewingLobbyId = lobbyId;
        _searchQuery = null;
        _deleteMode = false;
        RefreshAll();
        _list?.ResetScroll();
    }

    private void RunSearch()
    {
        string query = (_searchInput?.Text ?? "").Trim();
        if (query.Length == 0)
        {
            ShowAllPlayersView();
            return;
        }

        _searchQuery = query;
        _viewingLobbyId = SearchViewId;
        _deleteMode = false;
        RefreshAll();
        _list?.ResetScroll();
    }

    private static List<RectTransform> Rects(IReadOnlyList<RowView> rows)
    {
        var rects = new List<RectTransform>(rows.Count);
        foreach (var row in rows)
            rects.Add(row.Root);
        return rects;
    }

    private static void AnchorTop(RectTransform rect, float height, float topOffset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, -topOffset);
    }

    private bool OpenMenuWindow()
    {
        if (!HasLivePage || MenuWindowOpen == null)
            return false;
        try
        {
            MenuWindowOpen.Invoke((MenuWindow)_page!, null);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"MenuWindow.Open invoke failed: {ex.Message}");
            return false;
        }
    }

    private void CloseMenuWindow()
    {
        if (!HasLivePage)
            return;
        try
        {
            if (MenuWindowClose != null)
                MenuWindowClose.Invoke((MenuWindow)_page!, null);
            else
                ((MenuWindow)_page!).Hide();
        }
        catch
        {
            try { ((MenuWindow)_page!).Hide(); } catch { /* ignore */ }
        }
    }

    public void OnRoomClicked(RoomRecord room)
    {
        float now = Time.unscaledTime;
        bool isDouble =
            _lastClickLobbyId != null
            && string.Equals(_lastClickLobbyId, room.LobbyId, StringComparison.Ordinal)
            && (now - _lastClickTime) <= DoubleClickSeconds;

        _lastClickLobbyId = room.LobbyId;
        _lastClickTime = now;

        if (!isDouble)
            return;

        if (!HistoryUi.IsTitleScene && HistoryUi.IsInLiveLobby)
        {
            Plugin.Log.LogDebug("Double-click join ignored: already in a live lobby.");
            return;
        }

        _lastClickLobbyId = null;
        RoomJoiner.TryJoin(room, hidePage: true);
    }

    public void OnRoomJoin(RoomRecord room)
    {
        RoomJoiner.TryJoin(room, hidePage: true);
    }

    public void OnRoomView(RoomRecord room)
    {
        ShowPlayersView(room.LobbyId);
    }

    public void OnRoomDelete(RoomRecord room)
    {
        Plugin.Store.DeleteRoom(room.LobbyId);
        RefreshAll();
    }

    public void OnPlayerInvite(string steamId)
    {
        if (!_context.CanInvite)
        {
            Plugin.Log.LogDebug("Invite ignored: not in a live lobby.");
            return;
        }

        if (!SteamInviteService.InvitePlayer(steamId))
            return;

        RefreshRail();
        WireNavigation();
        RefreshInitialSelection();
    }

    public void OnPlayerAddFriend(string steamId)
    {
        SteamAvatarService.OpenAddFriend(steamId);
    }

    public void OnFriendJoin(FriendRoomInfo friend)
    {
        RoomJoiner.TryJoinLobbyId(friend.LobbyId.ToString(), hidePage: true);
    }

    public void OnFriendInvite(ulong steamId)
    {
        OnPlayerInvite(steamId.ToString());
    }
}
