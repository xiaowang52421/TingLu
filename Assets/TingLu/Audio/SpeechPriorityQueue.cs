using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Audio
{
    /// <summary>Stable priority order for short menu speech requests not yet sent to the provider.</summary>
    public sealed class SpeechPriorityQueue
    {
        private readonly List<SpeechRequest> requests = new List<SpeechRequest>();
        public int Count { get { return requests.Count; } }

        public SpeechRequest[] Enqueue(SpeechRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            SpeechRequest[] replaced = RemoveSupersession(request.SupersessionKey);
            requests.Add(request);
            return replaced;
        }

        public SpeechRequest Dequeue()
        {
            if (requests.Count == 0) return null;
            int best = 0;
            for (int i = 1; i < requests.Count; i++)
                if ((int)requests[i].Priority > (int)requests[best].Priority) best = i;
            SpeechRequest result = requests[best];
            requests.RemoveAt(best);
            return result;
        }

        public SpeechRequest Peek()
        {
            if (requests.Count == 0) return null;
            int best = 0;
            for (int i = 1; i < requests.Count; i++)
                if ((int)requests[i].Priority > (int)requests[best].Priority) best = i;
            return requests[best];
        }

        public SpeechRequest Peek(FeedbackPriority priority)
        {
            SpeechRequest best = null;
            for (int i = 0; i < requests.Count; i++)
                if (requests[i].Priority == priority && best == null) best = requests[i];
            return best;
        }

        public SpeechRequest[] RemoveEvent(string eventId)
        { return Remove(item => item.EventId == eventId); }

        public SpeechRequest[] RemoveSupersession(string key)
        {
            if (string.IsNullOrEmpty(key)) return new SpeechRequest[0];
            return Remove(item => item.SupersessionKey == key);
        }

        public SpeechRequest[] Clear()
        { SpeechRequest[] result = requests.ToArray(); requests.Clear(); return result; }

        public SpeechRequest[] RemoveWhere(Predicate<SpeechRequest> match)
        {
            if (match == null) throw new ArgumentNullException("match");
            return Remove(match);
        }

        private SpeechRequest[] Remove(Predicate<SpeechRequest> match)
        {
            var removed = new List<SpeechRequest>();
            for (int i = requests.Count - 1; i >= 0; i--)
                if (match(requests[i])) { removed.Add(requests[i]); requests.RemoveAt(i); }
            return removed.ToArray();
        }
    }
}
