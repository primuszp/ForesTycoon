using System;
using System.Collections.Generic;
namespace ForesTycoon.Ecology
{
    internal static class LargeForestGenerator
    {
        internal static ForestStand[] Create(IForestHabitat habitat,int columns,int rows,ForestPattern pattern)
        {
            var result = new ForestStand[habitat.TileCount];
            SimulationWork.Complete(CreateWork(habitat, columns, rows, pattern, result)); return result;
        }
        internal static IEnumerable<int> CreateWork(IForestHabitat habitat,int columns,int rows,ForestPattern pattern, ForestStand[] result, ForesTycoon.Rules.GameplayProgram gameplay = null)
        {
            gameplay ??= ForesTycoon.Rules.GameplayProgram.Default;
            if(columns*rows!=habitat.TileCount) throw new ArgumentException("Incorrect forest grid.");

            float Hash(int x,int y,int salt){
                uint h=unchecked((uint)(habitat.Seed+x*374761393+y*668265263+salt*1442695041));
                h=(h^(h>>13))*1274126177; return (h^(h>>16))/(float)uint.MaxValue;
            }
            float shiftX=(Hash(0,0,1)-0.5f)*0.1f, shiftY=(Hash(0,0,2)-0.5f)*0.1f;
            for(int x=0;x<columns;x++) for(int y=0;y<rows;y++){
                yield return 1;
                int id=x*rows+y;
                if(!habitat.CanSupportForest(id)) continue;
                float u=(x+0.5f)/columns, v=(y+0.5f)/rows;
                bool mixed=pattern==ForestPattern.LargeMixed;
                float a=Ellipse(u,v,0.28f+shiftX,0.34f+shiftY,mixed?0.31f:0.43f,mixed?0.36f:0.44f);
                float b=Ellipse(u,v,0.7f-shiftX,0.66f-shiftY,mixed?0.32f:0.43f,mixed?0.35f:0.44f);
                if (gameplay.Evaluate("forest.initialAllow", (int)pattern, Math.Min(a,b), Hash(x/3,y/3,3), habitat.GetMoisture(id), habitat.GetNormalizedElevation(id), 0, 0, ForestPatchNoise.At(habitat.Seed, x, y)) < .5) continue;
                float speciesSeed=Hash(x/6,y/6,4);
                ForestSpecies species=(ForestSpecies)(int)gameplay.Evaluate("forest.initialSpecies", (int)pattern, a < b ? 1 : 0, speciesSeed, habitat.GetMoisture(id), habitat.GetNormalizedElevation(id), 0, 0);
                var profile=ForestSpeciesProfile.For(species);
                float age=(float)gameplay.Evaluate("forest.initialAge", (int)pattern, profile.MatureAgeYears, Hash(x/4,y/4,5));
                float health=(float)gameplay.Evaluate("forest.initialHealth", (int)pattern, Hash(x,y,6), 0);
                float biomass=(float)gameplay.Evaluate("forest.initialBiomass", profile.MaximumBiomass, age, profile.MatureAgeYears, health);
                result[id]=new ForestStand(species,age,biomass,health);
            }

        }
        private static float Ellipse(float x,float y,float cx,float cy,float rx,float ry)
            => MathF.Pow((x-cx)/rx,2)+MathF.Pow((y-cy)/ry,2);
    }
}
