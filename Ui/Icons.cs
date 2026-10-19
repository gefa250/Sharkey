using System.Windows.Media;

namespace GlobalTranslator
{
    // One 16px stroke icon set for the whole app. Keep every glyph on the same
    // 16x16 grid with a 1.6 stroke so icons read as a family; header buttons,
    // list rows and status lines all pull from here instead of emoji or one-off
    // path strings.
    internal static class Icons
    {
        internal const double Size = 16;
        internal const double Stroke = 1.6;

        internal static readonly Geometry Search = Geometry.Parse(
            "M 11.2,11.2 L 14.6,14.6 M 2,6.9 A 4.9,4.9 0 1 1 11.8,6.9 A 4.9,4.9 0 1 1 2,6.9");
        internal static readonly Geometry Send = Geometry.Parse(
            "M 2,8 L 14,8 M 8.6,2.6 L 14,8 L 8.6,13.4");
        internal static readonly Geometry Copy = Geometry.Parse(
            "M 5.6,1.6 L 13.6,1.6 L 13.6,9.6 M 2.4,5.4 L 10.4,5.4 L 10.4,13.4 L 2.4,13.4 Z");
        internal static readonly Geometry Edit = Geometry.Parse(
            "M 2.4,13.6 L 2.4,11 L 11,2.4 L 13.6,5 L 5,13.6 Z M 9.6,3.8 L 12.2,6.4");
        internal static readonly Geometry Pin = Geometry.Parse(
            "M 5,2 L 11,2 L 10,7 L 13,10 L 9,10 L 9,14 L 7,14 L 7,10 L 3,10 L 6,7 Z");
        internal static readonly Geometry Expand = Geometry.Parse(
            "M 1,5 L 1,1 L 5,1 M 11,1 L 15,1 L 15,5 M 15,11 L 15,15 L 11,15 M 5,15 L 1,15 L 1,11");
        internal static readonly Geometry Restore = Geometry.Parse(
            "M 4.5,1.5 L 14.5,1.5 L 14.5,11.5 L 11.5,11.5 M 1.5,4.5 L 11.5,4.5 L 11.5,14.5 L 1.5,14.5 Z");
        internal static readonly Geometry Close = Geometry.Parse(
            "M 3.2,3.2 L 12.8,12.8 M 12.8,3.2 L 3.2,12.8");
        internal static readonly Geometry ChevronDown = Geometry.Parse(
            "M 3.4,6 L 8,10.6 L 12.6,6");
        internal static readonly Geometry Plus = Geometry.Parse(
            "M 8,2.6 L 8,13.4 M 2.6,8 L 13.4,8");
        internal static readonly Geometry More = Geometry.Parse(
            "M 1.9,8 A 1.1,1.1 0 1 1 4.1,8 A 1.1,1.1 0 1 1 1.9,8 " +
            "M 6.9,8 A 1.1,1.1 0 1 1 9.1,8 A 1.1,1.1 0 1 1 6.9,8 " +
            "M 11.9,8 A 1.1,1.1 0 1 1 14.1,8 A 1.1,1.1 0 1 1 11.9,8");
        internal static readonly Geometry Sidebar = Geometry.Parse(
            "M 1.6,2.6 L 14.4,2.6 L 14.4,13.4 L 1.6,13.4 Z M 6,2.6 L 6,13.4");
        internal static readonly Geometry Trash = Geometry.Parse(
            "M 2.6,4.4 L 13.4,4.4 M 6,4.4 L 6,2.4 L 10,2.4 L 10,4.4 " +
            "M 4.1,4.4 L 4.9,13.6 L 11.1,13.6 L 11.9,4.4 M 6.6,7 L 6.6,11 M 9.4,7 L 9.4,11");
        internal static readonly Geometry Image = Geometry.Parse(
            "M 1.6,2.6 L 14.4,2.6 L 14.4,13.4 L 1.6,13.4 Z " +
            "M 4.4,6.2 A 1.2,1.2 0 1 1 6.8,6.2 A 1.2,1.2 0 1 1 4.4,6.2 " +
            "M 2.2,12.4 L 6.6,8 L 9.2,10.6 L 11.2,8.6 L 14,11.4");
        internal static readonly Geometry Camera = Geometry.Parse(
            "M 1.6,5.4 L 4.6,5.4 L 6.1,2.8 L 9.9,2.8 L 11.4,5.4 L 14.4,5.4 L 14.4,13 L 1.6,13 Z " +
            "M 5.4,9 A 2.6,2.6 0 1 1 10.6,9 A 2.6,2.6 0 1 1 5.4,9");
        internal static readonly Geometry Table = Geometry.Parse(
            "M 1.6,2.6 L 14.4,2.6 L 14.4,13.4 L 1.6,13.4 Z " +
            "M 1.6,6.2 L 14.4,6.2 M 1.6,9.8 L 14.4,9.8 M 6,2.6 L 6,13.4");
        internal static readonly Geometry Refresh = Geometry.Parse(
            "M 13.4,8 A 5.4,5.4 0 1 1 11.6,4.2 M 13.8,1.6 L 13.8,4.8 L 10.6,4.8");
        internal static readonly Geometry Download = Geometry.Parse(
            "M 8,2 L 8,10 M 4.6,6.8 L 8,10.2 L 11.4,6.8 M 2.6,13.4 L 13.4,13.4");
        internal static readonly Geometry Settings = Geometry.Parse(
            "M 2,4.6 L 14,4.6 M 2,11.4 L 14,11.4 M 5.6,2.4 L 5.6,6.8 M 10.4,9.2 L 10.4,13.6");
        internal static readonly Geometry Sparkle = Geometry.Parse(
            "M 8,1.4 L 9.7,6.3 L 14.6,8 L 9.7,9.7 L 8,14.6 L 6.3,9.7 L 1.4,8 L 6.3,6.3 Z");
        internal static readonly Geometry Check = Geometry.Parse(
            "M 2.6,8.6 L 6.4,12.4 L 13.4,3.6");
        internal static readonly Geometry Alert = Geometry.Parse(
            "M 8,2 L 14.5,13.4 L 1.5,13.4 Z M 8,6.2 L 8,9.6 M 8,11.4 L 8,12");
        internal static readonly Geometry Info = Geometry.Parse(
            "M 2.2,8 A 5.8,5.8 0 1 1 13.8,8 A 5.8,5.8 0 1 1 2.2,8 M 8,7.2 L 8,11.2 M 8,4.8 L 8,5.2");
        internal static readonly Geometry External = Geometry.Parse(
            "M 9,2.6 L 13.4,2.6 L 13.4,7 M 13.4,2.6 L 7.4,8.6 M 11,10.4 L 11,13.4 L 2.6,13.4 L 2.6,5 L 5.6,5");
    }
}
