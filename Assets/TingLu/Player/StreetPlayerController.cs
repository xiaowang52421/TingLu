using System;
using System.Collections.Generic;
using TingLu.Config;
using TingLu.Contracts;

namespace TingLu.Player
{
    /// <summary>Simulation-time player rules for the street upgrade.</summary>
    public sealed class StreetPlayerController : IPlayerStateReader
    {
        private const float MovementEpsilon = 0.0001f;
        private const float FootstepInterval = 0.72f;
        private const double ContactTimeEpsilon = 0.0000001;

        private sealed class ContactReceipt
        {
            public bool Touching;
            public double LastHit = -1000;
            public double LeftAt = -1000;
        }

        private readonly StreetConfig config;
        private readonly IGameClock clock;
        private readonly IStreetWorld world;
        private readonly Queue<double> caneTaps = new Queue<double>();
        private readonly Dictionary<string, ContactReceipt> contacts = new Dictionary<string, ContactReceipt>(StringComparer.Ordinal);
        private readonly HashSet<string> vehicleContacts = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> contactsThisTick = new HashSet<string>(StringComparer.Ordinal);
        private PlayerState current;
        private double stepDistance;
        private int recoveryGeneration;
        private int failedGeneration = -1;
        private long probeSequence;

        public event Action<StreetProbeRequest> Probed;
        public event Action<StreetContact> Contacted;
        public event Action<StreetFailure, string> Failed;
        public event Action<string, Point2> Stepped;

        public PlayerState Current { get { return current; } }

        public StreetPlayerController(StreetConfig tuning, IGameClock gameClock, IStreetWorld streetWorld, PlayerState initial)
        {
            if (tuning == null || gameClock == null || streetWorld == null || initial == null)
                throw new ArgumentNullException();
            tuning.Validate();
            config = tuning;
            clock = gameClock;
            world = streetWorld;
            ContractGuard.Time(clock.Now);
            current = Copy(initial.MainState, ActionMode.Walking, initial.Position, initial.Facing, initial.Courage,
                initial.Focus, initial.CaneReadyAt, initial.StunnedUntil, initial.ConfusedUntil);
        }

        /// <summary>Advances one player step. The caller advances the world before calling this method.</summary>
        public void Tick(StreetInputFrame input, double delta)
        {
            ContractGuard.Time(delta);
            if (clock.Paused || input.PausePressed || current.MainState == PlayerMainState.Failing ||
                current.MainState == PlayerMainState.Finished)
                return;

            double now = ContractGuard.Time(clock.Now);
            int tickGeneration = recoveryGeneration;
            PruneTaps(now);

            if ((current.MainState == PlayerMainState.Stunned || current.MainState == PlayerMainState.Active) &&
                now < current.StunnedUntil)
            {
                // Stun freezes player actions, not the world's moving vehicle hazards.
                contactsThisTick.Clear();
                IReadOnlyList<StreetContact> stunnedHits = world.VehicleContacts(current.Position, current.Position, config.BodyRadius);
                if (stunnedHits != null)
                {
                    for (int i = 0; i < stunnedHits.Count; i++)
                    {
                        StreetContact contact = stunnedHits[i];
                        if (contact != null && contact.Vehicle && HandleVehicleContact(contact, now, tickGeneration)) return;
                    }
                    for(int i=0;i<stunnedHits.Count;i++)
                    {var contact=stunnedHits[i];if(contact!=null&&!contact.Vehicle&&HandleOrdinaryContact(contact,now,tickGeneration))return;}
                }
                MarkContactExits(now);
                return;
            }

            if (current.MainState == PlayerMainState.Stunned)
            {
                current = Copy(PlayerMainState.Active, ActionMode.Walking, current.Position, current.Facing,
                    current.Courage, current.Focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);
            }
            if (current.MainState != PlayerMainState.Active) return;

            if (float.IsNaN(input.TurnDegrees) || float.IsInfinity(input.TurnDegrees))
                throw new ArgumentOutOfRangeException("input", "Turn must be finite.");
            ValidateFinite(input.Move, "input");

            Point2 from = current.Position;
            Point2 facing = Math.Abs(input.TurnDegrees) > 0
                ? StreetCoordinates.Turn(current.Facing, input.TurnDegrees)
                : current.Facing;
            Point2 direction = StreetCoordinates.ScreenMove(input.Move);
            float moveLength = Length(direction);
            bool moving = moveLength > MovementEpsilon;

            float speed = config.Gameplay.walkSpeed;
            if (now < current.ConfusedUntil) speed *= config.Gameplay.confusionMultiplier;
            double requestedDistance = moving ? speed * delta : 0;
            if (requestedDistance > float.MaxValue || double.IsNaN(requestedDistance) || double.IsInfinity(requestedDistance))
                throw new ArgumentOutOfRangeException("delta", "Player movement exceeds the supported range.");

            Point2 requested = new Point2(from.X + direction.X * (float)requestedDistance,
                from.Y + direction.Y * (float)requestedDistance);
            StreetMoveResult movement = world.Move(from, requested, config.BodyRadius);
            if (movement == null) throw new InvalidOperationException("IStreetWorld.Move returned null.");
            ValidateFinite(movement.Position, "movement.Position");

            current = Copy(PlayerMainState.Active, ActionMode.Walking, movement.Position, facing,
                current.Courage, current.Focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);

            double travelled = StreetGeometry.Distance(from, movement.Position);
            if (travelled > 0)
            {
                stepDistance += travelled;
                while (stepDistance >= FootstepInterval)
                {
                    stepDistance -= FootstepInterval;
                    string material = world.MaterialAt(current.Position);
                    if (material != null)
                    {
                        Action<string, Point2> stepped = Stepped;
                        if (stepped != null) stepped(material, current.Position);
                        if (recoveryGeneration != tickGeneration) return;
                    }
                }
            }

            float focus = Clamp(current.Focus + (moving ? -config.Gameplay.focusWalkDrain : config.Gameplay.focusListenGain) * (float)delta, 0, 100);
            current = Copy(PlayerMainState.Active, ActionMode.Walking, current.Position, current.Facing,
                current.Courage, focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);

            contactsThisTick.Clear();
            var physicalHits = new List<StreetContact>();
            IReadOnlyList<StreetContact> worldHits = world.VehicleContacts(from, current.Position, config.BodyRadius);
            if (worldHits != null) physicalHits.AddRange(worldHits);
            physicalHits.AddRange(movement.Contacts);
            // Aggregate both physical sources first. A dangerous contact resolves the entire
            // simulation step immediately, regardless of lexical object ordering or wall samples.
            for (int i = 0; i < physicalHits.Count; i++)
            {
                StreetContact contact = physicalHits[i];
                if (contact != null && contact.Vehicle && HandleVehicleContact(contact, now, tickGeneration)) return;
            }
            for (int i = 0; i < physicalHits.Count; i++)
            {
                StreetContact contact = physicalHits[i];
                if (contact != null && !contact.Vehicle && HandleOrdinaryContact(contact, now, tickGeneration)) return;
            }

            MarkContactExits(now);

            // Contact resolution precedes an action requested in the same frame. A vehicle
            // failure, courage failure, or ordinary collision stun must not leak a probe.
            if (current.MainState == PlayerMainState.Active && input.ProbePressed && now >= current.CaneReadyAt)
            {
                StreetProbeRequest request = MakeProbe(now);
                Action<StreetProbeRequest> probed = Probed;
                if (probed != null) probed(request);
                if (recoveryGeneration != tickGeneration) return;
            }
        }

        /// <summary>Rebases active timers to the restored simulation timeline and starts a clean contact generation.</summary>
        public void Restore(PlayerState state, double capturedAt, double now)
        {
            if (state == null) throw new ArgumentNullException("state");
            ContractGuard.Time(capturedAt);
            ContractGuard.Time(now);
            current = Copy(PlayerMainState.Active, ActionMode.Walking, state.Position, state.Facing,
                state.Courage, state.Focus,
                Math.Max(now, state.CaneReadyAt - capturedAt + now),
                now + Math.Max(0, state.StunnedUntil - capturedAt),
                Math.Max(now, state.ConfusedUntil - capturedAt + now));
            contacts.Clear();
            vehicleContacts.Clear();
            caneTaps.Clear();
            stepDistance = 0;
            recoveryGeneration++;
            failedGeneration = -1;
        }

        public void SetFinished()
        {
            current = Copy(PlayerMainState.Finished, ActionMode.Walking, current.Position, current.Facing,
                current.Courage, current.Focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);
        }

        private StreetProbeRequest MakeProbe(double now)
        {
            float angle = config.ProbeAngle;
            float radius = config.Gameplay.caneRadius;
            if (current.Focus < config.Gameplay.lowFocusThreshold)
            {
                angle = config.Gameplay.lowFocusAngle;
                radius = config.Gameplay.lowFocusRadius;
            }

            caneTaps.Enqueue(now);
            while (caneTaps.Count > config.Gameplay.confusionTapCount) caneTaps.Dequeue();
            double confusedUntil = current.ConfusedUntil;
            if (caneTaps.Count >= config.Gameplay.confusionTapCount && now - OldestTap() < config.Gameplay.confusionWindow)
                confusedUntil = Math.Max(confusedUntil, now + config.Gameplay.confusionSeconds);

            current = Copy(PlayerMainState.Active, ActionMode.Walking, current.Position, current.Facing,
                current.Courage, Clamp(current.Focus - config.Gameplay.caneFocusCost, 0, 100),
                now + config.Gameplay.caneCooldown, current.StunnedUntil, confusedUntil);

            string id = "street-probe-" + (++probeSequence).ToString("D10");
            return new StreetProbeRequest(id, now, current.Position, current.Facing, angle, radius);
        }

        private bool HandleVehicleContact(StreetContact contact, double now, int tickGeneration)
        {
            if (vehicleContacts.Contains(contact.ObjectId)) return false;
            vehicleContacts.Add(contact.ObjectId);
            current = Copy(PlayerMainState.Failing, ActionMode.Walking, current.Position, current.Facing,
                current.Courage, current.Focus, current.CaneReadyAt, current.StunnedUntil, current.ConfusedUntil);

            Action<StreetContact> contacted = Contacted;
            if (contacted != null) contacted(contact);
            if (recoveryGeneration != tickGeneration) return true;
            if (failedGeneration == recoveryGeneration) return true;

            failedGeneration = recoveryGeneration;
            Action<StreetFailure, string> failed = Failed;
            if (failed != null) failed(StreetFailure.Vehicle, contact.ObjectId);
            // Failed is synchronous: the listener may have restored this controller. Never resume this Tick.
            return true;
        }

        private bool HandleOrdinaryContact(StreetContact contact, double now, int tickGeneration)
        {
            if (!contactsThisTick.Add(contact.ObjectId)) return false;

            ContactReceipt receipt;
            if (!contacts.TryGetValue(contact.ObjectId, out receipt))
            {
                receipt = new ContactReceipt();
                contacts.Add(contact.ObjectId, receipt);
            }
            // Tuning is float seconds while the simulation clock is double seconds.
            bool accepted = !receipt.Touching && now - receipt.LeftAt + ContactTimeEpsilon >= config.Gameplay.contactExitSeconds &&
                now - receipt.LastHit + ContactTimeEpsilon >= config.Gameplay.contactDebounceSeconds;
            receipt.Touching = true;
            if (!accepted) return false;
            receipt.LastHit = now;

            // Ordinary physical contact is feedback only. Legacy per-object damage/stun and
            // Gameplay damageStatic must never turn walls, people or parked vehicles into recovery.
            Action<StreetContact> contacted = Contacted;
            if (contacted != null) contacted(contact);
            if (recoveryGeneration != tickGeneration) return true;
            return false;
        }

        private void MarkContactExits(double now)
        {
            foreach (KeyValuePair<string, ContactReceipt> pair in contacts)
            {
                if (pair.Value.Touching && !contactsThisTick.Contains(pair.Key))
                {
                    pair.Value.Touching = false;
                    pair.Value.LeftAt = now;
                }
            }
        }

        private void PruneTaps(double now)
        {
            while (caneTaps.Count > 0 && now - caneTaps.Peek() >= config.Gameplay.confusionWindow)
                caneTaps.Dequeue();
        }

        private double OldestTap() { return caneTaps.Count == 0 ? 0 : caneTaps.Peek(); }

        private static PlayerState Copy(PlayerMainState state, ActionMode mode, Point2 position, Point2 facing,
            float courage, float focus, double caneReadyAt, double stunnedUntil, double confusedUntil)
        {
            return new PlayerState(state, mode, position, StreetGeometry.Unit(facing), Clamp(courage, 0, 100),
                Clamp(focus, 0, 100), Math.Max(0, caneReadyAt), Math.Max(0, stunnedUntil), Math.Max(0, confusedUntil));
        }

        private static void ValidateFinite(Point2 point, string parameter)
        {
            if (float.IsNaN(point.X) || float.IsInfinity(point.X) || float.IsNaN(point.Y) || float.IsInfinity(point.Y))
                throw new ArgumentOutOfRangeException(parameter, "Point coordinates must be finite.");
        }

        private static void ValidateEffect(float value, string parameter)
        {
            if (value < 0 || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter, "Contact effects must be finite and non-negative.");
        }

        private static float Length(Point2 value)
        {
            return (float)Math.Sqrt(value.X * value.X + value.Y * value.Y);
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
