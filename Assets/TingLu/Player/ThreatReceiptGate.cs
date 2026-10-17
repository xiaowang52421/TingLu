using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Player
{
    public enum ThreatAudioState { Requested, Ready, Started, Cancelled }
    public sealed class ThreatReceiptGate
    {
        private sealed class Pending { public ThreatAudioState State; public double RequestedAt; public int Generation; public Action<double> OnStarted; public Action OnCancelled; }
        private readonly Dictionary<string,Pending> pending=new Dictionary<string,Pending>();
        private readonly Dictionary<string,ThreatAudioState> states=new Dictionary<string,ThreatAudioState>();
        private readonly double timeout;
        public ThreatReceiptGate(double timeoutSeconds){if(timeoutSeconds<=0)throw new ArgumentOutOfRangeException("timeoutSeconds");timeout=timeoutSeconds;}
        public void Request(string eventId,int recoveryGeneration,double now,Action<double> started,Action cancelled)
        {
            ContractGuard.Id(eventId);ContractGuard.Time(now);if(started==null)throw new ArgumentNullException("started");
            pending[eventId]=new Pending{State=ThreatAudioState.Requested,RequestedAt=now,Generation=recoveryGeneration,OnStarted=started,OnCancelled=cancelled};
            states[eventId]=ThreatAudioState.Requested;
        }
        public void Receive(AudioReceipt receipt)
        {
            if(receipt==null)throw new ArgumentNullException("receipt");Pending p;
            if(!pending.TryGetValue(receipt.EventId,out p)||receipt.RecoveryGeneration!=p.Generation)return;
            if(receipt.Status==AudioStatus.Ready){if(p.State==ThreatAudioState.Requested)p.State=ThreatAudioState.Ready;states[receipt.EventId]=p.State;return;}
            if(receipt.Status==AudioStatus.Started){if(p.State==ThreatAudioState.Cancelled)return;p.State=ThreatAudioState.Started;states[receipt.EventId]=p.State;pending.Remove(receipt.EventId);p.OnStarted(receipt.SimulationTime);return;}
            if(receipt.Status==AudioStatus.Failed||receipt.Status==AudioStatus.Stopped)Cancel(receipt.EventId,p);
        }
        public void Tick(double now){ContractGuard.Time(now);var expired=new List<string>();foreach(var pair in pending)if(pair.Value.State!=ThreatAudioState.Cancelled&&now-pair.Value.RequestedAt>=timeout)expired.Add(pair.Key);foreach(string id in expired){Pending p=pending[id];Cancel(id,p);}}
        public void Reset(int recoveryGeneration){var ids=new List<string>();foreach(var pair in pending)if(pair.Value.Generation!=recoveryGeneration)ids.Add(pair.Key);foreach(string id in ids)Cancel(id,pending[id]);}
        public ThreatAudioState? StateOf(string eventId){ThreatAudioState s;return states.TryGetValue(eventId,out s)?(ThreatAudioState?)s:null;}
        private void Cancel(string id,Pending p){p.State=ThreatAudioState.Cancelled;states[id]=p.State;pending.Remove(id);if(p.OnCancelled!=null)p.OnCancelled();}
    }
}
