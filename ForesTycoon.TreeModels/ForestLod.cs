namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// Level of detail a tree model is generated for. Tessellation is chosen so the silhouette error
    /// stays under about one pixel at the finest zoom of each band (see DendroCrownMesh.ReferencePixels).
    /// </summary>
    internal enum ForestLod { Far, Medium, Near }
}
