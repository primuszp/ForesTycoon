using System;
using System.IO;
using System.Text.Json.Serialization;

namespace ForesTycoon
{
    /// <summary>Pins the catalog content and generator semantics, including upgraded legacy worlds.</summary>
    internal sealed class SoilModelData
    {
        [JsonRequired] public int GeneratorVersion { get; init; }
        [JsonRequired] public string CatalogJson { get; init; }
        [JsonRequired] public string CatalogHash { get; init; }
        /// <summary>Painted soil by tile ID; absent in saves before the world editor.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public System.Collections.Generic.Dictionary<int, byte> Overrides { get; init; }

        internal static SoilModelData From(SoilLandscapeDefinition definition) => new() {
            GeneratorVersion = definition.GeneratorVersion,
            CatalogJson = definition.Catalog.Json, CatalogHash = definition.Catalog.Hash,
            Overrides = definition.Overrides.Count == 0 ? null : new(definition.Overrides)
        };

        internal SoilLandscapeDefinition ToDefinition()
        {
            var catalog = new SoilCatalog(CatalogJson);
            if (!string.Equals(catalog.Hash, CatalogHash, StringComparison.Ordinal))
                throw new InvalidDataException("Saved soil catalog hash does not match its content.");
            return new SoilLandscapeDefinition(GeneratorVersion, catalog, Overrides);
        }
    }
}
