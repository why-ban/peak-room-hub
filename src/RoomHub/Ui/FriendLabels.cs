namespace RoomHub.Ui;

/// <summary>
/// Friend status text. Mapping table: docs/audit/friend-modes.md §3. The area only decorates the
/// text, and button availability is decided by FriendRoomInfo.IsJoinable / IsInvitable alone.
/// </summary>
internal static class FriendLabels
{
    public static string Status(FriendGameMode mode, FriendArea area)
    {
        string text = Mode(mode);
        string? detail = Area(area, mode);
        return detail == null ? text : Loc.Tf(Loc.FriendStatusArea, text, detail);
    }

    public static string Mode(FriendGameMode mode) => mode switch
    {
        FriendGameMode.Joinable => Loc.T(Loc.FriendInGame),
        FriendGameMode.InviteOnly => Loc.T(Loc.FriendInviteOnly),
        FriendGameMode.Solo => Loc.T(Loc.FriendSolo),
        FriendGameMode.MainMenu => Loc.T(Loc.FriendMainMenu),
        FriendGameMode.Airport => Loc.T(Loc.FriendAirport),
        _ => Loc.T(Loc.FriendUnknown),
    };

    private static string? Area(FriendArea area, FriendGameMode mode)
    {
        // main menu is already the whole status, and "at the airport" would be printed twice
        if (area == FriendArea.MainMenu)
            return null;
        if (area == FriendArea.Airport && mode == FriendGameMode.Airport)
            return null;

        return area switch
        {
            FriendArea.Airport => Loc.T(Loc.AreaAirport),
            FriendArea.Shore => Loc.T(Loc.AreaShore),
            FriendArea.Tropics => Loc.T(Loc.AreaTropics),
            FriendArea.Alpine => Loc.T(Loc.AreaAlpine),
            FriendArea.Mesa => Loc.T(Loc.AreaMesa),
            FriendArea.Caldera => Loc.T(Loc.AreaCaldera),
            FriendArea.Kiln => Loc.T(Loc.AreaKiln),
            FriendArea.Peak => Loc.T(Loc.AreaPeak),
            FriendArea.Roots => Loc.T(Loc.AreaRoots),
            FriendArea.Gloom => Loc.T(Loc.AreaGloom),
            FriendArea.Citadel => Loc.T(Loc.AreaCitadel),
            FriendArea.Nadir => Loc.T(Loc.AreaNadir),
            _ => null,
        };
    }
}
