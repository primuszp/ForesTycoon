using System;
namespace ForesTycoon.Ecology
{
    internal static class LargeForestGenerator
    {
        internal static ForestStand[] Create(IForestHabitat habitat,int columns,int rows,ForestPattern pattern)
        {
            if(columns*rows!=habitat.TileCount) throw new ArgumentException("Incorrect forest grid.");
            var result=new ForestStand[habitat.TileCount];
            float Hash(int x,int y,int salt){
                uint h=unchecked((uint)(habitat.Seed+x*374761393+y*668265263+salt*1442695041));
                h=(h^(h>>13))*1274126177; return (h^(h>>16))/(float)uint.MaxValue;
            }
            float shiftX=(Hash(0,0,1)-0.5f)*0.1f, shiftY=(Hash(0,0,2)-0.5f)*0.1f;
            for(int x=0;x<columns;x++) for(int y=0;y<rows;y++){
                int id=x*rows+y;
                if(!habitat.CanSupportForest(id)) continue;
                float u=(x+0.5f)/columns, v=(y+0.5f)/rows;
                bool mixed=pattern==ForestPattern.LargeMixed;
                float a=Ellipse(u,v,0.28f+shiftX,0.34f+shiftY,mixed?0.31f:0.43f,mixed?0.36f:0.44f);
                float b=Ellipse(u,v,0.7f-shiftX,0.66f-shiftY,mixed?0.32f:0.43f,mixed?0.35f:0.44f);
                if(Math.Min(a,b)>0.93f+Hash(x/3,y/3,3)*0.2f) continue;
                bool spruce=pattern==ForestPattern.LargeSpruce || (mixed && a<b);
                float speciesSeed=Hash(x/6,y/6,4);
                ForestSpecies species=spruce?ForestSpecies.Spruce:
                    speciesSeed<0.65f?ForestSpecies.Beech:speciesSeed<0.9f?ForestSpecies.Oak:ForestSpecies.Birch;
                var profile=ForestSpeciesProfile.For(species);
                float age=profile.MatureAgeYears*(0.8f+Hash(x/4,y/4,5)*0.65f);
                float health=0.8f+Hash(x,y,6)*0.18f;
                float biomass=profile.MaximumBiomass*Math.Clamp(age/profile.MatureAgeYears,0,1)*health;
                result[id]=new ForestStand(species,age,biomass,health);
            }
            return result;
        }
        private static float Ellipse(float x,float y,float cx,float cy,float rx,float ry)
            => MathF.Pow((x-cx)/rx,2)+MathF.Pow((y-cy)/ry,2);
    }
}
