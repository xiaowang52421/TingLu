using System;
using System.Collections.Generic;
using System.IO;
using TingLu.Contracts;
using UnityEngine;
using UnityEditor;

namespace TingLu.Levels.Editor
{

    public sealed class LevelEditorWindow : EditorWindow
    {
        [SerializeField] private LevelEditBuffer buffer;
        [SerializeField] private string filePath="",savedJson="",selectedId="",clipboard="";
        [SerializeField] private string fileAssetGuid="";
        [SerializeField] private Vector2 center=new Vector2(10,20);
        [SerializeField] private float scale=12;
        [SerializeField] private bool snap=true,drawGuide;
        private LevelDocument document;
        private List<LevelIssue> issues=new List<LevelIssue>();
        private Vector2 propertiesScroll,paletteScroll,issuesScroll;
        private Rect canvas;
        private string placing,dragType,status="",dragOriginal,drawRouteActor;
        private bool dragging,panning,rotating;
        private Vector2 dragOffset,lastMouse;
        private int selectedNode=-1;
        private string selectedRoutePoint="";
        private bool Dirty { get { return buffer!=null&&buffer.json!=savedJson; } }
        [MenuItem("TingLu/关卡编辑器")]
        public static void ShowWindow(){var w=GetWindow<LevelEditorWindow>();w.titleContent=new GUIContent("听路 · 关卡编辑器");w.minSize=new Vector2(1000,650);w.Show();}
        private void OnEnable()
        {
            if(buffer==null){buffer=CreateInstance<LevelEditBuffer>();buffer.hideFlags=HideFlags.HideAndDontSave;buffer.json=LevelCodec.Encode(FirstStepTemplate.Create());}
            drawRouteActor=null;
            try{RememberFilePath(EditorProjectPaths.ResolveFilePath(filePath,fileAssetGuid));}
            catch(Exception e){status=e.Message;}
            Reload();Undo.undoRedoPerformed+=Undone;
            if(!EditorPrefs.GetBool("TingLu.LevelEditor.Welcomed",false))EditorApplication.delayCall+=Welcome;
        }
        private void OnDisable(){Undo.undoRedoPerformed-=Undone;EditorApplication.delayCall-=Welcome;}
        private void Welcome(){if(this==null)return;EditorPrefs.SetBool("TingLu.LevelEditor.Welcomed",true);Help();}
        private void OnDestroy()
        {
            // Unity's own unsaved-window dialog uses SaveChanges/DiscardChanges before destruction.
            if(buffer!=null)DestroyImmediate(buffer);
        }
        private void Reload(){document=LevelCodec.Decode(buffer.json);selectedNode=document.guide.FindIndex(n=>n.id==selectedId);UpdateUnsaved();Repaint();}
        private void Undone(){Reload();issues.Clear();status="已撤销 / 重做；未保存修改仍保留在工作台。";}
        private void UpdateUnsaved(){hasUnsavedChanges=Dirty;saveChangesMessage="关卡有未保存修改。保存为草稿、放弃修改或取消关闭。";}
        public override void SaveChanges(){if(Save(false))base.SaveChanges();}
        public override void DiscardChanges(){savedJson=buffer.json;base.DiscardChanges();}
        private void Commit(string label)
        {
            string after=LevelCodec.Encode(document);if(after==buffer.json)return;
            try{LevelCodec.Decode(after);}
            catch(Exception e){document=LevelCodec.Decode(buffer.json);status="属性未应用："+e.Message;Repaint();return;}
            Undo.RecordObject(buffer,label);buffer.json=after;EditorUtility.SetDirty(buffer);issues.Clear();UpdateUnsaved();Repaint();
        }
        private bool SwitchAllowed()
        {
            if(!Dirty)return true;
            int choice=EditorUtility.DisplayDialogComplex("未保存的关卡","当前关卡有修改，请选择保存、放弃或取消。","保存草稿","取消","放弃修改");
            return choice==0?Save(false):choice==2;
        }
        private void SetDocument(LevelDocument value,string path)
        {Undo.ClearUndo(buffer);document=value;buffer.json=LevelCodec.Encode(value);RememberFilePath(path);savedJson=path==""?"":buffer.json;selectedId="";selectedNode=-1;issues.Clear();placing=null;drawGuide=false;drawRouteActor=null;selectedRoutePoint="";FrameAll();UpdateUnsaved();}
        private void RememberFilePath(string path)
        {filePath=EditorProjectPaths.StoreFilePath(path);fileAssetGuid=EditorProjectPaths.FileAssetGuid(path);}
        private void OnGUI()
        {
            if(document==null)return;
            try
            {
                Toolbar();
                GUILayout.Label(document.name+(Dirty?"　● 未保存":"　已保存"),EditorStyles.boldLabel);
                GUILayout.Label(string.IsNullOrEmpty(filePath)?"尚未保存；建议放在 "+EditorProjectPaths.DataAssetDirectory:filePath,EditorStyles.miniLabel);
                float top=78,bottom=position.height-155,left=180,right=290;
                canvas=new Rect(left+5,top,Mathf.Max(100,position.width-left-right-10),Mathf.Max(100,bottom-top));
                using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)){
                GUILayout.BeginArea(new Rect(0,top,left,bottom-top));Palette();GUILayout.EndArea();}
                DrawCanvas();
                using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)){
                GUILayout.BeginArea(new Rect(position.width-right,top,right,bottom-top));Properties();GUILayout.EndArea();
                }
                GUILayout.BeginArea(new Rect(5,bottom+5,position.width-10,145));IssuesPanel();GUILayout.EndArea();
                Shortcuts();
            }
            catch(ExitGUIException){throw;}
            catch(Exception error){status="操作未完成："+error.Message;Repaint();}
        }
        private void Toolbar()
        {
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if(GUILayout.Button("新建",EditorStyles.toolbarButton))if(SwitchAllowed())SetDocument(FirstStepTemplate.Blank(),"");
                if(GUILayout.Button("打开",EditorStyles.toolbarButton))Open();
                if(GUILayout.Button("复制关卡",EditorStyles.toolbarButton))if(SwitchAllowed())SetDocument(LevelCodec.Duplicate(document),"");
                if(GUILayout.Button("第一步模板",EditorStyles.toolbarButton))if(SwitchAllowed())SetDocument(FirstStepTemplate.Create(),"");
                if(GUILayout.Button("保存",EditorStyles.toolbarButton))Save(false);
                if(GUILayout.Button("另存为",EditorStyles.toolbarButton))Save(true);
                if(GUILayout.Button("撤销",EditorStyles.toolbarButton))Undo.PerformUndo();
                if(GUILayout.Button("重做",EditorStyles.toolbarButton))Undo.PerformRedo();
                if(GUILayout.Button("检查关卡",EditorStyles.toolbarButton))Check();
                if(GUILayout.Button("试玩",EditorStyles.toolbarButton))Preview();
                if(GUILayout.Button("使用帮助",EditorStyles.toolbarButton))Help();
                GUILayout.EndHorizontal();
            }
        }
        private void Palette()
        {
            paletteScroll=GUILayout.BeginScrollView(paletteScroll);
            GUILayout.Label("对象素材",EditorStyles.boldLabel);
            GUILayout.Label("点击后点地图，或拖入地图",EditorStyles.wordWrappedMiniLabel);
            for(int k=0;k<LevelCatalog.Types.Length;k++)
            {
                if(k==0||k==2||k==8||k==10)GUILayout.Label(k==0?"关卡入口":k==2?"世界与声源":k==8?"剧情与进度":"几何区域",EditorStyles.miniBoldLabel);
                Rect rect=GUILayoutUtility.GetRect(160,27);
                string type=LevelCatalog.Types[k];var e=Event.current;
                if(GUI.Button(rect,LevelCatalog.Names[k])){placing=type;drawGuide=false;drawRouteActor=null;status="在画布点击放置「"+LevelCatalog.Names[k]+"」，Esc 取消。";}
                if(e.rawType==EventType.MouseDown&&rect.Contains(e.mousePosition))dragType=type;
                if(e.rawType==EventType.MouseDrag&&dragType==type){DragAndDrop.PrepareStartDrag();DragAndDrop.SetGenericData("TingLuLevelType",type);DragAndDrop.StartDrag(LevelCatalog.Names[k]);dragType=null;}
            }
            GUILayout.Space(10);
            bool next=GUILayout.Toggle(drawGuide,"✎ 连续绘制盲道", "Button");if(next!=drawGuide){drawGuide=next;placing=null;drawRouteActor=null;}
            GUILayout.Label("单击加节点；结束绘制后拖动节点。选择节点后右侧可插入 / 删除。",EditorStyles.wordWrappedMiniLabel);
            if(GUILayout.Button("清空盲道重新绘制")){document.guide.Clear();selectedNode=-1;selectedId="";drawGuide=true;drawRouteActor=null;Commit("清空盲道");}
            snap=GUILayout.Toggle(snap,"吸附网格（0.5 米）");
            if(GUILayout.Button("查看整个关卡"))FrameAll();
            GUILayout.Label("滚轮缩放；中键或 Alt + 左键平移。选中物体后拖动箭头端点旋转。Shift + 点击优先选择重叠盲道节点。",EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(10);GUILayout.Label("关卡对象（点击定位）",EditorStyles.boldLabel);
            foreach(var i in document.items)if(GUILayout.Button(i.name,EditorStyles.miniButton))Locate(i.id);
            GUILayout.EndScrollView();
        }
        private Vector2 Screen(Point2 p){return new Vector2(canvas.center.x+(p.X-center.x)*scale,canvas.center.y-(p.Y-center.y)*scale);}
        private Point2 World(Vector2 p)
        {float x=center.x+(p.x-canvas.center.x)/scale,y=center.y-(p.y-canvas.center.y)/scale;if(snap){x=Mathf.Round(x*2)/2;y=Mathf.Round(y*2)/2;}return new Point2(x,y);}
        private void DrawCanvas()
        {
            EditorGUI.DrawRect(canvas,new Color(.10f,.13f,.16f));
            GUI.BeginClip(canvas);
            if(Event.current.type==EventType.Repaint)
            {
                float step=Mathf.Pow(10,Mathf.Floor(Mathf.Log10(60/scale)));
                float xmin=center.x-canvas.width/(2*scale),xmax=center.x+canvas.width/(2*scale),ymin=center.y-canvas.height/(2*scale),ymax=center.y+canvas.height/(2*scale);
                Handles.BeginGUI();Handles.color=new Color(.23f,.27f,.30f);
                for(float x=Mathf.Floor(xmin/step)*step;x<=xmax;x+=step)Line(new Point2(x,ymin),new Point2(x,ymax),1);
                for(float y=Mathf.Floor(ymin/step)*step;y<=ymax;y+=step)Line(new Point2(xmin,y),new Point2(xmax,y),1);
                foreach(var i in document.items)if(i.type=="ground")DrawItem(i);
                Handles.color=new Color(.9f,.8f,.25f,.8f);
                for(int j=1;j<document.guide.Count;j++)Line(new Point2(document.guide[j-1].x,document.guide[j-1].y),new Point2(document.guide[j].x,document.guide[j].y),Mathf.Max(2,document.guideWidth*scale));
                foreach(var i in document.items)if(i.type!="ground")DrawItem(i);
                foreach(var n in document.guide){Vector2 p=Screen(new Point2(n.x,n.y))-canvas.position;EditorGUI.DrawRect(new Rect(p.x-4,p.y-4,8,8),n.id==selectedId?Color.white:Color.yellow);}
                foreach(var route in document.movementRoutes)
                {
                    Handles.color=new Color(.9f,.45f,1,.85f);
                    for(int j=1;j<route.waypoints.Count;j++)Line(new Point2(route.waypoints[j-1].x,route.waypoints[j-1].y),new Point2(route.waypoints[j].x,route.waypoints[j].y),2);
                    if(route.actorId==selectedId)for(int j=0;j<route.waypoints.Count;j++)
                    {
                        var point=route.waypoints[j];Vector2 at=Screen(new Point2(point.x,point.y))-canvas.position;
                        Handles.color=point.id==selectedRoutePoint?Color.white:new Color(.9f,.45f,1,.85f);
                        Handles.DrawWireDisc(at,Vector3.forward,5);GUI.Label(new Rect(at.x+7,at.y+2,130,22),"路线 "+(j+1)+" · "+point.timeSeconds.ToString("0.##")+"秒");
                    }
                }
                Handles.EndGUI();
            }
            GUI.EndClip();
            GUI.Label(new Rect(canvas.x+10,canvas.y+8,canvas.width-20,35),"真值编辑视图　|　1 米 = "+scale.ToString("0")+" 像素　|　"+(drawRouteActor!=null?"绘制人物路线（Esc 结束）":drawGuide?"绘制盲道":placing!=null?"放置对象":"选择 / 拖动"));
            GUI.Label(new Rect(canvas.x+10,canvas.yMax-25,canvas.width-20,20),"坐标 +Y 朝上；试玩沿用正式感知，此画布不会进入游戏。",EditorStyles.miniLabel);
            CanvasInput();
        }
        private void Line(Point2 a,Point2 b,float width){Handles.DrawAAPolyLine(width,Screen(a)-canvas.position,Screen(b)-canvas.position);}
        private Vector2 RotationHandle(LevelItem i)
        {var p=LevelCompiler.Position(document,i);double r=i.rotation*Math.PI/180;return Screen(new Point2(p.X-(float)Math.Sin(r)*1.5f,p.Y+(float)Math.Cos(r)*1.5f));}
        private void DrawItem(LevelItem i)
        {
            var p=LevelCompiler.Position(document,i);Vector2 s=Screen(p)-canvas.position;
            Color color=i.type=="ground"?new Color(.22f,.30f,.25f):i.type=="spawn"?Color.cyan:i.type=="goal"?Color.green:i.type=="story"?new Color(.7f,.5f,1):LevelCatalog.IsSolid(i.type)?new Color(.85f,.42f,.35f):new Color(.4f,.7f,.95f);
            if(i.id==selectedId)color=Color.white;Handles.color=color;
            if(LevelCatalog.IsSolid(i.type)||i.type=="ground"||i.type=="walker"||i.type=="guard")
            {
                var vertices=new Vector3[5];double r=i.rotation*Math.PI/180;int k=0;
                foreach(var q in new[]{new Vector2(-i.width/2,-i.height/2),new Vector2(i.width/2,-i.height/2),new Vector2(i.width/2,i.height/2),new Vector2(-i.width/2,i.height/2),new Vector2(-i.width/2,-i.height/2)})
                {var world=new Point2(p.X+q.x*(float)Math.Cos(r)-q.y*(float)Math.Sin(r),p.Y+q.x*(float)Math.Sin(r)+q.y*(float)Math.Cos(r));vertices[k++]=Screen(world)-canvas.position;}
                Handles.DrawAAPolyLine(i.id==selectedId?3:2,vertices);
            }
            else Handles.DrawWireDisc(s,Vector3.forward,Mathf.Max(6,i.type=="story"||i.type=="checkpoint"||i.type=="goal"?i.radius*scale:6));
            GUI.Label(new Rect(s.x+6,s.y-20,160,24),i.name);
            if(i.id==selectedId||i.type=="spawn")
            {
                Vector2 h=RotationHandle(i)-canvas.position;Handles.DrawAAPolyLine(3,s,h);Handles.DrawWireDisc(h,Vector3.forward,5);GUI.Label(new Rect(h.x+7,h.y-12,50,24),"朝向");
            }
            if(i.type=="story"&&i.relative){var parent=document.items.Find(o=>o.id==i.target);if(parent!=null){Handles.color=new Color(.7f,.5f,1,.5f);Line(p,LevelCompiler.Position(document,parent),1);}}
        }
        private void CanvasInput()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            Event e=Event.current;bool inside=canvas.Contains(e.mousePosition);
            if(inside&&(e.type==EventType.DragUpdated||e.type==EventType.DragPerform))
            {
                string type=DragAndDrop.GetGenericData("TingLuLevelType") as string;
                if(type!=null){DragAndDrop.visualMode=DragAndDropVisualMode.Copy;if(e.type==EventType.DragPerform){DragAndDrop.AcceptDrag();Add(type,World(e.mousePosition));placing=null;}e.Use();return;}
            }
            if(inside&&e.type==EventType.ScrollWheel)
            {
                bool was=snap;snap=false;Point2 before=World(e.mousePosition);scale=Mathf.Clamp(scale*Mathf.Exp(-e.delta.y*.08f),.002f,100);Point2 after=World(e.mousePosition);center+=new Vector2(before.X-after.X,before.Y-after.Y);snap=was;e.Use();Repaint();return;
            }
            if(e.type==EventType.MouseDown&&inside)
            {
                GUI.FocusControl(null);
                if(e.button==2||(e.button==0&&e.alt)){panning=true;lastMouse=e.mousePosition;e.Use();return;}
                if(e.button!=0)return;
                if(drawRouteActor!=null)
                {var actor=document.items.Find(i=>i.id==drawRouteActor);if(actor!=null)AppendMovementPoint(actor,World(e.mousePosition));else drawRouteActor=null;e.Use();return;}
                if(placing!=null){Add(placing,World(e.mousePosition));placing=null;e.Use();return;}
                if(drawGuide){var p=World(e.mousePosition);var n=new LevelNode(p.X,p.Y);document.guide.Add(n);selectedId=n.id;selectedNode=document.guide.Count-1;Commit("添加盲道节点");e.Use();return;}
                selectedRoutePoint="";
                if(e.shift)
                {
                    var route=document.movementRoutes.Find(r=>r.actorId==selectedId);
                    if(route!=null)foreach(var point in route.waypoints)
                    {
                        if(Vector2.Distance(Screen(new Point2(point.x,point.y)),e.mousePosition)>=9)continue;
                        selectedRoutePoint=point.id;var mouse=Unsnapped(e.mousePosition);
                        dragOffset=new Vector2(point.x-mouse.X,point.y-mouse.Y);dragOriginal=buffer.json;dragging=true;e.Use();return;
                    }
                }
                var selected=document.items.Find(i=>i.id==selectedId);
                if(selected!=null&&Vector2.Distance(e.mousePosition,RotationHandle(selected))<10){rotating=true;dragOriginal=buffer.json;e.Use();return;}
                selectedId="";selectedNode=-1;
                if(e.shift)for(int j=document.guide.Count-1;j>=0;j--)if(Vector2.Distance(Screen(new Point2(document.guide[j].x,document.guide[j].y)),e.mousePosition)<9){selectedId=document.guide[j].id;selectedNode=j;break;}
                if(selectedNode<0)
                {
                    for(int pass=0;pass<2&&selectedId=="";pass++)
                    for(int j=document.items.Count-1;j>=0;j--)
                    {
                        var i=document.items[j];if((i.type=="ground")!=(pass==1))continue;
                        var p=LevelCompiler.Position(document,i);bool hit=Vector2.Distance(Screen(p),e.mousePosition)<12;
                        if(LevelCatalog.IsSolid(i.type)||i.type=="ground"){Point2 w=Unsnapped(e.mousePosition);hit|=LevelValidator.InBox(w.X,w.Y,i,0);}
                        if(hit){selectedId=i.id;break;}
                    }
                }
                if(selectedId=="")for(int j=document.guide.Count-1;j>=0;j--)if(Vector2.Distance(Screen(new Point2(document.guide[j].x,document.guide[j].y)),e.mousePosition)<9){selectedId=document.guide[j].id;selectedNode=j;break;}
                if(selectedId!="")
                {
                    Point2 at=selectedNode>=0?new Point2(document.guide[selectedNode].x,document.guide[selectedNode].y):LevelCompiler.Position(document,document.items.Find(i=>i.id==selectedId));
                    var mouse=Unsnapped(e.mousePosition);dragOffset=new Vector2(at.X-mouse.X,at.Y-mouse.Y);dragOriginal=buffer.json;dragging=true;
                }
                e.Use();Repaint();return;
            }
            if(e.type==EventType.MouseDrag&&(panning||dragging||rotating))
            {
                if(panning){Vector2 delta=e.mousePosition-lastMouse;center+=new Vector2(-delta.x/scale,delta.y/scale);lastMouse=e.mousePosition;}
                else if(rotating){var i=document.items.Find(a=>a.id==selectedId);if(i!=null){var p=LevelCompiler.Position(document,i);Point2 w=Unsnapped(e.mousePosition);float before=i.rotation;i.rotation=Mathf.Atan2(-(w.X-p.X),w.Y-p.Y)*Mathf.Rad2Deg;if(snap)i.rotation=Mathf.Round(i.rotation/15)*15;RotateMovementHeading(i,i.rotation-before);}}
                else
                {
                    var p=Unsnapped(e.mousePosition);float x=p.X+dragOffset.x,y=p.Y+dragOffset.y;if(snap){x=Mathf.Round(x*2)/2;y=Mathf.Round(y*2)/2;}
                    if(!string.IsNullOrEmpty(selectedRoutePoint))
                    {foreach(var route in document.movementRoutes){var point=route.waypoints.Find(n=>n.id==selectedRoutePoint);if(point!=null){point.x=x;point.y=y;break;}}}
                    else if(selectedNode>=0){document.guide[selectedNode].x=x;document.guide[selectedNode].y=y;}
                    else{var i=document.items.Find(a=>a.id==selectedId);if(i.relative){var parent=document.items.Find(a=>a.id==i.target);x-=parent.x;y-=parent.y;}TranslateMovementRoute(i,x-i.x,y-i.y);i.x=x;i.y=y;}
                }
                e.Use();Repaint();return;
            }
            if(e.type==EventType.MouseUp&&(dragging||panning||rotating))
            {if(!panning){buffer.json=dragOriginal;Commit(rotating?"旋转对象":"拖动对象 / 节点");}dragging=panning=rotating=false;e.Use();}
        }
        private Point2 Unsnapped(Vector2 screen){bool old=snap;snap=false;Point2 p=World(screen);snap=old;return p;}
        private void Add(string type,Point2 p)
        {drawRouteActor=null;var i=LevelCatalog.Create(type,p.X,p.Y);document.items.Add(i);selectedId=i.id;selectedNode=-1;Commit("添加"+i.name);}
        private void Properties()
        {
            propertiesScroll=GUILayout.BeginScrollView(propertiesScroll);
            GUILayout.Label("中文属性",EditorStyles.boldLabel);
            var item=document.items.Find(i=>i.id==selectedId);
            EditorGUI.BeginChangeCheck();
            if(selectedNode>=0&&selectedNode<document.guide.Count)
            {
                var n=document.guide[selectedNode];GUILayout.Label("盲道节点 "+(selectedNode+1));n.x=EditorGUILayout.FloatField("横向位置（米）",n.x);n.y=EditorGUILayout.FloatField("纵向位置（米）",n.y);
                if(EditorGUI.EndChangeCheck())Commit("修改节点");
                if(GUILayout.Button("在后方插入节点"))
                {var next=selectedNode+1<document.guide.Count?document.guide[selectedNode+1]:new LevelNode(n.x,n.y+1);var added=new LevelNode((n.x+next.x)/2,(n.y+next.y)/2);document.guide.Insert(selectedNode+1,added);selectedId=added.id;selectedNode++;Commit("插入盲道节点");}
                if(GUILayout.Button("删除节点"))Delete();
            }
            else if(item!=null)
            {
                float previousX=item.x,previousY=item.y,previousRotation=item.rotation;
                int typeIndex=Array.IndexOf(LevelCatalog.Types,item.type);GUILayout.Label(typeIndex<0?"未知对象":LevelCatalog.Names[typeIndex]);
                item.name=EditorGUILayout.TextField("名称",item.name);
                GUILayout.Label(item.relative?"位置相对绑定对象，拖动父对象时随动。":"位置独立；移动不会改变稳定标识。",EditorStyles.wordWrappedMiniLabel);
                item.x=EditorGUILayout.FloatField("横向位置（米）",item.x);item.y=EditorGUILayout.FloatField("纵向位置（米）",item.y);
                item.rotation=EditorGUILayout.FloatField("朝向（度）",item.rotation);
                if(LevelCatalog.IsSolid(item.type)||item.type=="ground"||item.type=="walker"||item.type=="guard")
                {item.width=EditorGUILayout.FloatField("宽度（米）",item.width);item.height=EditorGUILayout.FloatField("长度（米）",item.height);item.material=Choice("材质",item.material,LevelCatalog.Materials,LevelCatalog.MaterialNames);}
                if(item.type!="spawn"&&item.type!="ground")
                {
                    GUILayout.Label("执行动作："+(item.action=="checkpoint"?"激活安全检查点":item.action=="collect"?"合法感知后收集":item.action=="finish"?"结算本关":"播放对白 / 提示"),EditorStyles.wordWrappedMiniLabel);
                    item.radius=EditorGUILayout.FloatField("触发半径（米）",item.radius);
                    GUILayout.Label(LevelCatalog.IsWorld(item.type)?"对象相关对白通过合法感知后触发。":"进入范围后触发；不会修改玩家规则。",EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Label("对白 / 提示（可留空）");item.text=EditorGUILayout.TextArea(item.text,GUILayout.MinHeight(65));
                    item.sound=Choice("声音",item.sound,LevelCatalog.Sounds,LevelCatalog.SoundNames);
                    GUILayout.Label("声音仅为现有已注册合成占位音。此版未提供编辑器试听；实际声音请试玩。",EditorStyles.wordWrappedMiniLabel);
                    if(LevelCatalog.IsWorld(item.type)&&LevelCompiler.HasBeat(item)){item.trigger="perceived";item.target=item.id;}
                }
                if(item.type=="story")
                {
                    item.trigger=Choice("触发条件",item.trigger,new[]{"enter","start","perceived"},new[]{"进入区域","关卡开始","合法感知关联对象"});
                    string target=Reference("关联世界对象",item.target,true,item.id);
                    bool relative=EditorGUILayout.Toggle("触发区跟随关联对象",item.relative);
                    if(target!=item.target||relative!=item.relative)
                    {
                        Point2 old=LevelCompiler.Position(document,item);item.target=target;item.relative=relative&&!string.IsNullOrEmpty(target);
                        var parent=document.items.Find(i=>i.id==target);item.x=old.X-(item.relative&&parent!=null?parent.x:0);item.y=old.Y-(item.relative&&parent!=null?parent.y:0);
                    }
                }
                if(item.type=="goal"||item.type=="story")item.requires=Reference("需要先触发的剧情",item.requires,false,item.id);
                if(EditorGUI.EndChangeCheck()){TranslateMovementRoute(item,item.x-previousX,item.y-previousY);RotateMovementHeading(item,item.rotation-previousRotation);Commit("修改对象属性");}
                if(item.type=="walker"||item.type=="guard")MovementProperties(item);
                GUILayout.Space(10);if(GUILayout.Button("复制此对象")){clipboard=EncodeItem(item);Paste();}
                if(GUILayout.Button("删除此对象"))Delete();
            }
            else
            {
                document.name=EditorGUILayout.TextField("关卡名称",document.name);
                GUILayout.Label("玩家目标提示");document.goalText=EditorGUILayout.TextArea(document.goalText,GUILayout.MinHeight(55));
                document.guideWidth=EditorGUILayout.Slider("盲道宽度（米）",document.guideWidth,.2f,.8f);
                if(EditorGUI.EndChangeCheck())Commit("修改关卡属性");
                GUILayout.Label("选择画布项目后编辑属性。玩家移动、感知、永久记忆、碰撞及星级规则由游戏负责。",EditorStyles.wordWrappedLabel);
            }
            GUILayout.EndScrollView();
        }
        private void MovementProperties(LevelItem actor)
        {
            GUILayout.Space(10);GUILayout.Label("人物行走路线",EditorStyles.boldLabel);
            var route=document.movementRoutes.Find(r=>r.actorId==actor.id);
            if(route==null)
            {
                GUILayout.Label("没有路线时，人物停在摆放位置。",EditorStyles.wordWrappedMiniLabel);
                if(GUILayout.Button("添加行走路线"))
                {
                    route=new LevelMovementRoute{actorId=actor.id,loop=false};
                    route.waypoints.Add(new LevelMovementWaypoint(0,actor.x,actor.y,actor.rotation));
                    route.waypoints.Add(new LevelMovementWaypoint(2,actor.x,actor.y+2,actor.rotation));
                    document.movementRoutes.Add(route);Commit("添加人物路线");
                }
                return;
            }
            EditorGUI.BeginChangeCheck();route.loop=EditorGUILayout.Toggle("循环行走",route.loop);
            if(EditorGUI.EndChangeCheck())Commit("修改路线循环");
            GUILayout.Label("坐标是地图位置；时间是从关卡开始累计的秒数。循环末点需回到首点。选中人物后，Shift + 单击紫色节点可拖动。紫线只在编辑画布显示。",EditorStyles.wordWrappedMiniLabel);
            bool drawing=drawRouteActor==actor.id;
            bool nextDrawing=GUILayout.Toggle(drawing,"在地图点选添加路线节点","Button");
            if(nextDrawing!=drawing){drawRouteActor=nextDrawing?actor.id:null;placing=null;drawGuide=false;}
            using(new EditorGUI.DisabledScope(route.waypoints.Count==0||route.waypoints.Count>=2000))
            if(GUILayout.Button("添加回到起点的节点"))
            {
                var first=route.waypoints[0];double time=route.waypoints[route.waypoints.Count-1].timeSeconds+2;
                route.waypoints.Add(new LevelMovementWaypoint(time,first.x,first.y,first.rotationDegrees));Commit("闭合人物路线");
            }
            for(int k=0;k<route.waypoints.Count;k++)
            {
                var point=route.waypoints[k];GUILayout.Label("路线节点 "+(k+1),EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                if(k==0)GUILayout.Label("到达时间：0 秒（起点）",EditorStyles.miniLabel);
                else point.timeSeconds=EditorGUILayout.DelayedDoubleField("到达时间（秒）",point.timeSeconds);
                point.x=EditorGUILayout.DelayedFloatField("横向位置（米）",point.x);
                point.y=EditorGUILayout.DelayedFloatField("纵向位置（米）",point.y);
                point.rotationDegrees=EditorGUILayout.DelayedFloatField("朝向（度）",point.rotationDegrees);
                if(EditorGUI.EndChangeCheck())Commit("修改人物路线节点");
                if(GUILayout.Button("在地图定位此节点")){center=new Vector2(point.x,point.y);Repaint();}
                if(k>0&&GUILayout.Button("删除此路线节点"))
                {route.waypoints.RemoveAt(k);Commit("删除人物路线节点");return;}
            }
            if(GUILayout.Button("删除整条行走路线"))
            {document.movementRoutes.Remove(route);drawRouteActor=null;Commit("删除人物路线");}
        }
        private void AppendMovementPoint(LevelItem actor,Point2 position)
        {
            var route=document.movementRoutes.Find(r=>r.actorId==actor.id);if(route==null)return;
            if(route.waypoints.Count>=2000){status="路线最多 2000 个节点，请先删除多余节点。";return;}
            double time=route.waypoints.Count==0?0:route.waypoints[route.waypoints.Count-1].timeSeconds+2;
            route.waypoints.Add(new LevelMovementWaypoint(time,position.X,position.Y,actor.rotation));
            Commit("添加人物路线节点");
        }
        private void TranslateMovementRoute(LevelItem actor,float dx,float dy)
        {
            if(dx==0&&dy==0)return;
            foreach(var route in document.movementRoutes)if(route.actorId==actor.id)
                foreach(var point in route.waypoints){point.x+=dx;point.y+=dy;}
        }
        private void RotateMovementHeading(LevelItem actor,float delta)
        {
            if(delta==0)return;
            foreach(var route in document.movementRoutes)if(route.actorId==actor.id)
                foreach(var point in route.waypoints)point.rotationDegrees+=delta;
        }
        private static string Choice(string label,string value,string[] values,string[] names)
        {int old=Array.IndexOf(values,value);int index=EditorGUILayout.Popup(label,Mathf.Max(0,old),names);return index>=0?values[index]:value;}
        private string Reference(string label,string value,bool worldOnly,string excluded)
        {
            var ids=new List<string>{""};var names=new List<string>{"无"};
            foreach(var i in document.items)if(i.id!=excluded&&(worldOnly?LevelCatalog.IsWorld(i.type):LevelCompiler.HasBeat(i))){ids.Add(i.id);names.Add(i.name);}
            int index=ids.IndexOf(value);if(index<0){ids.Add(value);names.Add("已丢失的关联（请重选）");index=ids.Count-1;}
            return ids[EditorGUILayout.Popup(label,index,names.ToArray())];
        }
        private void IssuesPanel()
        {
            GUILayout.Label(string.IsNullOrEmpty(status)?"检查结果：点击问题可定位。检查通过仍需真人试玩。":status,EditorStyles.wordWrappedLabel);
            issuesScroll=GUILayout.BeginScrollView(issuesScroll);
            foreach(var i in issues)if(GUILayout.Button((i.Error?"错误：":"提示：")+i.Message,EditorStyles.miniButton))Locate(i.ItemId);
            GUILayout.EndScrollView();
        }
        private bool Check()
        {issues=LevelValidator.Check(document);int errors=issues.FindAll(i=>i.Error).Count;status=errors==0?"关卡检查：无阻断错误；警告请逐项核对。":"发现 "+errors+" 个错误，试玩前请修复。";return errors==0;}
        private void Locate(string id)
        {
            if(string.IsNullOrEmpty(id))return;drawRouteActor=null;
            var route=document.movementRoutes.Find(r=>r.id==id||r.waypoints.Exists(p=>p.id==id));
            if(route!=null)id=route.actorId;
            selectedId=id;selectedNode=document.guide.FindIndex(n=>n.id==id);
            var i=document.items.Find(a=>a.id==id);Point2 p=i!=null?LevelCompiler.Position(document,i):selectedNode>=0?new Point2(document.guide[selectedNode].x,document.guide[selectedNode].y):new Point2(center.x,center.y);center=new Vector2(p.X,p.Y);Repaint();
        }
        private void FrameAll()
        {
            float minX=0,maxX=0,minY=0,maxY=0;
            foreach(var i in document.items){var p=LevelCompiler.Position(document,i);minX=Mathf.Min(minX,p.X-i.width/2);maxX=Mathf.Max(maxX,p.X+i.width/2);minY=Mathf.Min(minY,p.Y-i.height/2);maxY=Mathf.Max(maxY,p.Y+i.height/2);}
            foreach(var n in document.guide){minX=Mathf.Min(minX,n.x);maxX=Mathf.Max(maxX,n.x);minY=Mathf.Min(minY,n.y);maxY=Mathf.Max(maxY,n.y);}
            foreach(var route in document.movementRoutes)foreach(var n in route.waypoints)
            {minX=Mathf.Min(minX,n.x);maxX=Mathf.Max(maxX,n.x);minY=Mathf.Min(minY,n.y);maxY=Mathf.Max(maxY,n.y);}
            center=new Vector2((minX+maxX)/2,(minY+maxY)/2);scale=Mathf.Clamp(Mathf.Min(Mathf.Max(400,position.width-500)/(maxX-minX+6),Mathf.Max(300,position.height-240)/(maxY-minY+6)),.002f,50);Repaint();
        }
        private void Open()
        {
            if(!SwitchAllowed())return;
            try
            {
                string path=EditorUtility.OpenFilePanel("打开关卡",EditorProjectPaths.DataDirectory,"json");if(string.IsNullOrEmpty(path))return;
                path=EditorProjectPaths.AbsoluteFilePath(path);var d=LevelCodec.Load(path);SetDocument(d,path);Check();
            }
            catch(Exception e){EditorUtility.DisplayDialog("无法打开",e.Message+"\n原文件保留。","知道了");}
        }
        private bool Save(bool saveAs)
        {
            try
            {
                string currentPath="";
                if(!string.IsNullOrEmpty(filePath))
                {
                    try{currentPath=EditorProjectPaths.ResolveFilePath(filePath,fileAssetGuid);}
                    catch(Exception){if(!saveAs)throw;}
                }
                string path=currentPath;
                if(saveAs||string.IsNullOrEmpty(path))path=EditorUtility.SaveFilePanel("保存关卡草稿",EditorProjectPaths.DataDirectory,document.name,"json");
                if(string.IsNullOrEmpty(path))return false;
                path=EditorProjectPaths.AbsoluteFilePath(path);
                // Save As to a new file gives a new identity; repeated saving and normal moves keep IDs.
                bool independent=saveAs&&!string.IsNullOrEmpty(filePath)&&(string.IsNullOrEmpty(currentPath)||!EditorProjectPaths.SameFilePath(path,currentPath));
                var toSave=independent?LevelCodec.Duplicate(document):document;
                toSave.name=document.name;LevelCodec.Save(path,toSave,false);
                if(toSave!=document){document=toSave;Undo.ClearUndo(buffer);buffer.json=LevelCodec.Encode(document);selectedId="";selectedNode=-1;}
                AssetDatabase.Refresh();RememberFilePath(path);savedJson=buffer.json;UpdateUnsaved();status="草稿已安全保存。试玩会先检查配置；.bak 为上次保存备份。";return true;
            }
            catch(Exception e){EditorUtility.DisplayDialog("保存失败",e.Message+"\n原文件保留，请选择新位置。","知道了");return false;}
        }
        private void Preview()
        {if(!Check())return;LevelPreviewHost.Start(LevelCodec.Clone(document));status="试玩使用当前数据与独立临时存档。Unity 顶部停止按钮结束后回到本工作台。";}
        private void Delete()
        {
            if(selectedNode>=0){document.guide.RemoveAt(selectedNode);}
            else{var i=document.items.Find(a=>a.id==selectedId);if(i==null)return;document.movementRoutes.RemoveAll(r=>r.actorId==i.id);document.items.Remove(i);if(drawRouteActor==i.id)drawRouteActor=null;}
            selectedId="";selectedNode=-1;Commit("删除项目");
        }
        private string EncodeItem(LevelItem i)
        {var d=new LevelDocument();d.items.Add(i);var route=LevelCodec.Clone(document).movementRoutes.Find(r=>r.actorId==i.id);if(route!=null)d.movementRoutes.Add(route);return LevelCodec.Encode(d);}
        private void Paste()
        {
            if(string.IsNullOrEmpty(clipboard))return;var copied=LevelCodec.Decode(clipboard);var i=LevelCodec.DuplicateItem(copied.items[0]);i.x+=.5f;i.y+=.5f;document.items.Add(i);
            foreach(var route in copied.movementRoutes)
            {route.id=LevelDocument.NewId();route.actorId=i.id;foreach(var point in route.waypoints){point.id=LevelDocument.NewId();point.x+=.5f;point.y+=.5f;}document.movementRoutes.Add(route);}
            selectedId=i.id;selectedNode=-1;Commit("复制粘贴对象");
        }
        private void Shortcuts()
        {
            Event e=Event.current;if(e.type!=EventType.KeyDown||EditorGUIUtility.editingTextField||GUIUtility.keyboardControl!=0)return;
            bool control=e.control||e.command;
            if(e.keyCode==KeyCode.Escape){placing=null;drawGuide=false;drawRouteActor=null;e.Use();Repaint();return;}
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(e.keyCode==KeyCode.Delete||e.keyCode==KeyCode.Backspace){Delete();e.Use();}
            else if(control&&e.keyCode==KeyCode.S){Save(false);e.Use();}
            else if(control&&e.keyCode==KeyCode.Z){if(e.shift)Undo.PerformRedo();else Undo.PerformUndo();e.Use();}
            else if(control&&e.keyCode==KeyCode.Y){Undo.PerformRedo();e.Use();}
            else if(control&&e.keyCode==KeyCode.C){var i=document.items.Find(a=>a.id==selectedId);if(i!=null)clipboard=EncodeItem(i);e.Use();}
            else if(control&&e.keyCode==KeyCode.V){Paste();e.Use();}
        }
        private static void Help()
        {
            EditorUtility.DisplayDialog("第一次制作关卡","1. 点击第一步模板，再点复制关卡，获得独立副本。\n2. 在地图选中电线杆并拖动；白色朝向端点可拖动旋转。\n3. 左侧点剧情提示，再点地图；右侧填写对白。对象对白需要合法感知；独立提示可进入区域触发。\n4. 连续绘制盲道：点地图加节点，退出绘制后拖动节点；右侧可插入和删除。\n5. 保存为 JSON 草稿（无需手改），检查错误后试玩。\n6. 点击 Unity 顶部停止按钮结束试玩。\n\n此版本需要 Unity 2022.3。音效为合成占位；暂无正式配音、车辆路径、天气或任意脚本节点。完整说明见 docs/LEVEL_EDITOR_GUIDE.md。","开始制作");
        }
    }
}
