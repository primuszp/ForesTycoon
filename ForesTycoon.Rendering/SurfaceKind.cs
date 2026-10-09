using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    /// <summary>Material family a draw call belongs to; the surface shader picks its look from it.</summary>
    internal enum SurfaceKind { Plain = 0, Ground = 1, Skirt = 2, Road = 3, Vehicle = 4, Wood = 5, Foliage = 6, Water = 7, ForestFloor = 8, Rubber = 9, LogCargo = 10, Macadam = 11, SkidTrail = 12, RoadShoulder = 13 }

    /// <summary>The scene-wide surface shading state that geometry code reads and sets while drawing.</summary>
    internal interface ISurfaceVisuals
    {
        /// <summary>True while the shadow depth pass is being drawn.</summary>
        bool ShadowPass { get; }
        SurfaceKind Kind { get; set; }
        /// <summary>Light-space view-projection of the shadow map.</summary>
        Matrix4 ShadowCamera { get; }
        /// <summary>The shadow map holds this frame's depth and may be sampled.</summary>
        bool ShadowsReady { get; }
        /// <summary>Cloud, storm, lightning flash and wetness, as the surface shader's climate input.</summary>
        Vector4 Atmosphere { get; }
        /// <summary>True when the enhanced surface shader replaces the plain geometry shader.</summary>
        bool Active { get; }
        /// <summary>Binds the surface shader and its camera and model uniforms.</summary>
        void Use(float outlineWidth = 0);
    }

    internal enum GraphicsQuality { Low, Medium, High }

    /// <summary>Lighting options shared by every renderer that shades its own geometry.</summary>
    internal interface IShadingSettings
    {
        bool Enhanced { get; }
        bool Lighting { get; }
        bool Textures { get; }
        /// <summary>Whether imported models draw their silhouette outline.</summary>
        bool ModelOutlines { get; }
        float SunAzimuth { get; }
        float SunElevation { get; }
    }

    /// <summary>Options of the diorama post-processing pass, read by <see cref="DioramaPostProcess"/>.</summary>
    internal interface IPostProcessSettings
    {
        bool Enhanced { get; }
        bool Diorama { get; }
        bool StudioBackdrop { get; }
        bool AmbientOcclusion { get; }
        float AmbientOcclusionStrength { get; }
        int OcclusionPairs { get; }
        bool ColorGrading { get; }
        bool TiltShift { get; }
        float TiltShiftStrength { get; }
        int TiltShiftTaps { get; }
        bool Vignette { get; }
    }
}
