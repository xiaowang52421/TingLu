using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Presentation
{
    public enum VoiceShape { SolidContour, MovingDash, ThreatPulse, InteractionDot, LocatorArc, MemoryLine, TactileGuide }
    public struct Tint
    {
        public readonly float R,G,B,A;
        public Tint(float r,float g,float b,float a){R=r;G=g;B=b;A=a;}
        public Tint WithAlpha(float alpha){return new Tint(R,G,B,Math.Max(0,Math.Min(1,alpha)));}
    }
    public sealed class VoiceMark
    {
        public readonly string Id;
        public readonly VoiceShape Shape;
        public readonly Tint Color;
        public readonly IReadOnlyList<Segment2> Segments;
        public readonly Point2 Position;
        public readonly float Opacity;
        public readonly float PatternScale;
        public VoiceMark(string id,VoiceShape shape,Tint color,IReadOnlyList<Segment2> segments,Point2 position,float opacity,float patternScale)
        {Id=id;Shape=shape;Color=color;Segments=segments;Position=position;Opacity=opacity;PatternScale=patternScale;}
    }
    public sealed class PresentationFrame
    {
        public readonly bool Visible;
        public readonly IReadOnlyList<VoiceMark> Marks;
        public readonly bool PainFlash;
        public readonly float PainAlpha;
        public readonly IReadOnlyList<Segment2> ViewSector;
        public readonly Tint SectorColor;
        public readonly Tint PainColor;
        internal PresentationFrame(bool visible,IReadOnlyList<VoiceMark> marks,bool pain,float alpha,IReadOnlyList<Segment2> sector,Tint sectorColor,Tint painColor)
        {Visible=visible;Marks=marks;PainFlash=pain;PainAlpha=alpha;ViewSector=sector;SectorColor=sectorColor;PainColor=painColor;}
    }

    /// <summary>Pure display projection. Its only geometry inputs are perception and frozen memory snapshots.</summary>
    public sealed class PresentationFrameComposer
    {
        private static readonly Tint StaticOrange=new Tint(1f,0.55f,0.24f,1);
        private static readonly Tint PersonBlue=new Tint(0.36f,0.82f,1f,1);
        private static readonly Tint ThreatRed=new Tint(1f,0.25f,0.2f,1);
        private static readonly Tint UnknownSound=new Tint(0.72f,0.76f,0.80f,1);
        private static readonly Tint InteractionGold=new Tint(1f,0.82f,0.42f,1);
        private static readonly Tint MemoryGrey=new Tint(0.52f,0.60f,0.65f,1);
        private static readonly Tint GuideCyan=new Tint(0.42f,0.83f,0.82f,1);
        private static readonly Tint ColorBlindOrdinary=new Tint(0.90f,0.62f,0f,1);
        private static readonly Tint ColorBlindPerson=new Tint(0f,0.45f,0.70f,1);
        private static readonly Tint ColorBlindThreat=new Tint(0.84f,0.37f,0f,1);
        private static readonly Tint ColorBlindInteraction=new Tint(0.80f,0.47f,0.65f,1);
        private static readonly Tint ColorBlindGuide=new Tint(0.34f,0.71f,0.91f,1);
        private static readonly Tint ColorBlindUnknown=new Tint(0.78f,0.80f,0.82f,1);
        private double painAt=-1;
        public void ResetFeedback() { painAt = -1; }

        public void Accept(GameplayEvent value)
        { if(value!=null && value.Type==SemanticType.Collision) painAt=value.SimulationTime; }

        public PresentationFrame Compose(IPerceptionReader perception,IMemoryReader memory,IPlayerStateReader player,
            UserSettings settings,double now)
        {
            if(perception==null||memory==null||player==null||settings==null)throw new ArgumentNullException();
            ContractGuard.Time(now);
            if(settings.Display==DisplayMode.AudioOnly)return new PresentationFrame(false,new VoiceMark[0],false,0,new Segment2[0],default(Tint),default(Tint));
            var marks=new List<VoiceMark>(); PlayerState state=player.Current;
            float intensity=(float)Math.Max(0,Math.Min(2,settings.VoiceprintIntensity));
            foreach(StaticMemorySnapshot fragment in memory.Fragments)
            {
                IReadOnlyList<Segment2> contours=fragment.WorldContour;
                Tint color=settings.Palette==PaletteMode.ColorBlind?ColorBlindUnknown:MemoryGrey;
                if(fragment.Semantic==PerceptionSemantic.Guide)
                {
                    var nearby=ClipToRadius(fragment.WorldContour,state.Position,6f);
                    if(nearby.Count==0)continue;
                    contours=nearby.AsReadOnly();
                    color=settings.Palette==PaletteMode.ColorBlind?ColorBlindGuide:GuideCyan;
                }
                marks.Add(new VoiceMark("memory:"+fragment.FragmentId,VoiceShape.MemoryLine,color,
                    contours,contours[0].A,0.34f*intensity,1));
            }
            foreach(Observation item in perception.Current.Observations)
            {
                if(item.ExpiresAt<=now)continue;
                float remaining=(float)Math.Max(0,Math.Min(1,(item.ExpiresAt-now)/Math.Max(0.001,item.ExpiresAt-item.ObservedAt)));
                float distance=Distance(state.Position,item.ObservedPosition);
                float reach=state.Focus<30?3f:state.Mode==ActionMode.Listening?5.5f:4f;
                float fade=Math.Max(0,1-distance/reach);
                float alpha=remaining*fade;
                if(alpha<=0)continue;
                alpha=(float)Math.Max(0,Math.Min(1,alpha*settings.VoiceprintIntensity));
                if(item.Source==ObservationSource.TactilePath)
                { marks.Add(new VoiceMark("guide:"+item.ObjectId,VoiceShape.TactileGuide,ColorFor(item,settings.Palette),item.ConfirmedContour,item.ObservedPosition,alpha,0.72f));continue; }
                if(item.Kind==InformationKind.SoundLocation)
                { marks.Add(new VoiceMark("loc:"+item.ObjectId,VoiceShape.LocatorArc,ColorFor(item,settings.Palette),new Segment2[0],item.ObservedPosition,alpha,1));continue; }
                VoiceShape shape=ShapeFor(item.Semantic);
                marks.Add(new VoiceMark("live:"+item.ObjectId,shape,ColorFor(item,settings.Palette),item.ConfirmedContour,item.ObservedPosition,alpha,1));
            }
            bool pain=painAt>=0&&now-painAt<1;
            float painAlpha=pain?(float)(1-(now-painAt)):0;
            Tint sector=settings.Palette==PaletteMode.ColorBlind?new Tint(0.1f,0.7f,0.85f,0.2f):new Tint(0.31f,0.38f,0.43f,0.18f);
            Tint painTint=settings.Palette==PaletteMode.ColorBlind?new Tint(0.9f,0.5f,0f,0.72f):new Tint(1f,0.15f,0.12f,0.72f);
            return new PresentationFrame(true,marks.AsReadOnly(),pain,painAlpha,BuildSector(state),sector,painTint);
        }
        private static VoiceShape ShapeFor(PerceptionSemantic semantic)
        {
            switch(semantic)
            {
                case PerceptionSemantic.Person:return VoiceShape.MovingDash;
                case PerceptionSemantic.Threat:return VoiceShape.ThreatPulse;
                case PerceptionSemantic.Interaction:return VoiceShape.InteractionDot;
                case PerceptionSemantic.Guide:return VoiceShape.TactileGuide;
                default:return VoiceShape.SolidContour;
            }
        }
        private static Tint ColorFor(Observation item,PaletteMode palette)
        {
            if(item.Source==ObservationSource.TactilePath)
                return palette==PaletteMode.ColorBlind?ColorBlindGuide:GuideCyan;
            if(palette==PaletteMode.ColorBlind)
            {
                switch(item.Semantic)
                {
                    case PerceptionSemantic.Person:return ColorBlindPerson;
                    case PerceptionSemantic.Threat:return ColorBlindThreat;
                    case PerceptionSemantic.Interaction:return ColorBlindInteraction;
                    case PerceptionSemantic.Guide:return ColorBlindGuide;
                    case PerceptionSemantic.Unknown:return ColorBlindUnknown;
                    default:return ColorBlindOrdinary;
                }
            }
            switch(item.Semantic)
            {
                case PerceptionSemantic.Person:return PersonBlue;
                case PerceptionSemantic.Threat:return ThreatRed;
                case PerceptionSemantic.Interaction:return InteractionGold;
                case PerceptionSemantic.Guide:return GuideCyan;
                case PerceptionSemantic.Unknown:return UnknownSound;
                default:return StaticOrange;
            }
        }
        private static float Distance(Point2 a,Point2 b){float x=a.X-b.X,y=a.Y-b.Y;return(float)Math.Sqrt(x*x+y*y);}
        private static List<Segment2> ClipToRadius(IReadOnlyList<Segment2> source,Point2 center,float radius)
        {
            var result=new List<Segment2>();
            foreach(Segment2 segment in source)
            {
                float dx=segment.B.X-segment.A.X,dy=segment.B.Y-segment.A.Y;
                float fx=segment.A.X-center.X,fy=segment.A.Y-center.Y;
                float aa=dx*dx+dy*dy;
                if(aa<1e-10f){if(Distance(center,segment.A)<=radius)result.Add(segment);continue;}
                float bb=2*(fx*dx+fy*dy),cc=fx*fx+fy*fy-radius*radius;
                float disc=bb*bb-4*aa*cc;
                if(disc<0){if(cc<=0)result.Add(segment);continue;}
                float root=(float)Math.Sqrt(disc),lo=Math.Max(0,(-bb-root)/(2*aa)),hi=Math.Min(1,(-bb+root)/(2*aa));
                if(hi>lo)result.Add(new Segment2(Lerp(segment.A,segment.B,lo),Lerp(segment.A,segment.B,hi)));
            }
            return result;
        }
        private static Point2 Lerp(Point2 a,Point2 b,float t){return new Point2(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
        private static IReadOnlyList<Segment2> BuildSector(PlayerState state)
        {
            float radius=state.Focus<30?3f:state.Mode==ActionMode.Listening?5.5f:4f;
            float angle=state.Focus<30?60f:state.Mode==ActionMode.Listening?110f:75f;
            double facing=Math.Atan2(state.Facing.X,state.Facing.Y),half=angle*Math.PI/360.0;
            Point2 left=Polar(state.Position,radius,facing-half),right=Polar(state.Position,radius,facing+half);
            return new[]{new Segment2(state.Position,left),new Segment2(left,right),new Segment2(right,state.Position)};
        }
        private static Point2 Polar(Point2 center,float radius,double angle){return new Point2(center.X+(float)Math.Sin(angle)*radius,center.Y+(float)Math.Cos(angle)*radius);}
    }
}
