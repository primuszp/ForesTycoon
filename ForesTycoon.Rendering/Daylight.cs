using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    /// <summary>The light of one moment: where it comes from and its colours, and the season's colour shift.</summary>
    /// <param name="Azimuth">Direction the light comes from, degrees (the sun by day, the moon by night).</param>
    /// <param name="Elevation">Height of the light above the horizon, degrees (always above it).</param>
    /// <param name="SunTint">Multiplier on the direct light: warm and low at dawn and dusk, pale blue moonlight at night.</param>
    /// <param name="SkyTint">Multiplier on the ambient sky light.</param>
    /// <param name="GroundTint">Multiplier on green ground: fresh in spring, deep in summer, straw in autumn, faded in winter.</param>
    /// <param name="Night">0 by day … 1 deep night.</param>
    internal readonly record struct DaylightState(float Azimuth, float Elevation, Vector3 SunTint, Vector3 SkyTint, Vector3 GroundTint, float Night);

    /// <summary>
    /// Times of day and seasons for the lighting, as pure functions of the calendar. The forest year starts in spring
    /// (0.25 is midsummer, 0.75 midwinter). A visible day is one month long: a calendar day lasts seconds at normal
    /// speed, so following it would flicker. Nights stay readable: moonlit blue, never black.
    /// </summary>
    internal static class Daylight
    {
        /// <summary>Visible days per forest year.</summary>
        internal const double DaysPerYear = 12;
        /// <summary>Hour of the visible day at the start of the year: a new game opens in the morning, not at midnight.</summary>
        internal const double StartHour = 0.33;

        internal static DaylightState At(double forestYears)
        {
            double yearFraction = forestYears - Math.Floor(forestYears);
            double days = forestYears * DaysPerYear + StartHour;
            double dayFraction = days - Math.Floor(days);
            // +1 at midsummer, -1 at midwinter.
            float summer = (float)Math.Cos(2 * Math.PI * (yearFraction - 0.25));
            return At((float)yearFraction, (float)dayFraction, summer);
        }

        internal static DaylightState At(float yearFraction, float dayFraction, float summer)
        {
            // Day length and the sun's noon height follow the season.
            float halfDay = 0.27f + 0.07f * summer;                 // fraction of the cycle from noon to sunset
            float noonElevation = 40f + 22f * summer;               // degrees
            float fromNoon = dayFraction - 0.5f;                    // noon at mid-cycle
            float daylight = 1 - Math.Abs(fromNoon) / halfDay;      // 1 at noon, 0 at sunrise/sunset, < 0 at night
            float sunHeight = Math.Clamp(daylight, -1, 1);
            float elevation = noonElevation * MathF.Sin(MathF.PI * 0.5f * Math.Clamp(sunHeight, 0, 1));
            // East in the morning, south at noon, west in the evening (world +X is east, +Y north).
            float azimuth = 360f - 180f * Math.Clamp(0.5f + fromNoon / (2 * halfDay), 0, 1);   // 360 east → 270 south → 180 west

            // Twilight blends the sun into the moon over a short band around sunrise and sunset.
            float night = Math.Clamp(-sunHeight * 4f + 0.15f, 0, 1);
            float golden = Math.Clamp(1 - elevation / 30f, 0, 1) * (1 - night);
            var noon = new Vector3(1.00f, 0.97f, 0.92f) * (1 + 0.08f * summer);
            var dusk = new Vector3(1.55f, 0.72f, 0.34f);
            var moon = new Vector3(0.20f, 0.27f, 0.46f);
            Vector3 sunTint = Vector3.Lerp(Vector3.Lerp(noon, dusk, golden * golden), moon, night);
            var skyDay = new Vector3(1.00f, 1.00f, 1.00f) * (0.96f + 0.06f * summer);
            var skyDusk = new Vector3(1.02f, 0.66f, 0.50f);
            var skyNight = new Vector3(0.30f, 0.37f, 0.60f);
            Vector3 skyTint = Vector3.Lerp(Vector3.Lerp(skyDay, skyDusk, golden), skyNight, night);
            if (night > 0)
            {
                // The moon stands high in the opposite quarter so shapes stay legible.
                elevation = MathHelper.Lerp(elevation, 38f, night);
                azimuth = MathHelper.Lerp(azimuth, 45f, night);
            }
            elevation = Math.Max(elevation, 8f);                    // keep shadows finite at the horizon

            // Season tint of green ground and foliage-free grass.
            Vector3 groundTint = SeasonGround(yearFraction);
            return new DaylightState(azimuth, elevation, sunTint, skyTint, groundTint, night);
        }

        private static Vector3 SeasonGround(float yearFraction)
        {
            // Keyframes at mid-season: spring fresh, summer deep, autumn straw, winter faded; blended around the year.
            Vector3[] keys =
            {
                new(0.86f, 1.20f, 0.70f),   // spring (0.125)
                new(0.90f, 0.98f, 0.80f),   // summer (0.375)
                new(1.85f, 1.02f, 0.34f),   // autumn (0.625)
                new(1.30f, 0.86f, 0.74f),   // winter (0.875)
            };
            float f = yearFraction * 4 - 0.5f + 4;
            int i = (int)Math.Floor(f) % 4;
            float t = f - MathF.Floor(f);
            t = t * t * (3 - 2 * t);
            return Vector3.Lerp(keys[i], keys[(i + 1) % 4], t);
        }

        internal static readonly DaylightState Neutral =
            new(135, 48, Vector3.One, Vector3.One, Vector3.One, 0);

        /// <summary>The light of the frame being drawn; every lit shader reads its tints from here.</summary>
        private sealed class FrameLight { internal DaylightState Current = Neutral; internal DaylightState? Override; }
        private static readonly object lightKey = new();
        private static FrameLight State => RenderDevice.GetState(lightKey, () => new FrameLight());
        internal static DaylightState Current { get => State.Current; set => State.Current = value; }
        /// <summary>Diagnostics: a fixed light instead of the calendar's.</summary>
        internal static DaylightState? Override { get => State.Override; set => State.Override = value; }
    }
}
