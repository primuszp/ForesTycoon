using System;
using System.Collections.Generic;

namespace ForesTycoon.TreeModels
{
    /// <summary>Authoring controls for the existing leaf-cluster crown surface, independent of branch tessellation.</summary>
    internal static class TreeCrownParameters
    {
        internal static readonly TreeShapeParameters.Entry[] Entries =
        {
            new("CrownClusters", "Lombtömegek száma", "Lombkorona", 3, 64, true,
                "Hány ágvégi lombcsoport formálja a koronát. A közeli korona összesített részletessége megmarad."),
            new("CrownLayers", "Lombszintek száma", "Lombkorona", 0, 4, true,
                "0 vagy 1: természetes, szabálytalan magasságok. 2–4: a lombtömegek rétegekbe rendeződnek."),
            new("CrownFlatten", "Lombtömegek aránya", "Lombkorona", .2, 1.5, false,
                "A lombtömeg magassága a szélességéhez képest. Kisebb érték: lapos párnák, nagyobb: gömbölyűbb tömegek."),
            new("CrownCore", "Koronaközép teltsége", "Lombkorona", .1, .65, false,
                "A belső lomb teltsége: zárt koronánál az összekötő rész szélessége, nyitott koronánál a belső hajtáscsoportok teltsége."),
            new("CrownRoundness", "Korona összesimulása", "Lombkorona", 0, 1, false,
                "0: az ágvégi hajtáscsoportok szabálytalan alakja. 1: sima, tojásdad burok; nyitott koronánál lombpárnánként."),
            new("CrownJitter", "Korona egyenetlensége", "Lombkorona", 0, .35, false,
                "Összefüggő, kisebb és nagyobb lombfoltok a felszínen. A részletek minden távolságból ugyanott maradnak; a háromszögek száma változatlan."),
            new("CrownBranchFollow", "Ágvégek követése", "Lombkorona", 0, 1, false,
                "Mennyire követik a lombtömegek az élő ágvégek helyét. Kisebb értéknél tömörebb, nagyobbnál tagoltabb korona."),
            new("CrownAnisotropy", "Lombtömegek nyúlása", "Lombkorona", 0, 1, false,
                "A lombcsoportok elnyúlása a levélpontjaik fő irányában. 0: kör alapú tömegek; 1: az ágvégi hajtások alakját követik."),
            new("CrownGaps", "Ágközi rések", "Lombkorona", 0, 1, false,
                "0: egyetlen zárt koronaburok. Pozitív értéknél külön ágvégi lombpárnák, köztük látható ágakkal. Nagyobb érték nyitottabb koronát ad.")
        };

        internal static bool IsSurfaceParameter(string name)
        {
            foreach (var entry in Entries) if (entry.Name == name) return true;
            return false;
        }

        internal static LobeCrownMesh.Profile? Resolve(ForestSpecies species, TreeLifePhase phase, bool dead = false, bool commonOnly = false)
        {
            if (!LobeCrownMesh.Applies(species, phase)) return null;
            var baseline = LobeCrownMesh.For(DendroCrownMesh.For(species, phase));
            if (baseline is not { } profile) return null;
            var form = ForestSpeciesBook.Shape(species);
            int clusters = phase >= TreeLifePhase.Old ? profile.Old : phase == TreeLifePhase.Mature ? profile.Mature : profile.Young;
            clusters = (int)Value(0, clusters);
            return profile with { Young = clusters, Mature = clusters, Old = clusters,
                Layers = (int)Value(1, profile.Layers), Flatten = (float)Value(2, profile.Flatten),
                CoreRadius = (float)Value(3, profile.CoreRadius), Roundness = (float)Value(4, profile.Roundness),
                Jitter = (float)Value(5, profile.Jitter), BranchFollow = (float)Value(6, profile.BranchFollow),
                Anisotropy = (float)Value(7, profile.Anisotropy), Gaps = (float)Value(8, profile.Gaps) };

            double Value(int index, double fallback)
            {
                var entry = Entries[index];
                double value = fallback;
                if (form?.All.TryGetValue(entry.Name, out double common) == true) value = common;
                if (!commonOnly && form != null)
                {
                    if (form.Phases.TryGetValue(phase, out var own) && own.TryGetValue(entry.Name, out double specific)) value = specific;
                    if (dead && form.Dead?.TryGetValue(entry.Name, out double final) == true) value = final;
                }
                value = double.IsFinite(value) ? Math.Clamp(value, entry.Min, entry.Max) : fallback;
                return entry.Integer ? Math.Round(value) : value;
            }
        }

        // The editor sees the same effective surface values that the mesher consumes.
        // These are not Weber–Penn branch parameters and never enter TreeParams.
        internal static void AppendValues(Dictionary<string, double> values, ForestSpecies species, TreeLifePhase phase,
            bool dead = false, bool commonOnly = false)
        {
            if (Resolve(species, phase, dead, commonOnly) is not { } profile) return;
            values["CrownClusters"] = profile.Mature;
            values["CrownLayers"] = profile.Layers;
            values["CrownFlatten"] = profile.Flatten;
            values["CrownCore"] = profile.CoreRadius;
            values["CrownRoundness"] = profile.Roundness;
            values["CrownJitter"] = profile.Jitter;
            values["CrownBranchFollow"] = profile.BranchFollow;
            values["CrownAnisotropy"] = profile.Anisotropy;
            values["CrownGaps"] = profile.Gaps;
        }
    }
}
