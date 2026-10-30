using System;
using System.ComponentModel;
using System.Linq;
using ForesTycoon.Rules;
using OpenTK.Mathematics;

namespace ForesTycoon;

internal sealed partial class WildlifeSystem
{
    [RuleEntity("animal", Display = "Állat", Element = "állat", Abstract = true, DiscoverDerived = true,
        Help = "Élő állat. Állapotváltozásait és a motoralgoritmusok meghívását a projekt szabályai adják.")]
    internal abstract class Animal : IWorldObject, IRuleInstance
    {
        internal SimulationWorld World;
        internal WildlifeSystem System;
        internal double StepSeconds;
        internal int TileId, TargetTile;
        internal Vector3 Position, PreviousPosition, Target;
        internal float Yaw, PreviousYaw, Blend;
        internal double WalkTime, PreviousWalkTime, PreviousAge;
        internal uint Seed;
        internal bool Removed;
        [DisplayName("Állat azonosítója")] public int Id { get; internal set; }
        int IRuleInstance.RuleId => Id;
        double IWorldObject.Index => Id;
        [RuleField("A szimulált állat életkora.", Display = "Életkor", Unit = "s", Min = 0)] public double Age { get; set; }
        [RuleField("Az éhség készlete.", Display = "Éhség", Min = 0, Max = 1)] public float Hunger { get; set; }
        [RuleField("A helyváltoztatás iránti igény.", Display = "Vándorlási igény", Min = 0, Max = 1)] public float WanderNeed { get; set; }
        [DisplayName("Kezdőállapot beállítva")] public bool Initialized { get; set; }
        [DisplayName("Seedelt kezdeti változatosság")] public double SeedFraction => (Seed & 0x7fffffffu) / (double)int.MaxValue;
        [RuleField("A test irányszöge.", Display = "Irány", Unit = "rad", Min = -1e6, Max = 1e6)]
        public double Heading { get => Yaw; set { Yaw = (float)value; if (!Initialized) PreviousYaw = Yaw; } }
        [RuleField("Az útvonalpontozó zaj időbeli paramétere.", Display = "Változatosság időléptéke", Min = 0, Max = 1e6)] public double NoiseTimeScale { get; set; }
        [RuleField("Az útvonalpontozó zaj térbeli paramétere.", Display = "Változatosság térléptéke", Min = 0, Max = 1e6)] public double NoiseTileScale { get; set; }
        [RuleField("A pontozásban szereplő esőarány skálája.", Display = "Esőskála", Unit = "mm/h", Min = .001, Max = 1e6)] public double RainScale { get; set; } = 1;
        [RuleTypeReference(typeof(Animal))]
        [DisplayName("Állattípus")] public int TypeId => RuleSchema.TypeCode(RuleSchema.EntityId(GetType()));
        [RuleField("A mozgási algoritmus sebességbemenete.", Display = "Haladási sebesség", Unit = "m/s", Min = 0, Max = 100)] public float MovementSpeed { get; set; }
        [RuleField("A mozgási algoritmus fordulási sugara.", Display = "Fordulási sugár", Unit = "m", Min = .001, Max = 1000)] public float TurnRadius { get; set; } = 1;
        [RuleField("Mekkora távolságon belül érkezett meg.", Display = "Megérkezési távolság", Unit = "m", Min = 0, Max = 100)] public float ArrivalRadius { get; set; }
        [RuleField("A járás fel- és lefutásának üteme.", Display = "Járásváltás üteme", Unit = "1/s", Min = 0, Max = 100)] public float GaitResponse { get; set; }
        [RuleField("Fordulás elzárt út esetén.", Display = "Akadály előtti fordulás", Unit = "rad/s", Min = 0, Max = 100)] public float BlockedTurnSpeed { get; set; }
        [RuleField("A járás során megtett távolság.", Display = "Lépésben megtett út", Unit = "m", Min = 0)] public double DistanceMoved { get; internal set; }
        [DisplayName("Megérkezett")] public bool Arrived => (Target.Xy - Position.Xy).Length <= ArrivalRadius;
        [DisplayName("Testméret szerinti járási sebesség")] public double BodyWalkingSpeed => WalkingSpeed;
        [DisplayName("Testméret szerinti fordulási sugár")] public double BodyTurningRadius => TurningRadius;
        [RuleField("A projekt táplálékkínálati gráfjának eredménye az aktuális csempén.", Display = "Táplálékkínálat", Min = 0, Max = 1)]
        public double Food => System.Gameplay.Evaluate("wildlife.food", System.Forage(TileId), ((IForestHabitat)World.Map).GetMoisture(TileId));
        [RuleRelation("A csempe, amelyen az állat áll.", Display = "Csempe")]
        public ITile Tile => World?.RuleTile(TileId);
        [RuleRelation("Az állat típusának állományadatai és projektben megadott célértékei.", Display = "Állomány")]
        public AnimalType Population => System.AnimalTypes.SingleOrDefault(t => t.Entity.Type == GetType());
        [RuleAction("A célcsempére érkezés geometriai lezárása.", Display = "Megérkezés rögzítése")]
        public bool Settle() { if (Arrived) TileId = TargetTile; return false; }
        [RuleAction("Járható szomszédok közül a projekt pontszámgráfja alapján választ.", Display = "Célpont kiválasztása")]
        public bool ChooseDestination() { System.ChooseDestination(this); return false; }
        [RuleAction("A megadott sebességgel és fordulási sugárral lépteti a testet a felszínen.", Display = "Mozgás végrehajtása")]
        public bool Advance() { System.Advance(this); return false; }
        [RuleAction("Az állat eltávolítása a világból.", Display = "Eltávolítás")]
        public bool Remove() { Removed = true; return false; }
    }

    [DisplayName("Gímszarvas")]
    internal sealed class Deer : Animal { }

    [RuleEntity("forage", Display = "Legelő", Element = "legelő", Help = "A táplálék elfogyasztott hányada; regenerációját a projekt adja.")]
    internal sealed class ForageCell : IRuleInstance, IWorldObject
    {
        [DisplayName("Csempe azonosítója")] public int Id { get; internal set; }
        int IRuleInstance.RuleId => Id;
        double IWorldObject.Index => Id;
        [RuleField("A legelő elfogyasztott hányada.", Display = "Táplálékfogyás", Min = 0, Max = 1)] public float Depletion { get; set; }
    }
}
