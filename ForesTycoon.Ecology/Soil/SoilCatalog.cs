using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForesTycoon.Ecology
{
    internal sealed class SoilProfileData
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string Color { get; init; }
        [JsonRequired] public double PreferredMoisture { get; init; }
        [JsonRequired] public double PreferredTexture { get; init; }
        public SoilProperties Properties { get; init; }
    }

    internal sealed class SoilCatalogData
    {
        public int Version { get; init; }
        public SoilProfileData[] Profiles { get; init; }
    }

    internal readonly record struct SoilProfile(string Id, string Name, uint Color,
        double PreferredMoisture, double PreferredTexture, SoilProperties Properties);

    /// <summary>Validated immutable profiles compiled once from a versioned JSON document.</summary>
    internal sealed class SoilCatalog
    {
        private static readonly JsonSerializerOptions options = new() {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        private readonly SoilProfile[] profiles;
        internal ReadOnlySpan<SoilProfile> Profiles => profiles;
        internal SoilProfile this[int index] => profiles[index];
        internal string Json { get; }
        internal string Hash { get; }

        internal SoilCatalog(string json)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            var data = JsonSerializer.Deserialize<SoilCatalogData>(json, options) ?? throw new InvalidDataException("Empty soil catalog.");
            if (data.Version != 1) throw new NotSupportedException("Unsupported soil catalog version.");
            if (data.Profiles == null || data.Profiles.Length == 0 || data.Profiles.Length > 256)
                throw new InvalidDataException("Soil catalog must contain 1 to 256 profiles.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            profiles = new SoilProfile[data.Profiles.Length];
            for (int i = 0; i < profiles.Length; i++)
            {
                var p = data.Profiles[i] ?? throw new InvalidDataException("Null soil profile.");
                if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name) || !ids.Add(p.Id))
                    throw new InvalidDataException("Soil profile needs a unique ID and a name.");
                if (!double.IsFinite(p.PreferredMoisture) || p.PreferredMoisture < 0 || p.PreferredMoisture > 1 ||
                    !double.IsFinite(p.PreferredTexture) || p.PreferredTexture < 0 || p.PreferredTexture > 1)
                    throw new InvalidDataException("Soil generation preferences must be between 0 and 1.");
                p.Properties.Validate();
                if (p.Color == null || p.Color.Length != 7 || p.Color[0] != '#' ||
                    !uint.TryParse(p.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                    throw new InvalidDataException("Soil color must be #RRGGBB.");
                uint packed = 0xff000000u | ((rgb & 255) << 16) | (rgb & 0xff00) | (rgb >> 16);
                profiles[i] = new(p.Id, p.Name, packed, p.PreferredMoisture, p.PreferredTexture, p.Properties);
            }
            Json = JsonSerializer.Serialize(data, options);
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json)));
        }

        internal static SoilCatalog Default { get; } = LoadDefault();
        private static SoilCatalog LoadDefault()
        {
            using var stream = typeof(SoilCatalog).Assembly.GetManifestResourceStream("ForesTycoon.Ecology.Soil.profiles.json")
                ?? throw new InvalidOperationException("Missing built-in soil catalog.");
            using var reader = new StreamReader(stream);
            return new(reader.ReadToEnd());
        }
        internal static SoilCatalog Legacy { get; } = new(JsonSerializer.Serialize(new SoilCatalogData {
            Version = 1, Profiles = new[] { new SoilProfileData { Id = "standard", Name = "Egységes talaj (régi modell)",
                Color = "#A98A62", PreferredMoisture = .5, PreferredTexture = .5, Properties = SoilProperties.Standard } }
        }, options));
    }
}
