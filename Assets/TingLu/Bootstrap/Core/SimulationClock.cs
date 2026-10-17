using System;
using TingLu.Contracts;
namespace TingLu.Bootstrap
{
    public sealed class SimulationClock : IGameClock
    {
        public double Now { get; private set; }
        public bool Paused { get; private set; }
        public void SetPaused(bool paused) { Paused = paused; }
        public void Reset(double time = 0)
        {
            if (!Paused) throw new System.InvalidOperationException("Freeze simulation before resetting its timeline.");
            Now = ContractGuard.Time(time);
        }
        public void Advance(double delta)
        {
            ContractGuard.Time(delta);
            if (!Paused) Now += delta;
        }
    }
}
