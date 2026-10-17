using System;
using System.Collections.Generic;
using TingLu.Contracts;
namespace TingLu.Bootstrap
{
    // Main-thread only. FIFO delivery; reentrant publication is queued until all consumers finish.
    public sealed class EventBus : IEventBus, IDisposable
    {
        private readonly List<Action<GameplayEvent>> consumers = new List<Action<GameplayEvent>>();
        private readonly Queue<GameplayEvent> pending = new Queue<GameplayEvent>();
        private bool publishing, disposed;
        public IDisposable Subscribe(Action<GameplayEvent> consumer)
        {
            if (disposed) throw new ObjectDisposedException("EventBus");
            if (consumer == null) throw new ArgumentNullException("consumer");
            consumers.Add(consumer);
            return new Subscription(() => consumers.Remove(consumer));
        }
        public void Publish(GameplayEvent value)
        {
            if (disposed) throw new ObjectDisposedException("EventBus");
            if (value == null) throw new ArgumentNullException("value");
            pending.Enqueue(value);
            if (publishing) return;
            publishing = true;
            var errors = new List<Exception>();
            try
            {
                while (pending.Count > 0 && !disposed)
                {
                    var next = pending.Dequeue();
                    foreach (var consumer in consumers.ToArray())
                    { try { consumer(next); } catch (Exception error) { errors.Add(error); } }
                }
            }
            finally { publishing = false; }
            if (errors.Count > 0) throw new AggregateException("Gameplay consumers failed", errors);
        }
        public void Dispose() { disposed = true; pending.Clear(); consumers.Clear(); }
        private sealed class Subscription : IDisposable
        {
            private Action unsubscribe;
            public Subscription(Action value) { unsubscribe = value; }
            public void Dispose() { var action = unsubscribe; unsubscribe = null; if (action != null) action(); }
        }
    }
}
