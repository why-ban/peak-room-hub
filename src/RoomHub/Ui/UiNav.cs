using System.Collections.Generic;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>
/// Explicit navigation wiring. Automatic navigation cannot reach pooled rows, and the panel must be
/// fully operable with a controller (audit-ui UI-01).
/// </summary>
internal static class UiNav
{
    public static void Link(Selectable? self, Selectable? up, Selectable? down, Selectable? left, Selectable? right)
    {
        if (self == null)
            return;

        var navigation = self.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.selectOnUp = up;
        navigation.selectOnDown = down;
        navigation.selectOnLeft = left;
        navigation.selectOnRight = right;
        self.navigation = navigation;
    }

    public static void Column(IReadOnlyList<Selectable> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var self = items[i];
            var left = self.navigation.selectOnLeft;
            var right = self.navigation.selectOnRight;
            Link(self, i > 0 ? items[i - 1] : null, i + 1 < items.Count ? items[i + 1] : null, left, right);
        }
    }

    public static void Row(IReadOnlyList<Selectable> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var self = items[i];
            var up = self.navigation.selectOnUp;
            var down = self.navigation.selectOnDown;
            Link(self, up, down, i > 0 ? items[i - 1] : null, i + 1 < items.Count ? items[i + 1] : null);
        }
    }
}
