using System;
using System.Collections.Generic;
using TingLu.Contracts;
using UnityEngine;

namespace TingLu.Presentation
{
    /// <summary>Explicitly initialized pooled renderer. Assign the camera and legal presentation-only layer in the Inspector.</summary>
    public sealed class VoiceRenderer : MonoBehaviour
    {
        private static readonly Comparison<VoiceMark> MarkSortComparison = CompareMarkPriority;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private int voiceLayer = 8;
        [SerializeField] private int poolSize = 96;
        [SerializeField] private float lineWidth = 0.025f;
        [SerializeField] private Material lineMaterial;
        private readonly List<LineRenderer> pool = new List<LineRenderer>();
        private readonly List<VoiceMark> orderedMarks = new List<VoiceMark>();
        private readonly List<Segment2> locatorSegments = new List<Segment2>(20);
        private PresentationFrameComposer composer;
        private Material generatedMaterial;
        private bool initialized;

        public void Configure(Camera camera, int layer, Material material = null)
        {
            if(initialized)throw new InvalidOperationException("Configure the voice renderer before Initialize.");
            if(camera==null)throw new ArgumentNullException("camera");
            if(layer<0||layer>31)throw new ArgumentOutOfRangeException("layer");
            playerCamera=camera;voiceLayer=layer;if(material!=null)lineMaterial=material;
        }

        public void Initialize(PresentationFrameComposer value)
        {
            if(value==null)throw new ArgumentNullException("value");
            composer=value;
            if(playerCamera==null)throw new InvalidOperationException("Assign a player camera before Initialize.");
            playerCamera.clearFlags=CameraClearFlags.SolidColor;
            playerCamera.backgroundColor=Color.black;
            playerCamera.cullingMask=1<<voiceLayer;
            if(lineMaterial==null)
            {
                Shader shader=Shader.Find("Hidden/TingLu/VoiceLine");
                if(shader==null)throw new InvalidOperationException("Hidden/TingLu/VoiceLine shader is missing.");
                generatedMaterial=new Material(shader);lineMaterial=generatedMaterial;
            }
            for(int i=pool.Count;i<Mathf.Clamp(poolSize,8,256);i++)
            {
                var go=new GameObject("Voice line "+i);go.layer=voiceLayer;go.transform.SetParent(transform,false);
                var line=go.AddComponent<LineRenderer>();line.material=lineMaterial;line.useWorldSpace=false;
                line.positionCount=0;line.startWidth=line.endWidth=lineWidth;line.numCapVertices=2;
                pool.Add(line);
            }
            initialized=true;
        }

        public void Initialize(PresentationFrameComposer value,Camera camera,int layer=8)
        { Configure(camera,layer);Initialize(value); }

        public void Render(IPerceptionReader perception,IMemoryReader memory,IPlayerStateReader player,UserSettings settings,double now)
        {
            if(!initialized)throw new InvalidOperationException("Initialize the voice renderer from the scene owner first.");
            PresentationFrame frame=composer.Compose(perception,memory,player,settings,now);
            int cursor=0;
            if(frame.Visible)
            {
                orderedMarks.Clear();
                for(int i=0;i<frame.Marks.Count;i++)orderedMarks.Add(frame.Marks[i]);
                var ordered=orderedMarks;
                ordered.Sort(MarkSortComparison);
                int critical=0;
                while(critical<ordered.Count&&MarkPriority(ordered[critical])<=1)
                {
                    VoiceMark mark=ordered[critical++];
                    if(mark.Shape==VoiceShape.LocatorArc) DrawLocator(mark,ref cursor);
                    else DrawSegments(mark,ref cursor);
                }
                if(frame.PainFlash)DrawPain(frame.PainAlpha,frame.PainColor,ref cursor);
                if(frame.ViewSector.Count>0)DrawRaw(frame.ViewSector,ToColor(frame.SectorColor),ref cursor);
                for(;critical<ordered.Count;critical++)
                {
                    VoiceMark mark=ordered[critical];
                    if(mark.Shape==VoiceShape.LocatorArc) DrawLocator(mark,ref cursor);
                    else DrawSegments(mark,ref cursor);
                }
            }
            for(int i=cursor;i<pool.Count;i++)pool[i].positionCount=0;
        }
        public void OnEvent(GameplayEvent value){if(initialized)composer.Accept(value);}
        public void ResetFeedback(){if(initialized)composer.ResetFeedback();Clear();}
        public void Clear(){for(int i=0;i<pool.Count;i++)pool[i].positionCount=0;}
        private void OnDestroy(){if(generatedMaterial!=null)Destroy(generatedMaterial);}

        private void DrawSegments(VoiceMark mark,ref int cursor)
        {
            Color color=new Color(mark.Color.R,mark.Color.G,mark.Color.B,mark.Color.A*mark.Opacity);
            foreach(Segment2 s in mark.Segments)
            {
                if(mark.Shape==VoiceShape.MovingDash||mark.Shape==VoiceShape.ThreatPulse||mark.Shape==VoiceShape.InteractionDot)
                {
                    int dashes=mark.Shape==VoiceShape.ThreatPulse?3:mark.Shape==VoiceShape.InteractionDot?5:7;
                    for(int d=0;d<dashes;d+=2)
                    {
                        float a=(float)d/dashes,b=(float)(d+1)/dashes;
                        DrawRaw(new Segment2(Lerp(s.A,s.B,a),Lerp(s.A,s.B,b)),color,ref cursor);
                    }
                }
                else DrawRaw(s,color,ref cursor);
            }
        }
        private void DrawLocator(VoiceMark mark,ref int cursor)
        {
            float radius=0.15f+0.22f*mark.Opacity; locatorSegments.Clear();
            for(int ring=0;ring<2;ring++)
            {
                float r=radius*(ring+1); Point2 previous=Point(mark.Position,r, -48);
                for(int degree=-40;degree<=40;degree+=8)
                {Point2 next=Point(mark.Position,r,degree);locatorSegments.Add(new Segment2(previous,next));previous=next;}
            }
            DrawRaw(locatorSegments,new Color(mark.Color.R,mark.Color.G,mark.Color.B,mark.Color.A*mark.Opacity),ref cursor);
        }
        private void DrawPain(float alpha,Tint painColor,ref int cursor)
        {
            Camera cam=playerCamera; float d=Mathf.Abs(cam.transform.position.z);
            Vector3 a=cam.ViewportToWorldPoint(new Vector3(0.02f,0.02f,d)),b=cam.ViewportToWorldPoint(new Vector3(0.98f,0.02f,d));
            Vector3 c=cam.ViewportToWorldPoint(new Vector3(0.98f,0.98f,d)),e=cam.ViewportToWorldPoint(new Vector3(0.02f,0.98f,d));
            Color tint=ToColor(painColor); tint.a*=alpha;
            DrawRaw(new Segment2(new Point2(a.x,a.y),new Point2(b.x,b.y)),tint,ref cursor);
            DrawRaw(new Segment2(new Point2(b.x,b.y),new Point2(c.x,c.y)),tint,ref cursor);
            DrawRaw(new Segment2(new Point2(c.x,c.y),new Point2(e.x,e.y)),tint,ref cursor);
            DrawRaw(new Segment2(new Point2(e.x,e.y),new Point2(a.x,a.y)),tint,ref cursor);
        }
        private void DrawRaw(Segment2 segment,Color color,ref int cursor)
        {
            if(cursor>=pool.Count)return;
            LineRenderer line=pool[cursor++];line.startColor=line.endColor=color;line.positionCount=2;
            line.SetPosition(0,new Vector3(segment.A.X,segment.A.Y,0));line.SetPosition(1,new Vector3(segment.B.X,segment.B.Y,0));
        }
        private void DrawRaw(IReadOnlyList<Segment2> segments,Color color,ref int cursor)
        {
            for(int i=0;i<segments.Count;i++)DrawRaw(segments[i],color,ref cursor);
        }
        private static int MarkPriority(VoiceMark mark)
        {
            if(mark.Shape==VoiceShape.ThreatPulse)return 0;
            if(mark.Shape==VoiceShape.TactileGuide)return 1;
            if(mark.Shape==VoiceShape.MemoryLine)return 3;
            return 2;
        }
        private static int CompareMarkPriority(VoiceMark left,VoiceMark right)
        { return MarkPriority(left).CompareTo(MarkPriority(right)); }
        private static Color ToColor(Tint tint){return new Color(tint.R,tint.G,tint.B,tint.A);}
        private static Point2 Point(Point2 center,float radius,float degrees){double a=degrees*Math.PI/180;return new Point2(center.X+(float)Math.Sin(a)*radius,center.Y+(float)Math.Cos(a)*radius);}
        private static Point2 Lerp(Point2 a,Point2 b,float t){return new Point2(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
    }
}
