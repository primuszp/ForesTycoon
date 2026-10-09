using System;
using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    internal enum GameIcon
    {
        GameMenu, Save, Load, Exit, NewMap,
        Pause, Play, Fast, Faster,
        Inspect, Raise, Lower, Road, RoadRemove, Plant, Harvest, Sawmill, Truck, TruckAdd,
        Vehicles, Forestry, Environment, Graphics, Developer, Help, Camera, Deer,
        Spruce, Birch, Oak, Beech,
        Sun, Cloud, Rain, Storm, Thermometer, Calendar, Timber, Lightning, Speedometer,
        RoadRepair, Macadam, SkidTrail, SkidTrailRemove, Depot, Forwarder, TimberRemove
    }

    /// <summary>
    /// Shared game icon entry point. The ImageGen transport artwork supplies all 46 icons;
    /// the original vector definitions remain available to renderers without an atlas.
    /// Each symbol keeps its semantic colours and scales inside the requested square.
    /// </summary>
    internal static class GameIcons
    {
        internal static readonly uint Leaf = Rgb(118, 176, 72);
        internal static readonly uint DarkLeaf = Rgb(62, 118, 58);
        internal static readonly uint Wood = Rgb(170, 118, 66);
        internal static readonly uint DarkWood = Rgb(110, 74, 44);
        internal static readonly uint Water = Rgb(92, 160, 220);
        internal static readonly uint Warning = Rgb(232, 88, 70);
        internal static readonly uint Amber = Rgb(240, 186, 78);
        internal static readonly uint Asphalt = Rgb(86, 90, 96);
        internal static readonly uint Earth = Rgb(150, 122, 82);
        internal static readonly uint Sky = Rgb(150, 196, 236);
        internal static readonly uint TruckBlue = Rgb(122, 182, 228);

        internal static uint Rgb(int r, int g, int b, int a = 255) =>
            (uint)(r | (g << 8) | (b << 16) | (a << 24));

        internal static uint Color(NVec4 color) => ImGui.ColorConvertFloat4ToU32(color);

        /// <summary>Draws an icon inside the square at <paramref name="origin"/> with side <paramref name="size"/>.</summary>
        internal static void Draw(ImDrawListPtr dl, GameIcon icon, NVec2 origin, float size, uint ink)
        {
            if (GameIconAtlas.Draw(dl, icon, origin, size, ink)) return;
            if (icon == GameIcon.TimberRemove)
            {
                Draw(dl, GameIcon.Timber, origin, size, ink);
                DrawRemovalBadge(dl, origin, size, ink);
                return;
            }
            var c = new Canvas(dl, origin, size, ink);
            switch (icon)
            {
                case GameIcon.GameMenu:
                    for (int i = 0; i < 3; i++) c.Line(0.2f, 0.3f + i * 0.2f, 0.8f, 0.3f + i * 0.2f, ink, 1.25f);
                    break;
                case GameIcon.Save:
                    c.RectFilled(0.18f, 0.16f, 0.82f, 0.84f, Rgb(70, 110, 150), 0.08f);
                    c.RectFilled(0.30f, 0.16f, 0.68f, 0.40f, Rgb(214, 222, 228));
                    c.RectFilled(0.56f, 0.20f, 0.64f, 0.36f, Rgb(70, 110, 150));
                    c.RectFilled(0.28f, 0.54f, 0.72f, 0.84f, Rgb(238, 236, 226));
                    c.Line(0.34f, 0.64f, 0.66f, 0.64f, Asphalt, 0.6f);
                    c.Line(0.34f, 0.74f, 0.60f, 0.74f, Asphalt, 0.6f);
                    break;
                case GameIcon.Load:
                    c.Poly(Amber, 0.14f, 0.30f, 0.40f, 0.30f, 0.46f, 0.38f, 0.86f, 0.38f, 0.86f, 0.80f, 0.14f, 0.80f);
                    c.Poly(Rgb(250, 210, 120), 0.14f, 0.80f, 0.24f, 0.48f, 0.94f, 0.48f, 0.86f, 0.80f);
                    break;
                case GameIcon.Exit:
                    c.Rect(0.20f, 0.16f, 0.58f, 0.84f, ink, 1f);
                    c.Line(0.46f, 0.50f, 0.88f, 0.50f, Warning, 1.2f);
                    c.Poly(Warning, 0.88f, 0.50f, 0.74f, 0.38f, 0.74f, 0.62f);
                    break;
                case GameIcon.NewMap:
                    c.Poly(Rgb(96, 150, 84), 0.10f, 0.62f, 0.50f, 0.42f, 0.90f, 0.62f, 0.50f, 0.82f);
                    c.Poly(Rgb(130, 180, 100), 0.10f, 0.52f, 0.50f, 0.32f, 0.90f, 0.52f, 0.50f, 0.72f);
                    c.Line(0.30f, 0.42f, 0.70f, 0.62f, Rgb(70, 110, 60), 0.6f);
                    c.Line(0.70f, 0.42f, 0.30f, 0.62f, Rgb(70, 110, 60), 0.6f);
                    c.Line(0.50f, 0.10f, 0.50f, 0.30f, Amber, 1f);
                    c.Line(0.40f, 0.20f, 0.60f, 0.20f, Amber, 1f);
                    break;

                case GameIcon.Pause:
                    c.RectFilled(0.28f, 0.22f, 0.43f, 0.78f, ink, 0.04f);
                    c.RectFilled(0.57f, 0.22f, 0.72f, 0.78f, ink, 0.04f);
                    break;
                case GameIcon.Play:
                    c.Poly(ink, 0.32f, 0.20f, 0.78f, 0.50f, 0.32f, 0.80f);
                    break;
                case GameIcon.Fast:
                    c.Poly(ink, 0.16f, 0.24f, 0.50f, 0.50f, 0.16f, 0.76f);
                    c.Poly(ink, 0.48f, 0.24f, 0.82f, 0.50f, 0.48f, 0.76f);
                    break;
                case GameIcon.Faster:
                    c.Poly(ink, 0.08f, 0.26f, 0.36f, 0.50f, 0.08f, 0.74f);
                    c.Poly(ink, 0.36f, 0.26f, 0.64f, 0.50f, 0.36f, 0.74f);
                    c.Poly(ink, 0.64f, 0.26f, 0.92f, 0.50f, 0.64f, 0.74f);
                    break;

                case GameIcon.Inspect:
                    c.CircleFilled(0.42f, 0.42f, 0.22f, Rgb(150, 196, 236, 90));
                    c.Circle(0.42f, 0.42f, 0.24f, ink, 1.2f);
                    c.Line(0.60f, 0.60f, 0.84f, 0.84f, Wood, 2f);
                    break;
                case GameIcon.Raise:
                    c.Poly(Earth, 0.08f, 0.84f, 0.42f, 0.42f, 0.58f, 0.56f, 0.92f, 0.84f);
                    c.Poly(Leaf, 0.08f, 0.84f, 0.26f, 0.62f, 0.74f, 0.62f, 0.92f, 0.84f);
                    Arrow(c, 0.72f, 0.46f, 0.72f, 0.10f, Amber);
                    break;
                case GameIcon.Lower:
                    c.Poly(Earth, 0.08f, 0.52f, 0.92f, 0.52f, 0.66f, 0.84f, 0.34f, 0.84f);
                    c.Poly(Leaf, 0.08f, 0.52f, 0.30f, 0.52f, 0.24f, 0.62f, 0.08f, 0.62f);
                    c.Poly(Leaf, 0.70f, 0.52f, 0.92f, 0.52f, 0.92f, 0.62f, 0.76f, 0.62f);
                    Arrow(c, 0.50f, 0.08f, 0.50f, 0.44f, Amber);
                    break;
                case GameIcon.Road:
                    c.Poly(Asphalt, 0.36f, 0.12f, 0.64f, 0.12f, 0.90f, 0.88f, 0.10f, 0.88f);
                    c.Line(0.36f, 0.12f, 0.10f, 0.88f, Rgb(200, 196, 180), 0.7f);
                    c.Line(0.64f, 0.12f, 0.90f, 0.88f, Rgb(200, 196, 180), 0.7f);
                    c.Line(0.50f, 0.18f, 0.50f, 0.30f, Amber, 0.9f);
                    c.Line(0.50f, 0.42f, 0.50f, 0.56f, Amber, 1f);
                    c.Line(0.50f, 0.68f, 0.50f, 0.84f, Amber, 1.1f);
                    break;
                case GameIcon.RoadRemove:
                    c.Poly(Asphalt, 0.36f, 0.12f, 0.64f, 0.12f, 0.90f, 0.88f, 0.10f, 0.88f);
                    c.Line(0.50f, 0.18f, 0.50f, 0.30f, Amber, 0.9f);
                    c.Line(0.50f, 0.68f, 0.50f, 0.84f, Amber, 1.1f);
                    c.Line(0.24f, 0.30f, 0.76f, 0.78f, Warning, 1.8f);
                    c.Line(0.76f, 0.30f, 0.24f, 0.78f, Warning, 1.8f);
                    break;
                case GameIcon.RoadRepair:
                    // A worn road with a shovel resting across it.
                    c.Poly(Asphalt, 0.36f, 0.12f, 0.64f, 0.12f, 0.90f, 0.88f, 0.10f, 0.88f);
                    c.EllipseFilled(0.40f, 0.62f, 0.08f, 0.04f, Rgb(54, 56, 60));
                    c.EllipseFilled(0.60f, 0.40f, 0.06f, 0.03f, Rgb(54, 56, 60));
                    c.Line(0.22f, 0.20f, 0.70f, 0.70f, Wood, 1.4f);
                    c.Poly(Rgb(196, 204, 212), 0.66f, 0.66f, 0.84f, 0.70f, 0.88f, 0.88f, 0.70f, 0.84f);
                    break;
                case GameIcon.Macadam:
                    c.Poly(Rgb(150, 141, 122), 0.36f, 0.12f, 0.64f, 0.12f, 0.90f, 0.88f, 0.10f, 0.88f);
                    c.EllipseFilled(0.42f, 0.30f, 0.04f, 0.03f, Rgb(110, 100, 86));
                    c.EllipseFilled(0.56f, 0.52f, 0.05f, 0.03f, Rgb(110, 100, 86));
                    c.EllipseFilled(0.36f, 0.72f, 0.05f, 0.03f, Rgb(110, 100, 86));
                    c.EllipseFilled(0.66f, 0.76f, 0.04f, 0.03f, Rgb(196, 188, 170));
                    break;
                case GameIcon.SkidTrail:
                case GameIcon.SkidTrailRemove:
                    // Two wheel ruts curving into the forest, a tree beside them.
                    c.Poly(Earth, 0.10f, 0.92f, 0.40f, 0.92f, 0.62f, 0.10f, 0.50f, 0.10f);
                    c.Line(0.18f, 0.90f, 0.52f, 0.12f, DarkWood, 1.1f);
                    c.Line(0.34f, 0.90f, 0.58f, 0.12f, DarkWood, 1.1f);
                    c.Poly(DarkLeaf, 0.80f, 0.20f, 0.66f, 0.56f, 0.94f, 0.56f);
                    c.Line(0.80f, 0.56f, 0.80f, 0.66f, Wood, 1.0f);
                    if (icon == GameIcon.SkidTrailRemove)
                    {
                        c.Line(0.18f, 0.30f, 0.62f, 0.78f, Warning, 1.8f);
                        c.Line(0.62f, 0.30f, 0.18f, 0.78f, Warning, 1.8f);
                    }
                    break;
                case GameIcon.Depot:
                    // A round-roofed machine hall with an open door.
                    c.Poly(Rgb(168, 172, 176), 0.08f, 0.86f, 0.08f, 0.54f, 0.24f, 0.30f, 0.50f, 0.22f, 0.76f, 0.30f, 0.92f, 0.54f, 0.92f, 0.86f);
                    c.RectFilled(0.36f, 0.56f, 0.64f, 0.86f, Rgb(54, 56, 60));
                    c.Line(0.08f, 0.86f, 0.92f, 0.86f, Earth, 1.2f);
                    break;
                case GameIcon.Forwarder:
                    // Articulated forwarder: cab, bunk with stakes and logs.
                    c.RectFilled(0.56f, 0.40f, 0.86f, 0.70f, Rgb(222, 170, 40));
                    c.RectFilled(0.10f, 0.58f, 0.54f, 0.68f, Rgb(80, 84, 90));
                    c.Line(0.14f, 0.30f, 0.14f, 0.60f, Rgb(80, 84, 90), 1.1f);
                    c.Line(0.48f, 0.30f, 0.48f, 0.60f, Rgb(80, 84, 90), 1.1f);
                    c.RectFilled(0.12f, 0.40f, 0.50f, 0.56f, Wood);
                    c.EllipseFilled(0.24f, 0.78f, 0.09f, 0.09f, Rgb(40, 40, 42));
                    c.EllipseFilled(0.42f, 0.78f, 0.09f, 0.09f, Rgb(40, 40, 42));
                    c.EllipseFilled(0.74f, 0.78f, 0.09f, 0.09f, Rgb(40, 40, 42));
                    break;
                case GameIcon.Plant:
                    c.Poly(Earth, 0.16f, 0.86f, 0.30f, 0.74f, 0.70f, 0.74f, 0.84f, 0.86f);
                    c.Line(0.50f, 0.78f, 0.50f, 0.36f, Rgb(120, 160, 70), 1.2f);
                    c.Poly(Leaf, 0.50f, 0.52f, 0.30f, 0.30f, 0.14f, 0.36f, 0.30f, 0.50f);
                    c.Poly(Leaf, 0.50f, 0.42f, 0.66f, 0.16f, 0.86f, 0.16f, 0.72f, 0.38f);
                    c.Poly(DarkLeaf, 0.50f, 0.42f, 0.72f, 0.38f, 0.86f, 0.16f);
                    break;
                case GameIcon.Harvest:
                    // Stump with growth rings and an axe bedded in it.
                    c.RectFilled(0.18f, 0.56f, 0.62f, 0.86f, DarkWood);
                    c.EllipseFilled(0.40f, 0.56f, 0.22f, 0.08f, Rgb(214, 176, 120));
                    c.Ellipse(0.40f, 0.56f, 0.12f, 0.04f, Wood, 0.6f);
                    c.Line(0.42f, 0.54f, 0.86f, 0.12f, Wood, 1.4f);
                    c.Poly(Rgb(196, 204, 212), 0.40f, 0.46f, 0.56f, 0.30f, 0.62f, 0.50f, 0.48f, 0.62f);
                    break;
                case GameIcon.Sawmill:
                    c.Poly(Rgb(150, 96, 58), 0.08f, 0.44f, 0.36f, 0.20f, 0.64f, 0.44f, 0.64f, 0.88f, 0.08f, 0.88f);
                    c.Poly(Rgb(110, 60, 40), 0.04f, 0.46f, 0.36f, 0.16f, 0.68f, 0.46f, 0.36f, 0.24f);
                    c.RectFilled(0.26f, 0.62f, 0.46f, 0.88f, DarkWood);
                    SawBlade(c, 0.74f, 0.66f, 0.20f, Rgb(206, 212, 218));
                    break;
                case GameIcon.Truck:
                case GameIcon.TruckAdd:
                    Truck(c);
                    if (icon == GameIcon.TruckAdd)
                    {
                        c.CircleFilled(0.80f, 0.24f, 0.18f, Leaf);
                        c.Line(0.80f, 0.14f, 0.80f, 0.34f, Rgb(255, 255, 255), 1.1f);
                        c.Line(0.70f, 0.24f, 0.90f, 0.24f, Rgb(255, 255, 255), 1.1f);
                    }
                    break;
                case GameIcon.Vehicles:
                    Truck(c, 0.18f);
                    c.Line(0.10f, 0.14f, 0.56f, 0.14f, ink, 0.9f);
                    c.Line(0.10f, 0.28f, 0.44f, 0.28f, ink, 0.9f);
                    break;
                case GameIcon.Forestry:
                    c.RectFilled(0.14f, 0.58f, 0.30f, 0.86f, DarkLeaf);
                    c.RectFilled(0.40f, 0.42f, 0.56f, 0.86f, Leaf);
                    c.RectFilled(0.66f, 0.22f, 0.82f, 0.86f, Rgb(160, 206, 96));
                    c.Line(0.08f, 0.88f, 0.92f, 0.88f, ink, 0.9f);
                    c.Poly(DarkLeaf, 0.74f, 0.04f, 0.62f, 0.22f, 0.86f, 0.22f);
                    break;
                case GameIcon.Environment:
                    // Water droplet over a soil line: the water-balance view.
                    c.Poly(Water, 0.50f, 0.10f, 0.72f, 0.46f, 0.28f, 0.46f);
                    c.CircleFilled(0.50f, 0.54f, 0.23f, Water);
                    c.CircleFilled(0.42f, 0.52f, 0.06f, Rgb(200, 228, 250));
                    c.Line(0.10f, 0.88f, 0.90f, 0.88f, Earth, 1.2f);
                    break;
                case GameIcon.Graphics:
                    // Framed landscape: picture / display settings.
                    c.RectFilled(0.10f, 0.18f, 0.90f, 0.82f, Sky, 0.05f);
                    c.Poly(DarkLeaf, 0.10f, 0.82f, 0.38f, 0.42f, 0.62f, 0.82f);
                    c.Poly(Leaf, 0.42f, 0.82f, 0.66f, 0.52f, 0.90f, 0.82f);
                    c.CircleFilled(0.72f, 0.34f, 0.08f, Amber);
                    c.Rect(0.10f, 0.18f, 0.90f, 0.82f, ink, 0.9f, 0.05f);
                    break;
                case GameIcon.Developer:
                    // Bug: the classic developer-tools glyph.
                    c.EllipseFilled(0.50f, 0.58f, 0.20f, 0.26f, Rgb(214, 120, 70));
                    c.CircleFilled(0.50f, 0.28f, 0.12f, Rgb(160, 84, 50));
                    c.Line(0.50f, 0.36f, 0.50f, 0.84f, Rgb(100, 50, 30), 0.7f);
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 0.46f + i * 0.14f;
                        c.Line(0.32f, y, 0.14f, y - 0.06f + i * 0.06f, ink, 0.8f);
                        c.Line(0.68f, y, 0.86f, y - 0.06f + i * 0.06f, ink, 0.8f);
                    }
                    c.Line(0.44f, 0.18f, 0.36f, 0.06f, ink, 0.7f);
                    c.Line(0.56f, 0.18f, 0.64f, 0.06f, ink, 0.7f);
                    break;
                case GameIcon.Help:
                    c.Circle(0.50f, 0.50f, 0.36f, ink, 1.1f);
                    c.Text("?", 0.50f, 0.50f, ink);
                    break;
                case GameIcon.Camera:
                    c.Circle(0.50f, 0.50f, 0.30f, ink, 1f);
                    c.CircleFilled(0.50f, 0.50f, 0.08f, Amber);
                    c.Line(0.50f, 0.06f, 0.50f, 0.26f, ink, 1f);
                    c.Line(0.50f, 0.74f, 0.50f, 0.94f, ink, 1f);
                    c.Line(0.06f, 0.50f, 0.26f, 0.50f, ink, 1f);
                    c.Line(0.74f, 0.50f, 0.94f, 0.50f, ink, 1f);
                    break;
                case GameIcon.Deer:
                    c.EllipseFilled(0.50f, 0.62f, 0.13f, 0.20f, Wood);
                    c.CircleFilled(0.50f, 0.82f, 0.08f, DarkWood);
                    c.Line(0.42f, 0.46f, 0.20f, 0.16f, Rgb(222, 206, 170), 1f);
                    c.Line(0.30f, 0.30f, 0.12f, 0.30f, Rgb(222, 206, 170), 0.9f);
                    c.Line(0.58f, 0.46f, 0.80f, 0.16f, Rgb(222, 206, 170), 1f);
                    c.Line(0.70f, 0.30f, 0.88f, 0.30f, Rgb(222, 206, 170), 0.9f);
                    break;

                case GameIcon.Spruce:
                    c.RectFilled(0.46f, 0.76f, 0.54f, 0.92f, DarkWood);
                    c.Poly(Rgb(48, 96, 52), 0.50f, 0.36f, 0.82f, 0.80f, 0.18f, 0.80f);
                    c.Poly(Rgb(58, 112, 58), 0.50f, 0.20f, 0.74f, 0.58f, 0.26f, 0.58f);
                    c.Poly(Rgb(72, 128, 64), 0.50f, 0.06f, 0.66f, 0.38f, 0.34f, 0.38f);
                    break;
                case GameIcon.Birch:
                    c.RectFilled(0.45f, 0.48f, 0.55f, 0.92f, Rgb(232, 232, 222));
                    c.Line(0.45f, 0.62f, 0.51f, 0.62f, Rgb(40, 40, 40), 0.7f);
                    c.Line(0.49f, 0.78f, 0.55f, 0.78f, Rgb(40, 40, 40), 0.7f);
                    c.EllipseFilled(0.50f, 0.34f, 0.24f, 0.28f, Rgb(150, 190, 80));
                    c.EllipseFilled(0.42f, 0.28f, 0.10f, 0.10f, Rgb(176, 210, 104));
                    break;
                case GameIcon.Oak:
                    c.RectFilled(0.43f, 0.56f, 0.57f, 0.92f, Rgb(110, 84, 54));
                    c.EllipseFilled(0.50f, 0.40f, 0.40f, 0.26f, Rgb(84, 122, 50));
                    c.EllipseFilled(0.32f, 0.44f, 0.18f, 0.16f, Rgb(74, 110, 44));
                    c.EllipseFilled(0.68f, 0.44f, 0.18f, 0.16f, Rgb(74, 110, 44));
                    c.EllipseFilled(0.44f, 0.30f, 0.14f, 0.10f, Rgb(108, 146, 66));
                    break;
                case GameIcon.Beech:
                    c.RectFilled(0.45f, 0.58f, 0.55f, 0.92f, Rgb(150, 140, 124));
                    c.EllipseFilled(0.50f, 0.36f, 0.26f, 0.32f, Rgb(100, 140, 56));
                    c.EllipseFilled(0.44f, 0.26f, 0.10f, 0.12f, Rgb(128, 168, 74));
                    break;

                case GameIcon.Sun:
                    SunGlyph(c, 0.50f, 0.50f, 0.20f);
                    break;
                case GameIcon.Cloud:
                    CloudGlyph(c, Rgb(206, 214, 222));
                    break;
                case GameIcon.Rain:
                    CloudGlyph(c, Rgb(170, 180, 192), -0.12f);
                    for (int i = 0; i < 3; i++) c.Line(0.32f + i * 0.18f, 0.66f, 0.26f + i * 0.18f, 0.86f, Water, 1f);
                    break;
                case GameIcon.Storm:
                    CloudGlyph(c, Rgb(120, 128, 140), -0.12f);
                    c.Poly(Amber, 0.54f, 0.52f, 0.36f, 0.76f, 0.50f, 0.76f, 0.42f, 0.96f, 0.66f, 0.68f, 0.52f, 0.68f);
                    break;
                case GameIcon.Lightning:
                    c.Poly(Amber, 0.60f, 0.06f, 0.24f, 0.56f, 0.48f, 0.56f, 0.36f, 0.94f, 0.78f, 0.40f, 0.54f, 0.40f);
                    break;
                case GameIcon.Thermometer:
                    c.RectFilled(0.42f, 0.10f, 0.58f, 0.70f, Rgb(236, 236, 236), 0.08f);
                    c.CircleFilled(0.50f, 0.76f, 0.14f, Warning);
                    c.RectFilled(0.46f, 0.36f, 0.54f, 0.72f, Warning);
                    break;
                case GameIcon.Calendar:
                    c.RectFilled(0.14f, 0.20f, 0.86f, 0.86f, Rgb(236, 232, 220), 0.06f);
                    c.RectFilled(0.14f, 0.20f, 0.86f, 0.38f, Warning, 0.06f);
                    c.Line(0.32f, 0.12f, 0.32f, 0.28f, ink, 1f);
                    c.Line(0.68f, 0.12f, 0.68f, 0.28f, ink, 1f);
                    c.Text("1", 0.50f, 0.62f, Rgb(60, 60, 60));
                    break;
                case GameIcon.Timber:
                    // Log pile seen end-on.
                    for (int row = 0; row < 3; row++)
                        for (int col = 0; col < 3 - row; col++)
                        {
                            float x = 0.26f + col * 0.24f + row * 0.12f, y = 0.74f - row * 0.21f;
                            c.CircleFilled(x, y, 0.12f, Wood);
                            c.CircleFilled(x, y, 0.07f, Rgb(222, 182, 124));
                        }
                    break;
                case GameIcon.Speedometer:
                    c.Arc(0.50f, 0.62f, 0.34f, MathF.PI, MathF.Tau, ink, 1.1f);
                    c.Line(0.50f, 0.62f, 0.70f, 0.40f, Amber, 1.2f);
                    c.CircleFilled(0.50f, 0.62f, 0.06f, ink);
                    break;
            }
        }

        internal static void DrawRemovalBadge(ImDrawListPtr dl, NVec2 origin, float size, uint ink)
        {
            var center = origin + new NVec2(0.79f, 0.25f) * size;
            uint alpha = ink & 0xff000000;
            uint red = (Color(HudTheme.Bad) & 0x00ffffff) | alpha;
            uint dark = (Rgb(48, 61, 53) & 0x00ffffff) | alpha;
            dl.AddCircleFilled(center, size * 0.20f, red, 24);
            float arm = size * 0.073f;
            float stroke = MathF.Max(1.3f, size * 0.057f);
            dl.AddLine(center - new NVec2(arm, arm), center + new NVec2(arm, arm), dark, stroke);
            dl.AddLine(center + new NVec2(-arm, arm), center + new NVec2(arm, -arm), dark, stroke);
        }

        private static void Arrow(Canvas c, float x0, float y0, float x1, float y1, uint color)
        {
            float dx = x1 - x0, dy = y1 - y0, length = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
            dx /= length; dy /= length;
            float hx = x1 - dx * 0.16f, hy = y1 - dy * 0.16f;
            c.Line(x0, y0, hx, hy, color, 1.4f);
            c.Poly(color, x1, y1, hx - dy * 0.12f, hy + dx * 0.12f, hx + dy * 0.12f, hy - dx * 0.12f);
        }

        private static void Truck(Canvas c, float lift = 0f)
        {
            float y = lift * 0.5f;
            // Log bundle on the trailer, cab at the front.
            c.RectFilled(0.08f, 0.56f + y, 0.66f, 0.66f + y, Rgb(60, 64, 70));
            c.RectFilled(0.10f, 0.36f + y, 0.64f, 0.46f + y, Wood, 0.04f);
            c.RectFilled(0.10f, 0.46f + y, 0.64f, 0.56f + y, Rgb(196, 140, 84), 0.04f);
            c.CircleFilled(0.10f, 0.41f + y, 0.05f, Rgb(222, 182, 124));
            c.CircleFilled(0.10f, 0.51f + y, 0.05f, Rgb(222, 182, 124));
            c.Poly(TruckBlue, 0.66f, 0.66f + y, 0.66f, 0.34f + y, 0.82f, 0.34f + y, 0.92f, 0.50f + y, 0.92f, 0.66f + y);
            c.Poly(Rgb(40, 60, 80), 0.70f, 0.38f + y, 0.80f, 0.38f + y, 0.87f, 0.50f + y, 0.70f, 0.50f + y);
            foreach (float x in new[] { 0.20f, 0.42f, 0.80f })
            {
                c.CircleFilled(x, 0.70f + y, 0.09f, Rgb(30, 30, 32));
                c.CircleFilled(x, 0.70f + y, 0.04f, Rgb(170, 170, 170));
            }
        }

        private static void SawBlade(Canvas c, float x, float y, float r, uint color)
        {
            const int teeth = 10;
            var points = new float[teeth * 4];
            for (int i = 0; i < teeth; i++)
            {
                float a0 = i * MathF.Tau / teeth, a1 = a0 + MathF.Tau / teeth * 0.55f;
                points[i * 4] = x + MathF.Cos(a0) * r; points[i * 4 + 1] = y + MathF.Sin(a0) * r;
                points[i * 4 + 2] = x + MathF.Cos(a1) * r * 0.78f; points[i * 4 + 3] = y + MathF.Sin(a1) * r * 0.78f;
            }
            c.Poly(color, points);
            c.CircleFilled(x, y, r * 0.22f, Rgb(80, 80, 86));
        }

        private static void SunGlyph(Canvas c, float x, float y, float r)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.Tau / 8;
                c.Line(x + MathF.Cos(a) * r * 1.35f, y + MathF.Sin(a) * r * 1.35f,
                    x + MathF.Cos(a) * r * 1.85f, y + MathF.Sin(a) * r * 1.85f, Amber, 1f);
            }
            c.CircleFilled(x, y, r, Amber);
        }

        private static void CloudGlyph(Canvas c, uint color, float lift = 0f)
        {
            c.CircleFilled(0.36f, 0.58f + lift, 0.17f, color);
            c.CircleFilled(0.56f, 0.48f + lift, 0.22f, color);
            c.CircleFilled(0.72f, 0.60f + lift, 0.15f, color);
            c.RectFilled(0.36f, 0.58f + lift, 0.72f, 0.75f + lift, color);
        }

        /// <summary>Unit-square drawing surface: every coordinate is a fraction of the icon size.</summary>
        private readonly struct Canvas
        {
            private readonly ImDrawListPtr dl;
            private readonly NVec2 origin;
            private readonly float size;
            private readonly float stroke;

            internal Canvas(ImDrawListPtr dl, NVec2 origin, float size, uint ink)
            {
                this.dl = dl; this.origin = origin; this.size = size;
                stroke = MathF.Max(1.2f, size * 0.06f);
            }

            private NVec2 P(float x, float y) => origin + new NVec2(x * size, y * size);

            internal void Line(float x0, float y0, float x1, float y1, uint color, float weight) =>
                dl.AddLine(P(x0, y0), P(x1, y1), color, stroke * weight);

            internal void Rect(float x0, float y0, float x1, float y1, uint color, float weight, float rounding = 0f) =>
                dl.AddRect(P(x0, y0), P(x1, y1), color, rounding * size, ImDrawFlags.None, stroke * weight);

            internal void RectFilled(float x0, float y0, float x1, float y1, uint color, float rounding = 0f) =>
                dl.AddRectFilled(P(x0, y0), P(x1, y1), color, rounding * size);

            internal void Circle(float x, float y, float r, uint color, float weight) =>
                dl.AddCircle(P(x, y), r * size, color, 0, stroke * weight);

            internal void CircleFilled(float x, float y, float r, uint color)
            {
                if (r > 0f) dl.AddCircleFilled(P(x, y), r * size, color);
            }

            internal void EllipseFilled(float x, float y, float rx, float ry, uint color) =>
                dl.AddEllipseFilled(P(x, y), new NVec2(rx * size, ry * size), color);

            internal void Ellipse(float x, float y, float rx, float ry, uint color, float weight) =>
                dl.AddEllipse(P(x, y), new NVec2(rx * size, ry * size), color, 0f, 0, stroke * weight);

            internal void Arc(float x, float y, float r, float from, float to, uint color, float weight)
            {
                dl.PathArcTo(P(x, y), r * size, from, to);
                dl.PathStroke(color, ImDrawFlags.None, stroke * weight);
            }

            /// <summary>Filled polygon from x,y pairs. Concave outlines are fanned from their centroid.</summary>
            internal void Poly(uint color, params float[] xy)
            {
                int count = xy.Length / 2;
                if (count < 3) return;
                float cx = 0, cy = 0;
                for (int i = 0; i < count; i++) { cx += xy[i * 2]; cy += xy[i * 2 + 1]; }
                NVec2 centre = P(cx / count, cy / count);
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    dl.AddTriangleFilled(centre, P(xy[i * 2], xy[i * 2 + 1]), P(xy[j * 2], xy[j * 2 + 1]), color);
                }
            }

            internal void Text(string text, float x, float y, uint color)
            {
                NVec2 extent = ImGui.CalcTextSize(text);
                dl.AddText(P(x, y) - extent * 0.5f, color, text);
            }
        }
    }
}
