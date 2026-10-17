using System;
using System.Collections.Generic;
using ForesTycoon.Rules;

namespace ForesTycoon.Ecology;

internal sealed partial class EnvironmentSystem
{
    private readonly SlicedQueryBatch climateQueries = new();
    private EcologyResponseProgram[] climateWorkers;
    private EcologyResponseProgram climateWorkerSource;
    private ClimateCell[] preparedClimate;
    private double[] potentialEvaporation, preparedEvaporation;
    private ulong[] evaporationStamp;
    private string[] localClimateErrors, evaporationErrors, preparedClimateErrors, preparedEvaporationErrors;

    internal double PotentialEvaporation(int id)
    {
        if ((uint)id >= (uint)CellCount) return 0;
        potentialEvaporation ??= new double[CellCount]; evaporationStamp ??= new ulong[CellCount];
        if (evaporationStamp[id] != localClimateEpoch) {
            potentialEvaporation[id] = LocalClimate(id).Forcing.Evaporation(Responses);
            evaporationStamp[id] = localClimateEpoch;
            if (evaporationErrors != null) evaporationErrors[id] = null;
        }
        Responses.PublishQueryError(localClimateErrors?[id]);
        Responses.PublishQueryError(evaporationErrors?[id]);
        return potentialEvaporation[id];
    }

    // Pure project queries only; no water writes or global accumulation happens in a worker.
    // Private result buffers keep UI reads isolated. Error publication follows the actual field-read order.
    internal IEnumerable<int> PrepareRuleClimateWork(bool evaporation)
    {
        int count = SlicedQueryBatch.WorkerCount;
        if (count == 1 || CellCount < 16384) yield break;
        localClimate ??= new ClimateCell[CellCount]; yield return 1;
        localClimateStamp ??= new ulong[CellCount]; yield return 1;
        preparedClimate ??= new ClimateCell[CellCount]; yield return 1;
        localClimateErrors ??= new string[CellCount]; preparedClimateErrors ??= new string[CellCount]; yield return 1;
        if (evaporation) {
            potentialEvaporation ??= new double[CellCount]; evaporationStamp ??= new ulong[CellCount]; yield return 1;
            preparedEvaporation ??= new double[CellCount]; yield return 1;
            evaporationErrors ??= new string[CellCount]; preparedEvaporationErrors ??= new string[CellCount]; yield return 1;
        }
        var source = Responses;
        if (!ReferenceEquals(climateWorkerSource, source)) {
            climateWorkers = new EcologyResponseProgram[count];
            for (int i = 0; i < count; i++) { climateWorkers[i] = source.CreateQueryWorker(); yield return 1; }
            climateWorkerSource = source;
        }
        var forcing = new WeatherForcing(weather.Radiation, weather.Temperature, weather.Humidity, weather.Wind);
        ulong epoch = localClimateEpoch;
        const int perWorker = 512;
        for (int first = 0; first < CellCount; first += perWorker * count) {
            int batchStart = first, end = Math.Min(CellCount, first + perWorker * count);
            foreach (int unit in climateQueries.RunWork(count, worker => {
                var queries = climateWorkers[worker];
                for (int id = batchStart + worker * perWorker; id < Math.Min(end, batchStart + (worker + 1) * perWorker); id++) {
                    queries.ClearQueryError();
                    var climate = Climate.Cell(id, forcing, queries);
                    preparedClimate[id] = climate; preparedClimateErrors[id] = queries.LastError;
                    if (evaporation) {
                        queries.ClearQueryError(); preparedEvaporation[id] = climate.Forcing.Evaporation(queries);
                        preparedEvaporationErrors[id] = queries.LastError;
                    }
                }
            })) yield return unit;
            for (int id = first; id < end; id++) {
                localClimate[id] = preparedClimate[id]; localClimateErrors[id] = preparedClimateErrors[id];
                localClimateStamp[id] = epoch;
                if (evaporation) {
                    potentialEvaporation[id] = preparedEvaporation[id]; evaporationErrors[id] = preparedEvaporationErrors[id];
                    evaporationStamp[id] = epoch;
                }
                yield return 1;
            }
        }
    }
    internal IEnumerable<int> FinishQueryWork() => climateQueries.JoinWork();
}
