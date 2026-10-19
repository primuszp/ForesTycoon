using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon;

internal sealed partial class WildlifeSystem
{
    [RuleEntity("animalType", Display = "Állattípus állománya", Element = "állattípus",
        Help = "Minden konkrét állatosztályhoz automatikusan létrejön. A kívánt állományt a projekt szabályai adják.")]
    internal sealed class AnimalType : IRuleInstance, IWorldObject, IRuleDisplay
    {
        internal WildlifeSystem System;
        internal RuleEntitySchema Entity;
        [RuleTypeReference(typeof(Animal))]
        [DisplayName("Állattípus")] public int TypeId => RuleSchema.TypeCode(Entity.Id);
        int IRuleInstance.RuleId => TypeId;
        string IRuleDisplay.RuleLabel => Entity.Name;
        double IWorldObject.Index => TypeId;
        [DisplayName("Jelenlegi egyedszám")] public int Population => Members.Count();
        [DisplayName("Felderített élőhelyek")] public int HabitatCount => System.spots.Count;
        [DisplayName("Élőhely felderítve")] public bool HabitatReady => System.habitatReady;
        [DisplayName("Projekt állománykerete")] public double PopulationLimit => System.Gameplay.Evaluate("wildlife.population");
        [RuleField("A szabályok által kért egyedszám.", Display = "Kívánt egyedszám", Unit = "db", Min = 0, Max = 1024)]
        public int DesiredPopulation { get; set; }
        [RuleCollection("A típus élő egyedei.", Display = "Egyedek", Addressable = true)]
        public IEnumerable<Animal> Members => System.Animals.Where(a => !a.Removed && a.GetType() == Entity.Type);
        [RuleAction("A kért darabszámot alkalmazza, a felderített helyeken, szeletelten. A típus konstruktora nem tartalmaz populációs döntést.", Display = "Állomány alkalmazása")]
        public bool ApplyPopulation() {
            System.populationRequests.Add(new(this, DesiredPopulation));
            return false;
        }
    }

    private AnimalType[] animalTypes;
    internal IEnumerable<AnimalType> AnimalTypes => animalTypes ??= CreateTypes(GameWorld.RuleObjects);
    internal AnimalType[] CreateTypes(RuleSchema schema) => schema.Entities
        .Where(e => e.Type.IsClass && !e.Abstract && typeof(Animal).IsAssignableFrom(e.Type))
        .Select(e => new AnimalType { Entity = e, System = this }).OrderBy(e => e.Entity.Id, StringComparer.Ordinal).ToArray();
    internal Func<double, IEnumerable<int>> PopulationRuleStep;
    internal Func<int[], IEnumerable<int>> BirthRuleStep;
    private readonly List<int> born = new();
    private sealed record PopulationRequest(AnimalType Type, int Desired);
    private readonly List<PopulationRequest> populationRequests = new();

    private IEnumerable<int> ApplyPopulationsWork()
    {
        foreach (var request in populationRequests) {
            var type = request.Type;
            int desired = Math.Clamp(request.Desired, 0, Math.Min(1024, spots.Count));
            var members = type.Members.ToList();
            for (int i = members.Count - 1; i >= desired; i--) { Animals.Remove(members[i]); yield return 1; }
            for (int i = members.Count; i < desired && NextAnimalId < 1_000_000; i++) {
                var spot = spots[i];
                var animal = (Animal)Activator.CreateInstance(type.Entity.Type, nonPublic: true);
                animal.Id = NextAnimalId++; animal.TileId = animal.TargetTile = spot.TileId;
                animal.Position = animal.PreviousPosition = animal.Target = spot.Position;
                animal.System = this; animal.Seed = spot.Rank;
                Animals.Add(animal); born.Add(animal.Id); yield return 1;
            }
        }
        populationRequests.Clear();
    }
}
