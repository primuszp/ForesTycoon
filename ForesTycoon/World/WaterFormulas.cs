namespace ForesTycoon
{
    /// <summary>The growth water response as an editable formula; the water balance itself is the water rules' flows.</summary>
    internal static class WaterFormulas
    {
        internal const string Stress =
            "# Vízválasz: a vízellátottság a faj szárazságérzékenységének hatványán; a pangó víz csökkenti.\n" +
            "korlát(hatvány(vízellátottság; faj.szárazságérzékenység) * (1 - pangóvíz * 0,65); 0; 1)";
    }
}
