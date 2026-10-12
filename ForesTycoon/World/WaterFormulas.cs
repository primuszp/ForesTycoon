using System.Globalization;

namespace ForesTycoon
{
    /// <summary>
    /// The water balance rules as formulas: exactly what the native code computes, readable and editable in the world
    /// editor. A project that installs one of them runs the formula instead of the code.
    /// </summary>
    internal static class WaterFormulas
    {
        private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
        internal static readonly string Snowmelt =
            "# Hóolvadás: a fagypont feletti hőmérséklet és az idő olvasztja a havat; a vastag hó gyorsabban fogy.\n" +
            "olvadási_fok = max(0; hőmérséklet) * óra\n" +
            $"(hókészlet + {N(EnvironmentSystem.SnowMeltBaseRate)} / {N(EnvironmentSystem.SnowMeltDepthRate)}) * (1 - exp(-{N(EnvironmentSystem.SnowMeltDepthRate)} * olvadási_fok))";
        internal const string Infiltration =
            "# Beszivárgás: a felszíni vízből annyi jut a talajba, amennyit a talaj ütemben és szabad helyben elnyel.\n" +
            "ütem = ha(burkolt > 0,5; 0,5; talaj.beszivárgás) * óra\n" +
            "szabad_hely = max(0; talaj.telítettség - gyökérzónavíz)\n" +
            "min(felszíni_víz; min(ütem; szabad_hely))";
        internal const string Evaporation =
            "# Talajpárolgás: a maradék párolgási igényből a nyitott, lombbal nem fedett talaj ennyit párologtat el.\n" +
            "hozzáférhető = korlát((gyökérzónavíz - talaj.hervadáspont) / (talaj.vízkapacitás - talaj.hervadáspont); 0; 1)\n" +
            "min(max(0; gyökérzónavíz - talaj.hervadáspont); párolgási_igény * hozzáférhető * (1 - lombborítás) * 0,65)";
        internal const string Uptake =
            "# Vízfelvétel: a fák vízigényéből annyi teljesül, amennyit a talaj nedvessége enged.\n" +
            "hozzáférhető = korlát((gyökérzónavíz - talaj.hervadáspont) / (talaj.vízkapacitás - talaj.hervadáspont); 0; 1)\n" +
            "min(max(0; gyökérzónavíz - talaj.hervadáspont); vízigény * hozzáférhető)";
        internal const string Drainage =
            "# Elszivárgás: a szabadföldi vízkapacitás feletti víz a talaj drénezésének ütemében a mélybe szivárog.\n" +
            "min(többletvíz; talaj.drénezés * óra)";
        internal const string Baseflow =
            "# Alaplefolyás: a mélyvíz lassan, óránként 0,15 mm-rel forrásokon és patakokon át távozik.\n" +
            "min(mélyvíz; 0,15 * lépés_órái)";
        internal const string Stress =
            "# Vízválasz: a vízellátottság a faj szárazságérzékenységének hatványán; a pangó víz csökkenti.\n" +
            "korlát(hatvány(vízellátottság; faj.szárazságérzékenység) * (1 - pangóvíz * 0,65); 0; 1)";
    }
}
