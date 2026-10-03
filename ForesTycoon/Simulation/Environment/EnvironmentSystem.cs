using System;

namespace ForesTycoon
{
    internal readonly record struct EnvironmentCell(double Canopy, double Surface, double Soil, double Deep,
        double Drought, double Waterlogging, double GrowthFactor);

    // Version 1: bounded fixed-step bucket hydrology. All water stores are mm over a cell.
    internal sealed class EnvironmentSystem
    {
        internal const double SecondsPerForestYear = 1200;
        internal const double HoursPerSecond = 1.0 / 60;
        internal const double StepSeconds = 0.5;
        private readonly IForestHabitat habitat;
        private readonly ForestSystem forest;
        private readonly double[] canopy, surface, soil, deep, drought, wet, dryIntegral, wetIntegral, transfer;
        private readonly int[] destinations;
        private double remainder, monthSeconds;
        private uint random;
        
        internal double Time { get; private set; }
        internal double EventStart { get; private set; }
        internal double EventEnd { get; private set; }
        internal WeatherPreset Preset { get; private set; }
        internal double PeakRain { get; private set; }
        internal double RainRate => RateAt(Time);
        internal double EventRain { get; private set; }
        internal double ExpectedEventRain => PeakRain*((EventEnd-EventStart)-Math.Min(10,(EventEnd-EventStart)*0.2))*HoursPerSecond;
        internal double TotalRain { get; private set; }
        internal double Evaporated { get; private set; }
        internal double Outflow { get; private set; }
        internal double InitialWater { get; }
        internal ulong Revision { get; private set; }
        internal int CellCount => soil.Length;
        internal double Temperature => 12 + 9 * Math.Sin(2 * Math.PI * Time / SecondsPerForestYear);
        internal double RelativeHumidity => Preset is WeatherPreset.Rain or WeatherPreset.Storm ? 0.9 : 0.55;
        internal double WindSpeed => 1.5+(Preset==WeatherPreset.Storm?8.5:Preset==WeatherPreset.Rain?1.5:0)*Math.Clamp(RainRate/Math.Max(1,PeakRain),0,1);
        private double previousCloud;
        internal double Cloud {
            get { double target=Preset==WeatherPreset.Sunny?0.12:Preset==WeatherPreset.Cloudy?0.65:1;
                double t=Math.Clamp((Time-EventStart)/10,0,1);t=t*t*(3-2*t);return previousCloud+(target-previousCloud)*t; }
        }
        internal double MeanWetness { get; private set; }
        internal double MeanSoil { get; private set; }
        internal double StoredWater { get { double total=0;for(int i=0;i<CellCount;i++)total+=canopy[i]+surface[i]+soil[i]+deep[i];return total; } }
        internal double BalanceError => StoredWater - (InitialWater + TotalRain*CellCount - Evaporated - Outflow);

        internal EnvironmentSystem(IForestHabitat habitat, ForestSystem forest)
        {
            this.habitat=habitat;this.forest=forest;
            int n=habitat.TileCount;
            canopy=new double[n];surface=new double[n];soil=new double[n];deep=new double[n];drought=new double[n];wet=new double[n];
            dryIntegral=new double[n];wetIntegral=new double[n];transfer=new double[n];destinations=new int[n];
            for(int i=0;i<n;i++){soil[i]=45+100*habitat.GetMoisture(i);deep[i]=20;}
            InitialWater=StoredWater;random=unchecked((uint)habitat.Seed)^0x73A94F21u;if(random==0)random=1;
            StartEvent(WeatherPreset.Sunny,150,0);RefreshRouting();Summarize();
        }
        private double Random() { random^=random<<13;random^=random>>17;random^=random<<5;return random/4294967296.0; }
        private void StartEvent(WeatherPreset preset,double duration,double peak)
        { previousCloud=Cloud;Preset=preset;EventStart=Time;EventEnd=Time+duration;PeakRain=peak;EventRain=0; }
        internal void ForceWeather(WeatherPreset preset,int peak=-1,int duration=-1)
        {
            if(preset==WeatherPreset.Snow||!Enum.IsDefined(preset))throw new ArgumentOutOfRangeException(nameof(preset));
            if(peak < -1 || peak>100 || duration < -1 || (duration!=-1&&duration<20) || duration>600)throw new ArgumentOutOfRangeException(nameof(peak));
            StartEvent(preset,duration<0?(preset==WeatherPreset.Storm?45:preset==WeatherPreset.Rain?90:180):duration,
                preset is WeatherPreset.Rain or WeatherPreset.Storm ? (peak<0?(preset==WeatherPreset.Storm?32:12):peak):0);
        }
        private void NextEvent()
        {
            double r=Random();WeatherPreset next;
            if(Preset is WeatherPreset.Rain or WeatherPreset.Storm)next=r<0.65?WeatherPreset.Cloudy:WeatherPreset.Sunny;
            else if(Preset==WeatherPreset.Cloudy)next=r<0.15?WeatherPreset.Storm:r<0.65?WeatherPreset.Rain:WeatherPreset.Sunny;
            else next=r<0.75?WeatherPreset.Cloudy:WeatherPreset.Sunny;
            double duration=next switch {WeatherPreset.Sunny=>120+Random()*180,WeatherPreset.Cloudy=>60+Random()*120,
                WeatherPreset.Rain=>45+Random()*75,_=>20+Random()*40};
            StartEvent(next,duration,next==WeatherPreset.Rain?6+Random()*12:next==WeatherPreset.Storm?20+Random()*20:0);
        }
        private double RateAt(double time)
        {
            double ramp=Math.Min(10,(EventEnd-EventStart)*0.2);
            return PeakRain*Math.Clamp(Math.Min((time-EventStart)/ramp,(EventEnd-time)/ramp),0,1);
        }
        internal void Update(double seconds)
        {
            if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
            remainder+=seconds;
            while(remainder+1e-10>=StepSeconds){remainder-=StepSeconds;Advance(StepSeconds);}
        }
        private void Advance(double seconds)
        {
            while(seconds>1e-9){
                if(Time>=EventEnd-1e-9)NextEvent();
                double dt=Math.Min(seconds,EventEnd-Time);
                // Split at the piecewise-linear ramp corners for exact rainfall integration.
                double ramp=Math.Min(10,(EventEnd-EventStart)*0.2);
                if(Time<EventStart+ramp-1e-9)dt=Math.Min(dt,EventStart+ramp-Time);
                else if(Time<EventEnd-ramp-1e-9)dt=Math.Min(dt,EventEnd-ramp-Time);
                if(forest!=null)dt=Math.Min(dt,forest.SecondsUntilMonth);
                double rain=(RateAt(Time)+RateAt(Time+dt))*0.5*dt*HoursPerSecond;
                WaterStep(dt,rain);Time+=dt;seconds-=dt;TotalRain+=rain;EventRain+=rain;
                forest?.Update(dt);
            }
            Revision++;Summarize();
        }
        internal void RefreshRouting()
        {
            Span<int> neighbours=stackalloc int[4];
            for(int i=0;i<CellCount;i++){
                destinations[i]=-1;float low=habitat.GetNormalizedElevation(i);
                int n=habitat.GetAdjacentTileIds(i,neighbours);
                for(int j=0;j<n;j++){int id=neighbours[j];float height=habitat.GetNormalizedElevation(id);
                    if(height<low){low=height;destinations[i]=id;}}
            }
        }
        private void WaterStep(double dt,double rain)
        {
            double hours=dt*HoursPerSecond;
            double demand=(0.12+Math.Max(0,Temperature)*0.015)*(Preset==WeatherPreset.Sunny?1:0.55)*hours;
            monthSeconds+=dt;Array.Clear(transfer);
            for(int i=0;i<CellCount;i++){
                ForestStand stand=default;
                bool wooded=forest!=null&&forest.TryGetStand(i,out stand);
                double cover=wooded?Math.Clamp(stand.Maturity,0.15,1):0;
                double capacity=cover*(wooded&&stand.Species==ForestSpecies.Spruce?2.5:1.5);
                double held=Math.Min(rain,Math.Max(0,capacity-canopy[i]));canopy[i]+=held;surface[i]+=rain-held;
                // Harvested canopy water drips to the ground rather than disappearing.
                double drip=Math.Max(0,canopy[i]-capacity);canopy[i]-=drip;surface[i]+=drip;
                bool road=habitat is Terrain terrain&&terrain.IsRoadTile(i);
                double infiltration=Math.Min(surface[i],Math.Min((road?0.5:12)*hours,Math.Max(0,180-soil[i])));
                surface[i]-=infiltration;soil[i]+=infiltration;
                double budget=demand,loss=Math.Min(canopy[i],budget);canopy[i]-=loss;budget-=loss;Evaporated+=loss;
                loss=Math.Min(surface[i],budget);surface[i]-=loss;budget-=loss;Evaporated+=loss;
                double available=Math.Max(0,soil[i]-25),stress=Math.Clamp(available/55,0,1);
                loss=Math.Min(available,budget*stress*(wooded?1:0.65));soil[i]-=loss;Evaporated+=loss;
                double drainage=Math.Min(Math.Max(0,soil[i]-130),2*hours);soil[i]-=drainage;deep[i]+=drainage;
                double baseflow=Math.Min(deep[i],0.15*hours);deep[i]-=baseflow;Outflow+=baseflow;
                double dry=Math.Clamp((70-soil[i])/45,0,1),waterlogged=Math.Clamp((soil[i]-145)/35,0,1);
                double blend=1-Math.Exp(-dt/90);drought[i]+=(dry-drought[i])*blend;wet[i]+=(waterlogged-wet[i])*blend;
                dryIntegral[i]+=drought[i]*dt;wetIntegral[i]+=wet[i]*dt;
                double runoff=Math.Min(surface[i],Math.Max(0,surface[i]-2)*(1-Math.Exp(-hours*6)));
                int to=destinations[i];
                if(to>=0){transfer[i]-=runoff;transfer[to]+=runoff;}
                else if(habitat is Terrain t&&t.IsEnvironmentWaterOutlet(i)){transfer[i]-=runoff;Outflow+=runoff;}
            }
            for(int i=0;i<CellCount;i++)surface[i]+=transfer[i];
        }
        private void Summarize(){double water=0,root=0;for(int i=0;i<CellCount;i++){water+=Math.Clamp(surface[i]/2+canopy[i]/4,0,1);root+=soil[i]/180;}
            MeanWetness=CellCount>0?water/CellCount:0;MeanSoil=CellCount>0?root/CellCount:0;}
        internal double GrowthFactor(int id,ForestSpecies species)
        {
            if((uint)id>=(uint)CellCount)return 1;
            double d=monthSeconds>0?dryIntegral[id]/monthSeconds:drought[id];
            double w=monthSeconds>0?wetIntegral[id]/monthSeconds:wet[id];
            double drySensitivity=species==ForestSpecies.Oak?0.6:species==ForestSpecies.Spruce?1:0.85;
            return Math.Clamp(1-d*drySensitivity-w*0.65,0.05,1);
        }
        internal void FinishForestMonth(){Array.Clear(dryIntegral);Array.Clear(wetIntegral);monthSeconds=0;}
        internal EnvironmentCell Cell(int id)
        {
            var species=forest!=null&&forest.TryGetStand(id,out var stand)?stand.Species:ForestSpecies.Beech;
            return new(canopy[id],surface[id],soil[id],deep[id],drought[id],wet[id],GrowthFactor(id,species));
        }
    }
}
