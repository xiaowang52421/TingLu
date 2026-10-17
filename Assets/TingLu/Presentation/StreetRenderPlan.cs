using System;
using System.Collections.Generic;
using TingLu.Contracts;
using UnityEngine;

namespace TingLu.Presentation
{
    internal enum StreetSurfacePattern
    {
        Aggregate = 0,
        Slabs = 1,
        Ribs = 2,
        Dots = 3,
        Crosswalk = 4,
        Ramp = 5,
        Plain = 6
    }

    internal enum StreetStrokeStyle
    {
        Permanent,
        StalePermanent,
        CurrentTemporary,
        StaleTemporary,
        TactileGuide,
        Voice,
        SignalGreen,
        SignalRed,
        Player
    }

    internal sealed class StreetSurfacePatch
    {
        internal readonly StreetKnownSurface Source;
        internal readonly bool Current;
        internal readonly StreetSurfacePattern Pattern;

        internal StreetSurfacePatch(StreetKnownSurface source, bool current, StreetSurfacePattern pattern)
        { Source = source; Current = current; Pattern = pattern; }
    }

    internal sealed class StreetStroke
    {
        internal readonly Segment2 Segment;
        internal readonly StreetStrokeStyle Style;
        internal readonly float Width;
        internal readonly float Elevation;
        internal readonly float DashLength;
        internal readonly float GapLength;
        internal readonly Color Color;

        internal StreetStroke(Segment2 segment, StreetStrokeStyle style, float width, float elevation,
            float dashLength, float gapLength, Color color)
        { Segment = segment; Style = style; Width = width; Elevation = elevation; DashLength = dashLength; GapLength = gapLength; Color = color; }
    }

    internal sealed class StreetRenderPlan
    {
        private const float SignalFixtureTopOffset = 0.005f;

        internal readonly List<StreetSurfacePatch> Surfaces = new List<StreetSurfacePatch>();
        internal readonly List<StreetStroke> Strokes = new List<StreetStroke>();

        internal static StreetRenderPlan Create(StreetMemoryFrame memory, PerceptionFrame perception,
            PlayerState player, UserSettings settings, double now, IEnumerable<StreetSignalObservation> signals = null)
        {
            if (memory == null) throw new ArgumentNullException("memory");
            if (perception == null) throw new ArgumentNullException("perception");
            if (player == null) throw new ArgumentNullException("player");
            if (settings == null) throw new ArgumentNullException("settings");
            ContractGuard.Time(now);

            var plan = new StreetRenderPlan();
            if (settings.Display == DisplayMode.AudioOnly) return plan;

            foreach (StreetKnownSurface surface in memory.Surfaces)
            {
                bool current = IsCurrent(surface.ObservedAt, surface.CurrentUntil, now);
                plan.Surfaces.Add(new StreetSurfacePatch(surface, current, PatternForMaterial(surface.MaterialId)));
            }

            foreach (StreetKnownObject item in memory.Objects)
            {
                bool current = IsCurrent(item.ObservedAt, item.CurrentUntil, now);
                StreetStrokeStyle style = item.Kind == StreetKind.Permanent
                    ? (current ? StreetStrokeStyle.Permanent : StreetStrokeStyle.StalePermanent)
                    : (current ? StreetStrokeStyle.CurrentTemporary : StreetStrokeStyle.StaleTemporary);
                float height = Mathf.Clamp(item.Height, 0f, 2.5f);
                float width = WidthForHeight(height);
                AddStrokes(plan.Strokes, item.Contour, style, width, settings.Palette);
                if (height > 0.05f && (item.Kind == StreetKind.Permanent || current))
                    AddHeightDetail(plan.Strokes, item.Contour, style, width, height, settings.Palette);
            }

            if (signals != null)
            {
                foreach (StreetSignalObservation signal in signals)
                {
                    if (signal == null || !IsCurrent(signal.ObservedAt, signal.ExpiresAt, now)) continue;
                    for (int i = 0; i < memory.Objects.Count; i++)
                    {
                        StreetKnownObject fixture = memory.Objects[i];
                        if (!StringComparer.Ordinal.Equals(fixture.ObjectId, signal.ObjectId)) continue;
                        AddStrokesAtElevation(plan.Strokes, fixture.Contour,
                            signal.IsGreen ? StreetStrokeStyle.SignalGreen : StreetStrokeStyle.SignalRed,
                            WidthForHeight(Mathf.Clamp(fixture.Height, 0f, 2.5f)), settings.Palette,
                            Mathf.Clamp(fixture.Height, 0f, 2.5f) + SignalFixtureTopOffset);
                        break;
                    }
                }
            }

            foreach (Observation item in perception.Observations)
            {
                if (item == null || !IsCurrent(item.ObservedAt, item.ExpiresAt, now)) continue;
                if (item.Kind == InformationKind.SoundLocation)
                {
                    if (settings.VoiceprintIntensity <= 0) continue;
                    float lifetime = (float)Math.Max(0, Math.Min(1,
                        (item.ExpiresAt - now) / Math.Max(0.001, item.ExpiresAt - item.ObservedAt)));
                    float alpha = Mathf.Clamp01(lifetime * settings.VoiceprintIntensity);
                    AddVoiceArcs(plan.Strokes, item.ObservedPosition, alpha, item.Semantic, settings.Palette);
                    continue;
                }

                if (item.Kind == InformationKind.Tactile && item.Source == ObservationSource.TactilePath)
                {
                    AddStrokes(plan.Strokes, item.ConfirmedContour, StreetStrokeStyle.TactileGuide, 0.035f, settings.Palette);
                    continue;
                }

                // Active and event geometry is intentionally not rendered as an object silhouette.
                if (item.Kind != InformationKind.LocalGeometry || item.Category != ObjectCategory.Static ||
                    item.ConfirmedContour.Count == 0)
                    continue;

                StreetStrokeStyle observationStyle = item.MemoryPolicy == MemoryPolicy.ConfirmedStaticGeometry
                    ? StreetStrokeStyle.Permanent
                    : StreetStrokeStyle.CurrentTemporary;
                AddStrokes(plan.Strokes, item.ConfirmedContour, observationStyle, 0.035f, settings.Palette);
            }

            AddPlayerArrow(plan.Strokes, player, settings.Palette, KnownGroundElevationAt(memory, player.Position));
            return plan;
        }

        private static bool IsCurrent(double observedAt, double currentUntil, double now)
        { return now >= observedAt && now < currentUntil; }

        private static float WidthForHeight(float height)
        { return Mathf.Clamp(0.022f + Mathf.Clamp(height, 0f, 2.5f) * 0.012f, 0.022f, 0.052f); }

        internal static StreetSurfacePattern PatternForMaterial(string material)
        {
            switch (material)
            {
                case "sidewalk": return StreetSurfacePattern.Slabs;
                case "tactile": return StreetSurfacePattern.Ribs;
                case "tactile.dots": return StreetSurfacePattern.Dots;
                case "asphalt": return StreetSurfacePattern.Aggregate;
                case "crosswalk": return StreetSurfacePattern.Crosswalk;
                case "ramp": return StreetSurfacePattern.Ramp;
                default: return StreetSurfacePattern.Plain;
            }
        }

        private static void AddStrokes(List<StreetStroke> output, IReadOnlyList<Segment2> contour,
            StreetStrokeStyle style, float width, PaletteMode palette)
        {
            AddStrokesAtElevation(output, contour, style, width, palette, 0f);
        }

        private static void AddStrokesAtElevation(List<StreetStroke> output, IReadOnlyList<Segment2> contour,
            StreetStrokeStyle style, float width, PaletteMode palette, float elevation)
        {
            Color color = ColorFor(style, palette);
            float dash = 0f, gap = 0f;
            if (style == StreetStrokeStyle.StaleTemporary) { dash = 0.10f; gap = 0.12f; }
            else if (style == StreetStrokeStyle.TactileGuide) { dash = 0.12f; gap = 0.055f; }

            for (int i = 0; i < contour.Count; i++)
                output.Add(new StreetStroke(contour[i], style, width, elevation, dash, gap, color));
        }

        private static void AddHeightDetail(List<StreetStroke> output, IReadOnlyList<Segment2> contour,
            StreetStrokeStyle style, float width, float height, PaletteMode palette)
        {
            Color color = ColorFor(style, palette);
            var emittedCorners = new HashSet<Point2>();
            for (int i = 0; i < contour.Count; i++)
            {
                Segment2 segment = contour[i];
                output.Add(new StreetStroke(segment, style, width, height, 0f, 0f, color));
                if (emittedCorners.Add(segment.A))
                    output.Add(new StreetStroke(new Segment2(segment.A, segment.A), style, width, height, 0f, 0f, color));
                if (emittedCorners.Add(segment.B))
                    output.Add(new StreetStroke(new Segment2(segment.B, segment.B), style, width, height, 0f, 0f, color));
            }
        }

        private static void AddVoiceArcs(List<StreetStroke> output, Point2 center, float alpha,
            PerceptionSemantic semantic, PaletteMode palette)
        {
            Color color = VoiceColor(semantic, palette, alpha);
            AddArc(output, center, 0.20f, -135f, 270f, 12, color);
            AddArc(output, center, 0.38f, -135f, 270f, 12, color);
        }

        private static void AddArc(List<StreetStroke> output, Point2 center, float radius,
            float startDegrees, float sweepDegrees, int steps, Color color)
        {
            Point2 previous = ArcPoint(center, radius, startDegrees);
            for (int i = 1; i <= steps; i++)
            {
                Point2 next = ArcPoint(center, radius, startDegrees + sweepDegrees * i / steps);
                output.Add(new StreetStroke(new Segment2(previous, next), StreetStrokeStyle.Voice,
                    0.018f, 0f, 0f, 0f, color));
                previous = next;
            }
        }

        private static Point2 ArcPoint(Point2 center, float radius, float degrees)
        {
            double angle = degrees * Math.PI / 180.0;
            return new Point2(center.X + (float)Math.Cos(angle) * radius,
                center.Y + (float)Math.Sin(angle) * radius);
        }

        private static float KnownGroundElevationAt(StreetMemoryFrame memory, Point2 position)
        {
            float elevation = 0f;
            for (int i = 0; i < memory.Surfaces.Count; i++)
            {
                StreetKnownSurface surface = memory.Surfaces[i];
                if (!StreetGeometry.Contains(surface.Polygon, position)) continue;
                elevation = Mathf.Max(elevation, surface.HeightAt(position));
            }
            return Mathf.Max(0f, elevation);
        }

        private static void AddPlayerArrow(List<StreetStroke> output, PlayerState player, PaletteMode palette,
            float groundElevation)
        {
            Point2 facing = player.Facing;
            Point2 side = new Point2(-facing.Y, facing.X);
            Point2 tip = new Point2(player.Position.X + facing.X * 0.46f, player.Position.Y + facing.Y * 0.46f);
            Point2 basePoint = new Point2(player.Position.X + facing.X * 0.26f, player.Position.Y + facing.Y * 0.26f);
            Point2 wingA = new Point2(basePoint.X + side.X * 0.13f, basePoint.Y + side.Y * 0.13f);
            Point2 wingB = new Point2(basePoint.X - side.X * 0.13f, basePoint.Y - side.Y * 0.13f);
            Color color = ColorFor(StreetStrokeStyle.Player, palette);
            output.Add(new StreetStroke(new Segment2(player.Position, tip), StreetStrokeStyle.Player, 0.035f, groundElevation, 0f, 0f, color));
            output.Add(new StreetStroke(new Segment2(wingA, tip), StreetStrokeStyle.Player, 0.035f, groundElevation, 0f, 0f, color));
            output.Add(new StreetStroke(new Segment2(tip, wingB), StreetStrokeStyle.Player, 0.035f, groundElevation, 0f, 0f, color));
        }

        private static Color ColorFor(StreetStrokeStyle style, PaletteMode palette)
        {
            if (palette == PaletteMode.ColorBlind)
            {
                switch (style)
                {
                    case StreetStrokeStyle.Permanent: return new Color(0.88f, 0.91f, 0.88f, 0.96f);
                    case StreetStrokeStyle.StalePermanent: return new Color(0.37f, 0.44f, 0.48f, 0.78f);
                    case StreetStrokeStyle.CurrentTemporary: return new Color(1f, 0.78f, 0.25f, 1f);
                    case StreetStrokeStyle.StaleTemporary: return new Color(0.49f, 0.54f, 0.59f, 0.76f);
                    case StreetStrokeStyle.TactileGuide: return new Color(0.38f, 0.84f, 0.87f, 1f);
                    case StreetStrokeStyle.Voice: return new Color(0.8f, 0.88f, 0.98f, 1f);
                    case StreetStrokeStyle.SignalGreen: return new Color(0.24f, 0.78f, 1f, 1f);
                    case StreetStrokeStyle.SignalRed: return new Color(1f, 0.66f, 0.18f, 1f);
                    default: return new Color(0.96f, 0.96f, 0.84f, 1f);
                }
            }

            switch (style)
            {
                case StreetStrokeStyle.Permanent: return new Color(0.86f, 0.88f, 0.81f, 0.94f);
                case StreetStrokeStyle.StalePermanent: return new Color(0.38f, 0.43f, 0.44f, 0.75f);
                case StreetStrokeStyle.CurrentTemporary: return new Color(1f, 0.68f, 0.34f, 1f);
                case StreetStrokeStyle.StaleTemporary: return new Color(0.48f, 0.53f, 0.54f, 0.74f);
                case StreetStrokeStyle.TactileGuide: return new Color(0.40f, 0.86f, 0.79f, 1f);
                case StreetStrokeStyle.Voice: return new Color(0.78f, 0.85f, 0.92f, 1f);
                case StreetStrokeStyle.SignalGreen: return new Color(0.28f, 0.95f, 0.42f, 1f);
                case StreetStrokeStyle.SignalRed: return new Color(0.98f, 0.25f, 0.25f, 1f);
                default: return new Color(0.93f, 0.94f, 0.86f, 1f);
            }
        }

        private static Color VoiceColor(PerceptionSemantic semantic, PaletteMode palette, float alpha)
        {
            Color color;
            if (palette == PaletteMode.ColorBlind)
            {
                switch (semantic)
                {
                    case PerceptionSemantic.Threat: color = new Color(1f, 0.72f, 0.24f, 1f); break;
                    case PerceptionSemantic.Person: color = new Color(0.30f, 0.69f, 1f, 1f); break;
                    case PerceptionSemantic.Guide: color = new Color(0.33f, 0.91f, 0.83f, 1f); break;
                    case PerceptionSemantic.Interaction: color = new Color(0.86f, 0.55f, 0.93f, 1f); break;
                    default: color = new Color(0.78f, 0.86f, 0.96f, 1f); break;
                }
            }
            else
            {
                switch (semantic)
                {
                    case PerceptionSemantic.Threat: color = new Color(1f, 0.34f, 0.27f, 1f); break;
                    case PerceptionSemantic.Person: color = new Color(0.38f, 0.75f, 0.96f, 1f); break;
                    case PerceptionSemantic.Guide: color = new Color(0.44f, 0.87f, 0.78f, 1f); break;
                    case PerceptionSemantic.Interaction: color = new Color(0.96f, 0.77f, 0.40f, 1f); break;
                    default: color = new Color(0.80f, 0.86f, 0.91f, 1f); break;
                }
            }
            color.a *= alpha;
            return color;
        }
    }
}
