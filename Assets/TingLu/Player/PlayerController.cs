using System;
using System.Collections.Generic;
using TingLu.Contracts;
using TingLu.Config;

namespace TingLu.Player
{
    /// <summary>Simulation-time player rules. Input, clock, events and tuning are supplied by the caller.</summary>
    public sealed class PlayerController : IPlayerController
    {
        private readonly GameplayConfig config;
        private readonly IGameClock clock;
        private readonly IEventBus events;
        private readonly IWorldQuery world;
        private readonly string levelId;
        private readonly Queue<double> taps = new Queue<double>();
        private readonly Dictionary<string, Contact> contacts = new Dictionary<string, Contact>();
        private PlayerState current;
        private double stepDistance;

        private sealed class Contact { public bool Touching; public double LastHit; public double LeftAt; }

        public PlayerState Current { get { return current; } }
        public PlayerController(GameplayConfig tuning, IGameClock gameClock, IEventBus eventBus,
            IWorldQuery worldQuery, string level, PlayerState initial)
        {
            if (tuning == null || gameClock == null || eventBus == null || worldQuery == null || initial == null) throw new ArgumentNullException();
            tuning.Validate(); config = tuning; clock = gameClock; events = eventBus; world = worldQuery;
            levelId = ContractGuard.Id(level); current = initial;
        }

        public void Tick(InputFrame input, double delta)
        {
            ContractGuard.Time(delta);
            if (clock.Paused || delta == 0) return;
            double now = clock.Now;
            PruneTaps(now);
            if (current.MainState == PlayerMainState.Stunned && now >= current.StunnedUntil)
                current = Make(PlayerMainState.Active, current.Mode, current.Position, current.Facing, current.Focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);
            if (current.MainState != PlayerMainState.Active) return;
            if (now < current.StunnedUntil) return;

            float focus = current.Focus;
            bool moving = Length(input.Move) > 0.001f;
            ActionMode mode = input.ListenHeld ? ActionMode.Listening : ActionMode.Walking;
            float speed = input.ListenHeld ? config.listenSpeed : config.walkSpeed;
            float rate = moving ? speed : 0;
            float mx = input.Move.X, my = input.Move.Y;
            float length = (float)Math.Sqrt(mx * mx + my * my);
            if (length > 1) { mx /= length; my /= length; }
            if (rate > 0)
            {
                Point2 facing = Normalize(mx, my);
                Point2 next = new Point2(current.Position.X + facing.X * rate * (float)delta,
                                         current.Position.Y + facing.Y * rate * (float)delta);
                // Static contours are authoritative for movement too. Stop at the first
                // crossed edge; footstep distance follows the distance actually travelled.
                Point2 before = current.Position;
                next = StopAtFirstContour(before, next);
                float travelled = Distance(before, next);
                current = Make(current.MainState, mode, next, facing, focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);
                stepDistance += travelled;
                while (stepDistance >= 0.72)
                {
                    stepDistance -= 0.72;
                    events.Publish(Event("step", now, SemanticType.Footstep, null, current.Position,
                        FeedbackPriority.Ambience, new FootstepPayload(world.MaterialAt(current.Position))));
                }
            }
            focus = Clamp(focus + (input.ListenHeld ? config.focusListenGain : (moving ? -config.focusWalkDrain : 0)) * (float)delta, 0, 100);
            current = Make(current.MainState, mode, current.Position, current.Facing, focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);

            if (input.TapPressed && now >= current.CaneReadyAt)
            {
                taps.Enqueue(now);
                current = Make(current.MainState, current.Mode, current.Position, current.Facing,
                    Clamp(current.Focus - config.caneFocusCost, 0, 100), now + config.caneCooldown,
                    current.StunnedUntil, current.ConfusedUntil);
                while (taps.Count > config.confusionTapCount) taps.Dequeue();
                double confusedUntil = current.ConfusedUntil;
                if (taps.Count >= config.confusionTapCount && now - OldestTap() < config.confusionWindow)
                    confusedUntil = Math.Max(confusedUntil, now + config.confusionSeconds);
                current = Make(current.MainState, current.Mode, current.Position, current.Facing,
                    current.Focus, current.CaneReadyAt, current.StunnedUntil, confusedUntil);
                events.Publish(Event("tap", now, SemanticType.CaneTap, null, current.Position,
                    FeedbackPriority.Navigation, new CanePayload(config.caneRadius)));
            }
        }

        public bool NotifyCollision(string objectId, float damage, double stunSeconds, Point2 contactPoint)
        {
            if (clock.Paused || current.MainState != PlayerMainState.Active) return false;
            ContractGuard.Id(objectId);
            Contact contact;
            if (!contacts.TryGetValue(objectId, out contact)) { contact = new Contact { LastHit = -1000, LeftAt = -1000 }; contacts.Add(objectId, contact); }
            double now = clock.Now;
            if (contact.Touching || (now - contact.LeftAt < config.contactExitSeconds) || (now - contact.LastHit < config.contactDebounceSeconds))
            { contact.Touching = true; return false; }
            contact.Touching = true; contact.LastHit = now;
            double stunnedUntil = Math.Max(current.StunnedUntil, now + stunSeconds);
            float courage = Clamp(current.Courage - damage, 0, 100);
            current = Make(courage <= 0 ? PlayerMainState.Failing : PlayerMainState.Stunned, current.Mode,
                current.Position, current.Facing, current.Focus, current.CaneReadyAt, stunnedUntil, current.ConfusedUntil, courage);
            events.Publish(Event("collision", now, SemanticType.Collision, objectId, contactPoint,
                FeedbackPriority.CriticalThreat, new CollisionPayload(damage, stunSeconds, contactPoint)));
            return true;
        }

        public void SetContact(string objectId, bool touching)
        {
            ContractGuard.Id(objectId); Contact contact;
            if (!contacts.TryGetValue(objectId, out contact)) { contact = new Contact { LastHit = -1000, LeftAt = -1000 }; contacts.Add(objectId, contact); }
            if (contact.Touching && !touching) contact.LeftAt = clock.Now;
            contact.Touching = touching;
        }

        public void SetMainState(PlayerMainState state)
        { current = Make(state, current.Mode, current.Position, current.Facing, current.Focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil); }

        public void Restore(PlayerState state, double capturedAt, double now)
        {
            if (state == null) throw new ArgumentNullException("state");
            ContractGuard.Time(capturedAt); ContractGuard.Time(now);
            double elapsed = Math.Max(0, now - capturedAt);
            current = Make(PlayerMainState.Active, ActionMode.Walking, state.Position, state.Facing,
                state.Focus, Math.Max(now, state.CaneReadyAt - capturedAt + now),
                now + Math.Max(0, state.StunnedUntil - capturedAt),
                Math.Max(now, state.ConfusedUntil - capturedAt + now), state.Courage);
            contacts.Clear(); taps.Clear(); stepDistance = 0;
        }

        public void AddCourage(float value)
        { current = Make(current.MainState, current.Mode, current.Position, current.Facing, current.Focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil, Clamp(current.Courage + value, 0, 100)); }
        public void AddFocus(float value)
        { current = Make(current.MainState, current.Mode, current.Position, current.Facing, Clamp(current.Focus + value, 0, 100), current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil); }
        public void SetPosition(Point2 position) { current = Make(current.MainState,current.Mode,position,current.Facing,current.Focus,current.CaneReadyAt,current.StunnedUntil,current.ConfusedUntil); }

        private void PruneTaps(double now) { while (taps.Count > 0 && now - taps.Peek() >= config.confusionWindow) taps.Dequeue(); }
        private double OldestTap() { return taps.Count == 0 ? 0 : taps.Peek(); }
        private GameplayEvent Event(string prefix, double time, SemanticType type, string objectId, Point2? position, FeedbackPriority priority, EventPayload payload)
        { return new GameplayEvent(Guid.NewGuid().ToString("N"), time, levelId, objectId, position, type, priority, payload); }
        private static float Length(Point2 p) { return (float)Math.Sqrt(p.X*p.X+p.Y*p.Y); }
        private Point2 StopAtFirstContour(Point2 from, Point2 to)
        {
            IWorldMovement movement=world as IWorldMovement;
            if(movement!=null)return movement.ResolveMovement(from,to,0.18f).Position;
            IReadOnlyList<WorldObjectState> nearby = world.QueryNearby(from, Distance(from, to) + 1.5f);
            float best = 1f;
            foreach (WorldObjectState item in nearby)
            {
                if (item.Definition.Category != ObjectCategory.Static) continue;
                var contour = item.Definition.LocalContour;
                for (int i = 0; i < contour.Count; i++)
                {
                    Point2 a = Transform(contour[i].A, item.Position, item.RotationDegrees);
                    Point2 b = Transform(contour[i].B, item.Position, item.RotationDegrees);
                    float t;
                    if (SegmentHit(from, to, a, b, out t) && t < best) best = t;
                }
            }
            if (best >= 1f) return to;
            float safe = Math.Max(0, best - 0.015f);
            return new Point2(from.X + (to.X-from.X)*safe, from.Y + (to.Y-from.Y)*safe);
        }
        private static Point2 Transform(Point2 local, Point2 origin, float degrees)
        { double r=degrees*Math.PI/180.0; return new Point2(origin.X+local.X*(float)Math.Cos(r)-local.Y*(float)Math.Sin(r),origin.Y+local.X*(float)Math.Sin(r)+local.Y*(float)Math.Cos(r)); }
        private static bool SegmentHit(Point2 a,Point2 b,Point2 c,Point2 d,out float t)
        {
            float rx=b.X-a.X,ry=b.Y-a.Y,sx=d.X-c.X,sy=d.Y-c.Y,den=rx*sy-ry*sx;
            if(Math.Abs(den)<0.000001f){t=0;return false;}
            float qx=c.X-a.X,qy=c.Y-a.Y;float u=(qx*ry-qy*rx)/den;t=(qx*sy-qy*sx)/den;
            return t>=0&&t<=1&&u>=0&&u<=1;
        }
        private static float Distance(Point2 a,Point2 b) { float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y); }
        private static Point2 Normalize(float x,float y) { float l=(float)Math.Sqrt(x*x+y*y); return l<0.0001f ? new Point2(0,1) : new Point2(x/l,y/l); }
        private static float Clamp(float v,float min,float max) { return Math.Max(min,Math.Min(max,v)); }
        private PlayerState Make(PlayerMainState state,ActionMode mode,Point2 p,Point2 f,float focus,double ready,double stunned,double confused)
        { return Make(state,mode,p,f,focus,ready,stunned,confused,current == null ? 100 : current.Courage); }
        private PlayerState Make(PlayerMainState state,ActionMode mode,Point2 p,Point2 f,float focus,double ready,double stunned,double confused,float courage)
        { return new PlayerState(state,mode,p,f,courage,Clamp(focus,0,100),Math.Max(0,ready),Math.Max(0,stunned),Math.Max(0,confused)); }
    }
}
