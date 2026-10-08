using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    internal readonly record struct StockHandle(StockFlowNetwork Owner, int Index);
    internal readonly record struct StockFlowBalance(double Before, double After, double Inflow, double Outflow)
    {
        internal double Error => (After - Before) - (Inflow - Outflow);
    }

    /// <summary>
    /// A homogeneous-unit stock/flow network. Rates read the start-of-step stocks; requests are
    /// committed together. Competing outflows share supply proportionally; inflows share free capacity.
    /// Incoming stock cannot be spent until the next step. No silent creation or destruction of stock.
    /// </summary>
    internal sealed class StockFlowNetwork
    {
        private readonly record struct Request(int Source, int Destination, double Amount);
        private readonly List<string> names = new();
        private readonly List<double> capacities = new();
        private readonly List<Request> requests = new(64);
        private double[] values = Array.Empty<double>(), next, demand, incoming, supplyLeft, roomLeft;
        private double dt;
        private bool sealedNetwork, active;
        internal string Unit { get; }
        internal int Count => names.Count;
        // Borrowed snapshot: valid until AddStock or the next successful Commit.
        internal ReadOnlySpan<double> Stocks => values.AsSpan(0, Count);

        internal StockFlowNetwork(string unit)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(unit);
            Unit = unit;
        }

        internal StockHandle AddStock(string name, double quantity, double capacity = double.PositiveInfinity)
        {
            if (sealedNetwork) throw new InvalidOperationException("Stocks must be declared before the first step.");
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (names.Contains(name)) throw new ArgumentException($"Duplicate stock '{name}'.", nameof(name));
            if (!double.IsFinite(quantity) || quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (double.IsNaN(capacity) || capacity < quantity) throw new ArgumentOutOfRangeException(nameof(capacity));
            int id = names.Count;
            if (id == values.Length) Array.Resize(ref values, Math.Max(4, checked(id * 2)));
            values[id] = quantity;
            names.Add(name); capacities.Add(capacity);
            return new(this, id);
        }

        internal double Quantity(StockHandle stock) => values[Index(stock)];

        internal void BeginStep(double seconds)
        {
            if (active) throw new InvalidOperationException("Commit or cancel the active stock/flow step first.");
            if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!sealedNetwork)
            {
                if (Count == 0) throw new InvalidOperationException("The network has no stocks.");
                next = new double[Count]; demand = new double[Count]; incoming = new double[Count];
                supplyLeft = new double[Count]; roomLeft = new double[Count];
                sealedNetwork = true;
            }
            dt = seconds; requests.Clear(); active = true;
        }

        internal void Transfer(StockHandle source, StockHandle destination, double ratePerSecond)
        {
            int from = Index(source), to = Index(destination);
            if (from == to) throw new ArgumentException("A transfer needs two different stocks.");
            RequestFlow(from, to, ratePerSecond);
        }
        internal void Inflow(StockHandle destination, double ratePerSecond) => RequestFlow(-1, Index(destination), ratePerSecond);
        internal void Outflow(StockHandle source, double ratePerSecond) => RequestFlow(Index(source), -1, ratePerSecond);

        private void RequestFlow(int source, int destination, double rate)
        {
            if (!active) throw new InvalidOperationException("Begin a stock/flow step before requesting flows.");
            if (!double.IsFinite(rate) || rate < 0 || !double.IsFinite(rate * dt)) throw new ArgumentOutOfRangeException(nameof(rate));
            if (rate != 0) requests.Add(new(source, destination, rate * dt));
        }

        internal StockFlowBalance Commit()
        {
            if (!active) throw new InvalidOperationException("There is no active stock/flow step.");
            Array.Clear(demand); Array.Clear(incoming);
            foreach (var request in requests)
                if (request.Source >= 0)
                {
                    double total = demand[request.Source] + request.Amount;
                    if (!double.IsFinite(total)) throw new InvalidOperationException("Combined flow demand overflowed.");
                    demand[request.Source] = total;
                }
            foreach (var request in requests)
                if (request.Destination >= 0)
                {
                    double amount = Supplied(request);
                    double total = incoming[request.Destination] + amount;
                    if (!double.IsFinite(total)) throw new InvalidOperationException("Combined inflow overflowed.");
                    incoming[request.Destination] = total;
                }
            double before = 0, inflow = 0, outflow = 0;
            for (int id = 0; id < Count; id++)
            {
                supplyLeft[id] = values[id];
                next[id] = 0; // Accumulate receipts separately from remaining start-of-step supply.
                roomLeft[id] = capacities[id] - values[id];
                before += values[id];
            }
            foreach (var request in requests)
            {
                double amount = Supplied(request);
                if (request.Destination >= 0 && incoming[request.Destination] > roomLeftAtStart(request.Destination))
                    amount *= roomLeftAtStart(request.Destination) / incoming[request.Destination];
                if (request.Source >= 0) amount = Math.Min(amount, supplyLeft[request.Source]);
                if (request.Destination >= 0) amount = Math.Min(amount, roomLeft[request.Destination]);
                if (request.Source >= 0) supplyLeft[request.Source] -= amount;
                else inflow += amount;
                if (request.Destination >= 0) { roomLeft[request.Destination] -= amount; next[request.Destination] += amount; }
                else outflow += amount;
            }
            double after = 0;
            for (int id = 0; id < Count; id++)
            {
                next[id] += supplyLeft[id];
                // Independently rounded receipt sums can exceed a finite capacity by a few ulps.
                // Only that roundoff is bounded; its residual remains visible in Balance.Error.
                if (next[id] > capacities[id] && next[id] - capacities[id] <=
                    2.220446049250313e-16 * Math.Max(1, capacities[id]) * Math.Max(1, requests.Count) * 4)
                    next[id] = capacities[id];
                if (!double.IsFinite(next[id]) || next[id] < 0 || next[id] > capacities[id])
                    throw new InvalidOperationException($"Invalid stock balance for '{names[id]}'.");
                after += next[id];
            }
            if (!double.IsFinite(before) || !double.IsFinite(after) || !double.IsFinite(inflow) || !double.IsFinite(outflow))
                throw new InvalidOperationException("Aggregate stock balance overflowed.");
            // Publish only after validating every stock. A failed commit can be cancelled without state loss.
            (values, next) = (next, values);
            active = false;
            return new(before, after, inflow, outflow);

            double roomLeftAtStart(int id) => capacities[id] - values[id];
        }

        internal void CancelStep() { requests.Clear(); active = false; }

        private double Supplied(Request request) => request.Source >= 0 && demand[request.Source] > values[request.Source]
            ? request.Amount * (values[request.Source] / demand[request.Source]) : request.Amount;

        private int Index(StockHandle stock)
        {
            if (!ReferenceEquals(stock.Owner, this) || (uint)stock.Index >= (uint)Count)
                throw new ArgumentException("Stock belongs to another network or is invalid.", nameof(stock));
            return stock.Index;
        }
    }
}
