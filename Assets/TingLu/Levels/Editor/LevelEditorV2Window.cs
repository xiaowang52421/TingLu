using System;
using System.Collections.Generic;
using System.IO;
using TingLu.Contracts;
using TingLu.Levels;
using UnityEditor;
using UnityEngine;

namespace TingLu.Levels.Editor
{
    /// <summary>中文街区 schema 2 编辑器。它与旧 M0 LevelEditorWindow 保持独立。</summary>
    public sealed class LevelEditorV2Window : EditorWindow
    {
        private const string DraftKey = "TingLu.LevelEditorV2.Draft";
        [SerializeField] private LevelEditBuffer buffer;
        [SerializeField] private string filePath = "", savedJson = "", fileAssetGuid = "";
        [SerializeField] private Vector2 center = new Vector2(0, 0);
        [SerializeField] private float scale = 12f;
        [SerializeField] private int tab;
        [SerializeField] private string selectedKind = "", selectedId = "", selectedRoute = "";
        [SerializeField] private string placingKind = "", placingId = "";
        private StreetLevelDocument document;
        private readonly List<string> issues = new List<string>();
        private Vector2 issueScroll, listScroll, inspectorScroll, paletteScroll;
        private Rect canvas;
        private bool dragging, panning;
        private Point2 dragWorld;
        private Vector2 lastMouse;
        private string dragOriginal = "";
        private int selectedRoutePoint = -1;
        private bool Dirty { get { return buffer != null && buffer.json != savedJson; } }

        [MenuItem("TingLu/关卡编辑器", false, 200)]
        [MenuItem("TingLu/新版关卡编辑器", false, 201)]
        public static void ShowWindow()
        {
            var window = GetWindow<LevelEditorV2Window>();
            window.titleContent = new GUIContent("听路 · 新版街区编辑器");
            window.minSize = new Vector2(1100, 700);
            window.Show();
        }

        private void OnEnable()
        {
            if (buffer == null)
            {
                buffer = CreateInstance<LevelEditBuffer>();
                buffer.hideFlags = HideFlags.HideAndDontSave;
            }
            string draft = SessionState.GetString(DraftKey, "");
            if (string.IsNullOrEmpty(buffer.json) && !string.IsNullOrEmpty(draft)) buffer.json = draft;
            if (string.IsNullOrEmpty(buffer.json)) buffer.json = StreetLevelCodec.Encode(StreetLevelCodec.Template());
            try { document = StreetLevelCodec.Decode(buffer.json); }
            catch (Exception error)
            {
                document = StreetLevelCodec.Template();
                buffer.json = StreetLevelCodec.Encode(document);
                issues.Clear(); issues.Add("工作草稿无法恢复，已打开安全模板：" + error.Message);
            }
            // A newly created template has no file and must show as unsaved. A restored
            // file keeps its serialized savedJson; a SessionState draft remains dirty.
            if (string.IsNullOrEmpty(savedJson) && !string.IsNullOrEmpty(filePath)) savedJson = buffer.json;
            hasUnsavedChanges = Dirty;
            saveChangesMessage = "新版街区关卡有未保存修改。保存、放弃或取消关闭。";
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.delayCall += RepaintLater;
        }

        private void RepaintLater() { if (this != null) Repaint(); }

        private void OnDisable()
        {
            EditorApplication.delayCall -= RepaintLater;
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (Dirty && buffer != null && !string.IsNullOrEmpty(buffer.json)) SessionState.SetString(DraftKey, buffer.json);
        }

        private void OnUndoRedo()
        {
            try { document = StreetLevelCodec.Decode(buffer.json); issues.Clear(); UpdateDirty(); Repaint(); }
            catch (Exception error) { issues.Clear(); issues.Add("撤销/重做结果无法读取：" + error.Message); }
        }

        private void OnDestroy()
        {
            if (buffer != null) DestroyImmediate(buffer);
        }

        public override void SaveChanges()
        {
            if (Save(false)) base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            if (buffer != null) buffer.json = savedJson;
            SessionState.EraseString(DraftKey);
            base.DiscardChanges();
        }

        private void UpdateDirty()
        {
            hasUnsavedChanges = Dirty;
            saveChangesMessage = "新版街区关卡有未保存修改。保存、放弃或取消关闭。";
        }

        private void Commit(string label)
        {
            if (buffer == null || document == null) return;
            string after;
            try { after = StreetLevelCodec.Encode(document); StreetLevelCodec.Decode(after); }
            catch (Exception error) { try { document = StreetLevelCodec.Decode(buffer.json); } catch { document = StreetLevelCodec.Template(); } issues.Clear(); issues.Add("修改未应用：" + error.Message); Repaint(); return; }
            if (after == buffer.json) return;
            Undo.RecordObject(buffer, label);
            buffer.json = after;
            EditorUtility.SetDirty(buffer);
            issues.Clear(); UpdateDirty(); Repaint();
        }

        private void SetDocument(StreetLevelDocument value, string path)
        {
            document = value;
            Undo.ClearUndo(buffer);
            buffer.json = StreetLevelCodec.Encode(value);
            savedJson = string.IsNullOrEmpty(path) ? "" : buffer.json;
            filePath = EditorProjectPaths.StoreFilePath(path);
            fileAssetGuid = EditorProjectPaths.FileAssetGuid(path);
            selectedKind = selectedId = selectedRoute = placingKind = placingId = ""; selectedRoutePoint = -1;
            dragging = panning = false;
            issues.Clear(); SessionState.EraseString(DraftKey); FrameAll(); UpdateDirty();
        }

        private void OnGUI()
        {
            if (document == null) return;
            try
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                HandleShortcuts();
                DrawToolbar();
                DrawTabs();
                if (tab == 0) DrawGuide();
                else if (tab == 1) DrawMapWorkspace();
                else if (tab == 2) DrawObjectWorkspace();
                else DrawCheckWorkspace();
                }
            }
            catch (ExitGUIException) { throw; }
            catch (Exception error) { issues.Clear(); issues.Add("操作未完成：" + error.Message); Repaint(); }
        }

        private void DrawToolbar()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (GUILayout.Button("新建十字路口模板", EditorStyles.toolbarButton)) NewDocument(StreetLevelCodec.Template());
                if (GUILayout.Button("空白街区", EditorStyles.toolbarButton)) NewDocument(Blank());
                if (GUILayout.Button("打开", EditorStyles.toolbarButton)) Open();
                if (GUILayout.Button("保存", EditorStyles.toolbarButton)) Save(false);
                if (GUILayout.Button("另存为", EditorStyles.toolbarButton)) Save(true);
                if (GUILayout.Button("撤销", EditorStyles.toolbarButton)) Undo.PerformUndo();
                if (GUILayout.Button("重做", EditorStyles.toolbarButton)) Undo.PerformRedo();
                if (GUILayout.Button("检查关卡", EditorStyles.toolbarButton)) Check();
                if (GUILayout.Button("试玩", EditorStyles.toolbarButton)) Preview();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (GUILayout.Button("导出JSON", EditorStyles.toolbarButton, GUILayout.Width(100))) ExportJson();
                if (GUILayout.Button("导出可玩场景", EditorStyles.toolbarButton, GUILayout.Width(120))) ExportScene();
                GUILayout.Label(document.title + (Dirty ? "  ● 未保存" : "  ✓ 已保存"), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Ctrl+S 保存 · Esc 取消放置", EditorStyles.miniLabel);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            string[] labels = { "开始向导", "地图画布", "对象与路线", "检查与试玩" };
            for (int i = 0; i < labels.Length; i++) if (GUILayout.Toggle(tab == i, labels[i], EditorStyles.toolbarButton, GUILayout.Width(105))) tab = i;
            GUILayout.FlexibleSpace();
            GUILayout.Label("街区关卡 · 中文制作工作台", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private void DrawGuide()
        {
            GUILayout.BeginArea(new Rect(22, 100, position.width - 44, position.height - 125), GUI.skin.box);
            GUILayout.Label("新版街区关卡编辑器", EditorStyles.largeLabel);
            GUILayout.Label("按四步完成一个可试玩的街区：先从模板开始，再在画布移动地面和对象，最后检查并导出。所有数据使用新版 schema 2，和旧版第一步编辑器完全分开。", EditorStyles.wordWrappedLabel);
            GUILayout.Space(12);
            GUILayout.Label("当前关卡", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            document.title = EditorGUILayout.TextField("标题", document.title);
            document.goalText = EditorGUILayout.TextField("目标提示", document.goalText);
            if (EditorGUI.EndChangeCheck()) Commit("编辑关卡信息");
            GUILayout.Space(10);
            GUILayout.Label("建议顺序", EditorStyles.boldLabel);
            GUILayout.Label("1. 点击“地图画布”，用左侧素材按钮添加地面、建筑、障碍和声源。\n2. 在右侧检查器调整尺寸、材质、路线和检查点。\n3. 点击“检查与试玩”修复错误，再用“导出可玩场景”生成独立 Unity 场景。", EditorStyles.wordWrappedLabel);
            GUILayout.Space(12);
            if (GUILayout.Button("进入地图画布", GUILayout.Width(220))) tab = 1;
            GUILayout.EndArea();
        }

        private void DrawMapWorkspace()
        {
            float top = 112, bottom = position.height - 36, left = 235, right = 330;
            canvas = new Rect(left, top, Mathf.Max(180, position.width - left - right), Mathf.Max(180, bottom - top));
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                GUILayout.BeginArea(new Rect(0, top, left - 5, bottom - top), GUI.skin.box); DrawPalette(); GUILayout.EndArea();
                DrawCanvas();
                GUILayout.BeginArea(new Rect(position.width - right + 5, top, right - 10, bottom - top), GUI.skin.box); DrawInspector(); GUILayout.EndArea();
            }
        }

        private void DrawPalette()
        {
            paletteScroll = GUILayout.BeginScrollView(paletteScroll);
            GUILayout.Label("素材与工具", EditorStyles.boldLabel);
            GUILayout.Label("点击按钮后在画布放置；已存在对象可在右侧检查器编辑。", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(5); GUILayout.Label("地面", EditorStyles.miniBoldLabel);
            for (int i = 0; i < StreetEditorCatalog.SurfaceMaterialIds.Length; i++)
            {
                string id = StreetEditorCatalog.SurfaceMaterialIds[i];
                if (GUILayout.Button(StreetEditorCatalog.SurfaceMaterialNames[i], EditorStyles.miniButton)) ArmPlacement("surface", id);
            }
            GUILayout.Space(5); GUILayout.Label("对象", EditorStyles.miniBoldLabel);
            for (int i = 0; i < StreetEditorCatalog.ObjectTypeIds.Length; i++)
            {
                string type = StreetEditorCatalog.ObjectTypeIds[i];
                if (GUILayout.Button(StreetEditorCatalog.ObjectTypeNames[i], EditorStyles.miniButton)) ArmPlacement("object", type);
            }
            GUILayout.Space(5);
            if (GUILayout.Button("添加检查点", EditorStyles.miniButton)) ArmPlacement("checkpoint", "checkpoint");
            if (GUILayout.Button("让选中人物或车辆开始移动", EditorStyles.miniButton)) AddRoute();
            if (GUILayout.Button("查看整个关卡", EditorStyles.miniButton)) FrameAll();
            if (!string.IsNullOrEmpty(placingKind))
                GUILayout.Label("画布点一下放置：" + (placingKind == "surface" ? StreetEditorCatalog.SurfaceName(placingId) : placingKind == "object" ? StreetEditorCatalog.ObjectName(placingId) : "检查点"), EditorStyles.helpBox);
            GUILayout.Space(7);
            GUILayout.Label("对象列表", EditorStyles.miniBoldLabel);
            foreach (var surface in document.surfaces) if (surface != null && GUILayout.Button("地面 · " + surface.id, EditorStyles.miniButton)) Select("surface", surface.id);
            foreach (var item in document.objects) if (item != null && GUILayout.Button("对象 · " + StreetEditorCatalog.ObjectName(StreetEditorCatalog.GetObjectType(item, document)) + " · " + item.id, EditorStyles.miniButton)) Select("object", item.id);
            foreach (var checkpoint in document.checkpoints) if (checkpoint != null && GUILayout.Button("检查点 · " + checkpoint.id, EditorStyles.miniButton)) Select("checkpoint", checkpoint.id);
            foreach (var route in document.routes) if (route != null && GUILayout.Button("路线 · " + route.objectId, EditorStyles.miniButton)) SelectRoute(route.objectId);
            GUILayout.EndScrollView();
        }

        private Vector2 Screen(Point2 point) { return new Vector2(canvas.center.x + (point.X - center.x) * scale, canvas.center.y - (point.Y - center.y) * scale); }
        private Point2 World(Vector2 screen)
        { return new Point2(center.x + (screen.x - canvas.center.x) / scale, center.y - (screen.y - canvas.center.y) / scale); }

        private void DrawCanvas()
        {
            EditorGUI.DrawRect(canvas, new Color(.08f, .10f, .12f));
            GUI.BeginClip(canvas);
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                Handles.color = new Color(.18f, .22f, .24f);
                float step = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(Mathf.Max(1, 60 / scale))));
                float xmin = center.x - canvas.width / (2 * scale), xmax = center.x + canvas.width / (2 * scale);
                float ymin = center.y - canvas.height / (2 * scale), ymax = center.y + canvas.height / (2 * scale);
                for (float x = Mathf.Floor(xmin / step) * step; x <= xmax; x += step) DrawLine(new Point2(x, ymin), new Point2(x, ymax), 1);
                for (float y = Mathf.Floor(ymin / step) * step; y <= ymax; y += step) DrawLine(new Point2(xmin, y), new Point2(xmax, y), 1);
                foreach (var s in document.surfaces) if (s != null) DrawPolygon(s.polygon, StreetEditorCatalog.GetSurfaceColor(s.materialId), selectedKind == "surface" && selectedId == s.id);
                foreach (var item in document.objects) if (item != null) { DrawPolygon(item.polygon, StreetEditorCatalog.GetObjectColor(StreetEditorCatalog.GetObjectType(item, document)), selectedKind == "object" && selectedId == item.id); if (item.polygon == null || item.polygon.Count < 3) DrawMarker(item.position == null ? new Point2() : item.position.ToPoint(), StreetEditorCatalog.GetObjectColor(StreetEditorCatalog.GetObjectType(item, document)), StreetEditorCatalog.ObjectName(StreetEditorCatalog.GetObjectType(item, document)), selectedKind == "object" && selectedId == item.id); }
                foreach (var route in document.routes) if (route != null) DrawRoute(route);
                DrawMarker(new Point2(document.spawnX, document.spawnY), Color.cyan, "出生", selectedKind == "spawn");
                DrawMarker(new Point2(document.goalX, document.goalY), Color.green, "终点", selectedKind == "goal");
                foreach (var checkpoint in document.checkpoints) if (checkpoint != null) DrawMarker(checkpoint.position.ToPoint(), new Color(1, .75f, .15f), "检查点", selectedKind == "checkpoint" && selectedId == checkpoint.id);
                Handles.EndGUI();
            }
            GUI.EndClip();
            GUI.Label(new Rect(canvas.x + 8, canvas.y + 7, canvas.width - 16, 20), "地图北向 +Y · 滚轮缩放 · 中键/Alt+左键平移 · 当前比例 " + scale.ToString("0.0") + " px/m", EditorStyles.miniLabel);
            CanvasInput();
        }

        private void DrawLine(Point2 a, Point2 b, float width) { Handles.DrawAAPolyLine(width, Screen(a) - canvas.position, Screen(b) - canvas.position); }
        private void DrawPolygon(List<StreetPointData> polygon, Color color, bool selected)
        {
            if (polygon == null || polygon.Count == 0) return;
            var fill = new Vector3[polygon.Count];
            var points = new Vector3[polygon.Count + 1];
            for (int i = 0; i < polygon.Count; i++) fill[i] = points[i] = Screen(polygon[i].ToPoint()) - canvas.position;
            points[polygon.Count] = points[0];
            if (polygon.Count >= 3 && IsConvex(polygon)) { Handles.color = new Color(color.r, color.g, color.b, .14f); Handles.DrawAAConvexPolygon(fill); }
            Handles.color = new Color(color.r, color.g, color.b, .9f); Handles.DrawAAPolyLine(selected ? 3 : 1.6f, points);
            if (selected) for (int i = 0; i < polygon.Count; i++) Handles.DrawSolidDisc(points[i], Vector3.forward, 3.5f);
        }
        private static bool IsConvex(List<StreetPointData> polygon)
        { float sign = 0; for (int i = 0; i < polygon.Count; i++) { var a = polygon[i].ToPoint(); var b = polygon[(i + 1) % polygon.Count].ToPoint(); var c = polygon[(i + 2) % polygon.Count].ToPoint(); float cross = StreetGeometry.Cross(a, b, c); if (Mathf.Abs(cross) < .00001f) continue; if (sign == 0) sign = Mathf.Sign(cross); else if (Mathf.Sign(cross) != sign) return false; } return sign != 0; }
        private void DrawRoute(StreetRouteData route)
        {
            if (route.points == null) return; Handles.color = new Color(.95f, .35f, .95f, .9f);
            for (int i = 1; i < route.points.Count; i++) DrawLine(route.points[i - 1].ToPoint(), route.points[i].ToPoint(), route.objectId == selectedRoute ? 3 : 1.5f);
            if (route.objectId == selectedRoute) for (int i = 0; i < route.points.Count; i++) { Vector2 p = Screen(route.points[i].ToPoint()) - canvas.position; Handles.DrawWireDisc(p, Vector3.forward, i == selectedRoutePoint ? 7 : 4); GUI.Label(new Rect(p.x + 6, p.y - 10, 90, 20), "节点 " + (i + 1)); }
        }
        private void DrawMarker(Point2 point, Color color, string label, bool selected)
        { Vector2 p = Screen(point) - canvas.position; Handles.color = selected ? Color.white : color; Handles.DrawWireDisc(p, Vector3.forward, selected ? 9 : 6); GUI.Label(new Rect(p.x + 7, p.y - 10, 90, 20), label); }

        private void CanvasInput()
        {
            if (!GUI.enabled) return;
            Event e = Event.current; bool inside = canvas.Contains(e.mousePosition);
            if (e.type == EventType.ScrollWheel && inside) { Point2 before = World(e.mousePosition); scale = Mathf.Clamp(scale * Mathf.Exp(-e.delta.y * .08f), .4f, 100); Point2 after = World(e.mousePosition); center += new Vector2(before.X - after.X, before.Y - after.Y); e.Use(); Repaint(); return; }
            if (e.type == EventType.MouseDown && inside)
            {
                if (e.button == 2 || (e.button == 0 && e.alt)) { panning = true; lastMouse = e.mousePosition; e.Use(); return; }
                if (e.button != 0) return;
                if (!string.IsNullOrEmpty(placingKind))
                {
                    Point2 at = World(e.mousePosition);
                    if (placingKind == "surface") AddSurfaceAt(placingId, at);
                    else if (placingKind == "object") AddObjectAt(placingId, at);
                    else AddCheckpointAt(at);
                    placingKind = placingId = ""; e.Use(); return;
                }
                SelectAt(e.mousePosition); dragWorld = World(e.mousePosition); dragOriginal = buffer.json; dragging = !string.IsNullOrEmpty(selectedKind); e.Use(); Repaint(); return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { placingKind = placingId = ""; selectedKind = selectedId = selectedRoute = ""; selectedRoutePoint = -1; e.Use(); Repaint(); return; }
            if (e.type == EventType.MouseDrag && (panning || dragging))
            {
                if (panning) { Vector2 delta = e.mousePosition - lastMouse; center += new Vector2(-delta.x / scale, delta.y / scale); lastMouse = e.mousePosition; }
                else { Point2 now = World(e.mousePosition); MoveSelection(now.X - dragWorld.X, now.Y - dragWorld.Y); dragWorld = now; }
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseUp && (panning || dragging))
            { if (dragging) Commit("移动地图几何"); panning = dragging = false; e.Use(); }
        }

        private void SelectAt(Vector2 mouse)
        {
            Point2 world = World(mouse); selectedKind = selectedId = selectedRoute = ""; selectedRoutePoint = -1;
            for (int r = document.routes.Count - 1; r >= 0; r--) { var route = document.routes[r]; if (route == null || route.points == null) continue; for (int p = route.points.Count - 1; p >= 0; p--) if (Vector2.Distance(Screen(route.points[p].ToPoint()), mouse) < 10) { SelectRoute(route.objectId); selectedRoutePoint = p; return; } }
            for (int r = document.routes.Count - 1; r >= 0; r--) { var route = document.routes[r]; if (route == null || route.points == null) continue; for (int p = 1; p < route.points.Count; p++) if (DistanceToSegment(mouse, Screen(route.points[p - 1].ToPoint()), Screen(route.points[p].ToPoint())) < 6) { SelectRoute(route.objectId); return; } }
            if (Vector2.Distance(Screen(new Point2(document.spawnX, document.spawnY)), mouse) < 12) { selectedKind = "spawn"; return; }
            if (Vector2.Distance(Screen(new Point2(document.goalX, document.goalY)), mouse) < 12) { selectedKind = "goal"; return; }
            for (int i = document.checkpoints.Count - 1; i >= 0; i--) if (document.checkpoints[i] != null && Vector2.Distance(Screen(document.checkpoints[i].position.ToPoint()), mouse) < 12) { Select("checkpoint", document.checkpoints[i].id); return; }
            for (int i = document.objects.Count - 1; i >= 0; i--) if (Hit(document.objects[i].polygon, world) || Vector2.Distance(Screen(document.objects[i].position.ToPoint()), mouse) < 12) { Select("object", document.objects[i].id); return; }
            for (int i = document.surfaces.Count - 1; i >= 0; i--) if (Hit(document.surfaces[i].polygon, world)) { Select("surface", document.surfaces[i].id); return; }
        }
        private bool Hit(List<StreetPointData> polygon, Point2 point)
        { if (polygon == null || polygon.Count < 3) return false; var p = new List<Point2>(); foreach (var x in polygon) p.Add(x.ToPoint()); return StreetGeometry.Contains(p, point); }
        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        { Vector2 delta = b - a; float length = delta.sqrMagnitude; if (length < .0001f) return Vector2.Distance(point, a); float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / length); return Vector2.Distance(point, a + t * delta); }
        private void MoveSelection(float dx, float dy)
        {
            if (selectedKind == "spawn") { document.spawnX += dx; document.spawnY += dy; return; }
            if (selectedKind == "goal") { document.goalX += dx; document.goalY += dy; return; }
            if (selectedKind == "checkpoint") { var c = document.checkpoints.Find(x => x != null && x.id == selectedId); if (c != null) { c.position.x += dx; c.position.y += dy; } return; }
            if (selectedRoutePoint >= 0) { var route = document.routes.Find(x => x != null && x.objectId == selectedRoute); if (route != null && selectedRoutePoint < route.points.Count) { MoveRoutePoint(route, selectedRoutePoint, route.points[selectedRoutePoint].x + dx, route.points[selectedRoutePoint].y + dy); } return; }
            if (selectedKind == "surface") { var s = document.surfaces.Find(x => x != null && x.id == selectedId); if (s != null) { foreach (var p in s.polygon) { p.x += dx; p.y += dy; } s.heightBase -= s.heightSlopeX * dx + s.heightSlopeY * dy; } return; }
            if (selectedKind == "object") { var o = document.objects.Find(x => x != null && x.id == selectedId); if (o != null) { o.position.x += dx; o.position.y += dy; foreach (var p in o.polygon) { p.x += dx; p.y += dy; } foreach (var route in document.routes) if (route != null && route.objectId == o.id) foreach (var point in route.points) { point.x += dx; point.y += dy; } } }
        }

        private void MoveRoutePoint(StreetRouteData route, int index, float x, float y)
        {
            var point = route.points[index]; float dx = x - point.x, dy = y - point.y;
            point.x = x; point.y = y;
            if (index != 0) return;
            var actor = document.objects.Find(item => item.id == route.objectId);
            if (actor == null) return;
            actor.position.x += dx; actor.position.y += dy;
            foreach (var vertex in actor.polygon) { vertex.x += dx; vertex.y += dy; }
        }

        private void DrawObjectWorkspace()
        {
            float top = 110;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
            GUILayout.BeginArea(new Rect(10, top, 330, position.height - top - 25), GUI.skin.box);
            GUILayout.Label("地面 / 对象 / 检查点 / 路线", EditorStyles.boldLabel);
            listScroll = GUILayout.BeginScrollView(listScroll);
            foreach (var s in document.surfaces) if (s != null && GUILayout.Button("地面 · " + StreetEditorCatalog.SurfaceName(s.materialId) + " · " + s.id)) Select("surface", s.id);
            foreach (var o in document.objects) if (o != null && GUILayout.Button("对象 · " + StreetEditorCatalog.ObjectName(StreetEditorCatalog.GetObjectType(o, document)) + " · " + o.id)) Select("object", o.id);
            foreach (var c in document.checkpoints) if (c != null && GUILayout.Button("检查点 · " + c.id)) Select("checkpoint", c.id);
            foreach (var r in document.routes) if (r != null && GUILayout.Button("路线 · " + r.objectId)) SelectRoute(r.objectId);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(350, top, Mathf.Max(300, position.width - 360), position.height - top - 25), GUI.skin.box);
            DrawInspector();
            GUILayout.EndArea();
            }
        }

        private void DrawCheckWorkspace()
        {
            GUILayout.BeginArea(new Rect(18, 105, position.width - 36, position.height - 125), GUI.skin.box);
            GUILayout.Label("检查与试玩", EditorStyles.largeLabel);
            if (GUILayout.Button("重新检查", GUILayout.Width(180))) Check();
            int errors = 0; foreach (var text in issues) if (text.StartsWith("错误")) errors++;
            GUILayout.Label(issues.Count == 0 ? "尚未检查；点击重新检查。" : (errors == 0 ? "检查完成：没有阻断错误。试玩前仍请听感和人工验收。" : "发现 " + errors + " 个错误，请修复后再试玩。"), EditorStyles.wordWrappedLabel);
            issueScroll = GUILayout.BeginScrollView(issueScroll);
            foreach (string issue in issues) GUILayout.Label(issue, EditorStyles.wordWrappedLabel);
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            if (GUILayout.Button("试玩当前文档", GUILayout.Width(180))) Preview();
            if (GUILayout.Button("导出可玩场景", GUILayout.Width(180))) ExportScene();
            GUILayout.EndArea();
        }

        private void DrawInspector()
        {
            inspectorScroll = GUILayout.BeginScrollView(inspectorScroll);
            GUILayout.Label("属性检查器", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            if (selectedKind == "spawn") { GUILayout.Label("出生点", EditorStyles.miniBoldLabel); FloatField("X", ref document.spawnX); FloatField("Y", ref document.spawnY); FloatField("朝向 X", ref document.facingX); FloatField("朝向 Y", ref document.facingY); }
            else if (selectedKind == "goal") { GUILayout.Label("终点", EditorStyles.miniBoldLabel); FloatField("X", ref document.goalX); FloatField("Y", ref document.goalY); FloatField("半径", ref document.goalRadius); }
            else if (selectedKind == "surface") { var s = document.surfaces.Find(x => x != null && x.id == selectedId); if (s != null) SurfaceInspector(s); }
            else if (selectedKind == "object") { var o = document.objects.Find(x => x != null && x.id == selectedId); if (o != null) ObjectInspector(o); }
            else if (selectedKind == "checkpoint") { var c = document.checkpoints.Find(x => x != null && x.id == selectedId); if (c != null) CheckpointInspector(c); }
            else if (!string.IsNullOrEmpty(selectedRoute)) { var route = document.routes.Find(x => x != null && x.objectId == selectedRoute); if (route != null) RouteInspector(route); }
            else GUILayout.Label("点击画布或左侧列表中的地面、对象、出生点、终点、检查点或路线节点。", EditorStyles.wordWrappedMiniLabel);
            if (EditorGUI.EndChangeCheck()) Commit("编辑属性");
            GUILayout.EndScrollView();
        }

        private void SurfaceInspector(StreetSurfaceData s)
        {
            GUILayout.Label("地面 · " + s.id, EditorStyles.miniBoldLabel);
            int material = Array.IndexOf(StreetEditorCatalog.SurfaceMaterialIds, s.materialId); int current = Mathf.Max(0, material); int next = EditorGUILayout.Popup("材质", current, StreetEditorCatalog.SurfaceMaterialNames); if (next != current && next >= 0) s.materialId = StreetEditorCatalog.SurfaceMaterialIds[next];
            ReadOnlyId("地面编号", s.id);
            IntField("优先级", ref s.priority); FloatField("纹理轴 X", ref s.textureAxis.x); FloatField("纹理轴 Y", ref s.textureAxis.y); FloatField("基础高度", ref s.heightBase); FloatField("X 坡度", ref s.heightSlopeX); FloatField("Y 坡度", ref s.heightSlopeY); DrawPolygonInspector(s.polygon); DrawSizeControls(s.polygon);
            if (GUILayout.Button("删除地面")) { document.surfaces.Remove(s); selectedKind = selectedId = ""; Commit("删除地面"); }
        }
        private void ObjectInspector(StreetObjectData o)
        {
            string type = StreetEditorCatalog.GetObjectType(o, document);
            int index = Array.IndexOf(StreetEditorCatalog.ObjectTypeIds, type);
            int current = Mathf.Max(0, index);
            int next = EditorGUILayout.Popup("对象类型", current, StreetEditorCatalog.ObjectTypeNames);
            if (next != current && next >= 0)
            {
                try
                {
                    string oldId = o.id;
                    string newType = StreetEditorCatalog.ObjectTypeIds[next];
                    StreetEditorCatalog.ApplyObjectType(o, newType, ExistingIds(o.id));
                    selectedId = o.id;
                    if (StreetEditorCatalog.ActorKindFor(newType) == StreetActorKind.None)
                        document.routes.RemoveAll(route => route != null && route.objectId == oldId);
                    else
                    {
                        foreach (var route in document.routes)
                            if (route != null && route.objectId == oldId) route.objectId = o.id;
                        StreetEditorCatalog.ConfigureMovement(document, o, StreetEditorCatalog.StartsMoving(newType));
                    }
                    type = newType;
                }
                catch (Exception error) { issues.Add("错误：" + error.Message); }
            }
            bool isActor = StreetEditorCatalog.ActorKindFor(type) != StreetActorKind.None;
            if (isActor)
            {
                bool moving = document.routes.Exists(route => route != null && route.objectId == o.id);
                int mode = EditorGUILayout.Popup("移动状态", moving ? 1 : 0,
                    new[] { "静止（无需路线）", "行走 / 行驶（编辑路线）" });
                if ((mode == 1) != moving) StreetEditorCatalog.ConfigureMovement(document, o, mode == 1);
                StreetRouteData actorRoute = document.routes.Find(route => route != null && route.objectId == o.id);
                if (actorRoute != null) RouteInspector(actorRoute);
            }
            ReadOnlyId("对象编号", o.id);
            ObjectPositionFields(o);
            FloatField("高度", ref o.height);
            FloatField("声学损失", ref o.acousticLoss);
            GUILayout.Label("普通碰撞仅有阻挡和反馈，不扣值或眩晕。", EditorStyles.wordWrappedMiniLabel);
            if (isActor) GUILayout.Label("实体碰撞：开启（运行时强制）", EditorStyles.wordWrappedMiniLabel);
            else BoolField("实体碰撞", ref o.solid);
            GUILayout.Label("仅碰撞发生时实际运动的车辆恢复检查点。", EditorStyles.wordWrappedMiniLabel);
            BoolField("阻挡探测", ref o.blocksProbe);
            BoolField("声音激活", ref o.soundActive);
            EnumField("记忆类型", ref o.kind, new[] { "永久环境", "临时障碍", "活动对象" });
            EnumField("感知语义", ref o.semantic, new[] { "未知", "普通", "人物", "危险", "交互", "引导" });
            CatalogPopup("材质", StreetEditorCatalog.ObjectMaterialIds, StreetEditorCatalog.ObjectMaterialNames, ref o.materialId);
            CatalogPopup("声音", StreetEditorCatalog.SoundIds, StreetEditorCatalog.SoundNames, ref o.soundId);
            DrawPolygonInspector(o.polygon);
            DrawSizeControls(o.polygon);
            if (GUILayout.Button("删除对象")) { document.objects.Remove(o); document.routes.RemoveAll(x => x != null && x.objectId == o.id); selectedKind = selectedId = ""; Commit("删除对象"); }
        }
        private void CheckpointInspector(StreetCheckpointData c)
        { GUILayout.Label("检查点 · " + c.id, EditorStyles.miniBoldLabel); FloatField("位置 X", ref c.position.x); FloatField("位置 Y", ref c.position.y); FloatField("朝向 X", ref c.facing.x); FloatField("朝向 Y", ref c.facing.y); FloatField("半径", ref c.radius); if (GUILayout.Button("删除检查点")) { document.checkpoints.Remove(c); selectedKind = selectedId = ""; Commit("删除检查点"); } }
        private void RouteInspector(StreetRouteData route)
        {
            GUILayout.Label("路线 · " + route.objectId, EditorStyles.miniBoldLabel);
            FloatField("速度（米/秒）", ref route.speed);
            DoubleField("起始偏移（米）", ref route.offset);
            route.loop = EditorGUILayout.Toggle("到达末端后循环", route.loop);
            int axis = route.signalAxis == null ? 0 : route.signalAxis == "ns" ? 1 : 2;
            int next = EditorGUILayout.Popup("信号控制", axis, new[] { "无", "南北", "东西" });
            route.signalAxis = next == 0 ? null : next == 1 ? "ns" : "ew";
            if (route.signalAxis != null)
            {
                float stop = route.stopLineDistance ?? 0;
                FloatField("停车线距离", ref stop);
                route.stopLineDistance = stop;
            }
            if (route.points != null)
            {
                GUILayout.Label("路线节点", EditorStyles.miniBoldLabel);
                for (int i = 0; i < route.points.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((i + 1).ToString(), GUILayout.Width(22));
                    float px = EditorGUILayout.FloatField(route.points[i].x);
                    float py = EditorGUILayout.FloatField(route.points[i].y);
                    if (px != route.points[i].x || py != route.points[i].y) MoveRoutePoint(route, i, px, py);
                    if (GUILayout.Button("×", GUILayout.Width(24)) && route.points.Count > 2)
                    { route.points.RemoveAt(i); i--; GUI.changed = true; }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("添加末尾节点"))
                {
                    var point = route.points.Count == 0 ? new StreetPointData(0, 0) : route.points[route.points.Count - 1];
                    route.points.Add(new StreetPointData(point.x + 2, point.y));
                    GUI.changed = true;
                }
            }
            if (GUILayout.Button("删除路线"))
            { document.routes.Remove(route); selectedRoute = ""; Commit("删除路线"); }
        }
        private void FloatField(string label, ref float value) { value = EditorGUILayout.FloatField(label, value); }
        private void DoubleField(string label, ref double value) { value = EditorGUILayout.DoubleField(label, value); }
        private void TextField(string label, ref string value) { value = EditorGUILayout.TextField(label, value ?? ""); }
        private void IntField(string label, ref int value) { value = EditorGUILayout.IntField(label, value); }
        private void BoolField(string label, ref bool value) { value = EditorGUILayout.Toggle(label, value); }
        private void ObjectPositionFields(StreetObjectData item)
        { float x = EditorGUILayout.FloatField("位置 X", item.position.x); float y = EditorGUILayout.FloatField("位置 Y", item.position.y); float dx = x - item.position.x, dy = y - item.position.y; if (dx != 0 || dy != 0) { item.position.x = x; item.position.y = y; if (item.polygon != null) foreach (var p in item.polygon) { p.x += dx; p.y += dy; } foreach (var route in document.routes) if (route != null && route.objectId == item.id && route.points != null) foreach (var point in route.points) { point.x += dx; point.y += dy; } } }
        private void NullableFloatField(string label, ref float? value)
        { bool enabled = EditorGUILayout.Toggle(label, value.HasValue); if (!enabled) { value = null; return; } float v = EditorGUILayout.FloatField("数值", value ?? 1); value = v; }
        private void EnumField<T>(string label, ref T value, string[] displayNames) where T : struct
        { Array values = Enum.GetValues(typeof(T)); int old = Convert.ToInt32(value); int next = EditorGUILayout.Popup(label, old, displayNames); if (next >= 0 && next < values.Length) value = (T)Enum.GetValues(typeof(T)).GetValue(next); }
        private void ReadOnlyId(string label, string value)
        { using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField(label, value ?? ""); }
        private void CatalogPopup(string label, string[] ids, string[] names, ref string value)
        {
            var labels = new string[names.Length + 1]; labels[0] = "无";
            Array.Copy(names, 0, labels, 1, names.Length);
            int old = string.IsNullOrEmpty(value) ? 0 : Array.IndexOf(ids, value) + 1;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, old, labels);
            if (EditorGUI.EndChangeCheck()) value = next <= 0 ? null : ids[next - 1];
        }
        private void DrawPolygonInspector(List<StreetPointData> polygon)
        { if (polygon == null || polygon.Count == 0) { GUILayout.Label("无轮廓几何（声源对象）。", EditorStyles.miniLabel); return; } GUILayout.Label("多边形顶点（世界坐标）", EditorStyles.miniBoldLabel); for (int i = 0; i < polygon.Count; i++) { GUILayout.BeginHorizontal(); GUILayout.Label((i + 1).ToString(), GUILayout.Width(20)); polygon[i].x = EditorGUILayout.FloatField(polygon[i].x); polygon[i].y = EditorGUILayout.FloatField(polygon[i].y); GUILayout.EndHorizontal(); } }
        private void DrawSizeControls(List<StreetPointData> polygon)
        { if (polygon == null || polygon.Count < 3) return; GUILayout.BeginHorizontal(); if (GUILayout.Button("宽度 −")) { ScalePolygon(polygon, .9f, 1); Commit("缩小宽度"); } if (GUILayout.Button("宽度 +")) { ScalePolygon(polygon, 1.1f, 1); Commit("增大宽度"); } if (GUILayout.Button("长度 −")) { ScalePolygon(polygon, 1, .9f); Commit("缩短长度"); } if (GUILayout.Button("长度 +")) { ScalePolygon(polygon, 1, 1.1f); Commit("增长长度"); } GUILayout.EndHorizontal(); }
        private static void ScalePolygon(List<StreetPointData> polygon, float xScale, float yScale) { float cx = 0, cy = 0; foreach (var p in polygon) { cx += p.x; cy += p.y; } cx /= polygon.Count; cy /= polygon.Count; foreach (var p in polygon) { p.x = cx + (p.x - cx) * xScale; p.y = cy + (p.y - cy) * yScale; } }

        private void CancelPlacement()
        {
            if (dragging && !string.IsNullOrEmpty(dragOriginal)) document = StreetLevelCodec.Decode(dragOriginal);
            placingKind = placingId = ""; dragging = panning = false;
            UpdateDirty(); Repaint();
        }
        private void HandleShortcuts()
        {
            if (!GUI.enabled) return;
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Escape) { CancelPlacement(); e.Use(); }
            else if ((e.control || e.command) && e.keyCode == KeyCode.S) { Save(e.shift); e.Use(); }
            else if ((e.control || e.command) && e.keyCode == KeyCode.Z) { if (e.shift) Undo.PerformRedo(); else Undo.PerformUndo(); e.Use(); }
        }
        private void ExportJson()
        {
            if (!Check()) return;
            string path = EditorUtility.SaveFilePanel("导出可玩街区JSON", EditorProjectPaths.DataDirectory, document.title, "json");
            if (string.IsNullOrEmpty(path)) return;
            try { StreetLevelFileStore.Save(path, StreetLevelCodec.Clone(document), true); issues.Add("JSON 已导出：" + path); }
            catch (Exception error) { EditorUtility.DisplayDialog("导出失败", error.Message, "知道了"); }
        }
        private void ArmPlacement(string kind, string id) { placingKind = kind; placingId = id; Repaint(); }
        private void AddSurfaceAt(string material, Point2 p)
        { var polygon = StreetEditorCatalog.Rectangle(p.X, p.Y, 4, 4); var item = StreetEditorCatalog.CreateSurface(material, polygon, ExistingIds()); document.surfaces.Add(item); Select("surface", item.id); Commit("添加地面"); }
        private void AddObjectAt(string type, Point2 p)
        { var item = StreetEditorCatalog.CreateObject(type, StreetEditorCatalog.Point(p), null, ExistingIds()); document.objects.Add(item); StreetEditorCatalog.ConfigureMovement(document,item,StreetEditorCatalog.StartsMoving(type)); Select("object", item.id); Commit("添加对象"); }
        private void AddCheckpointAt(Point2 p)
        { var item = StreetEditorCatalog.CreateCheckpoint(null, StreetEditorCatalog.Point(p)); document.checkpoints.Add(item); Select("checkpoint", item.id); Commit("添加检查点"); }
        private void AddRoute()
        {
            var item = document.objects.Find(value => value != null && value.id == selectedId);
            if (item == null || StreetEditorCatalog.ActorKindFor(StreetEditorCatalog.GetObjectType(item, document)) == StreetActorKind.None)
            {
                issues.Add("提示：先选中一名行人或一辆汽车、自行车、电动车，再设置移动路线。");
                return;
            }
            if (document.routes.Exists(route => route != null && route.objectId == item.id))
            {
                SelectRoute(item.id);
                issues.Add("提示：该对象已有路线，可在属性检查器中编辑速度、循环和路线节点。");
                return;
            }
            StreetEditorCatalog.ConfigureMovement(document, item, true);
            SelectRoute(item.id);
            Commit("开始移动并添加路线");
        }
        private IEnumerable<string> ExistingIds(string except = null) { var ids = new List<string>(); ids.Add(document.id); foreach (var x in document.surfaces) if (x != null && x.id != except) ids.Add(x.id); foreach (var x in document.objects) if (x != null && x.id != except) ids.Add(x.id); foreach (var x in document.checkpoints) if (x != null && x.id != except) ids.Add(x.id); return ids; }

        private void Select(string kind, string id) { selectedKind = kind; selectedId = id; selectedRoute = ""; selectedRoutePoint = -1; }
        private void SelectRoute(string id) { selectedKind = "route"; selectedId = ""; selectedRoute = id; }
        private void NewDocument(StreetLevelDocument value) { if (!SwitchAllowed()) return; SetDocument(value, ""); }
        private bool SwitchAllowed()
        { if (!Dirty) return true; int choice = EditorUtility.DisplayDialogComplex("未保存的新版关卡", "当前关卡有未保存修改。", "保存草稿", "取消", "放弃修改"); return choice == 0 ? Save(false) : choice == 2; }
        private static StreetLevelDocument Blank()
        { return new StreetLevelDocument { id = Guid.NewGuid().ToString("N"), title = "空白街区", goalText = "到达终点。", spawnX = 0, spawnY = 0, facingX = 0, facingY = 1, goalX = 0, goalY = 8, goalRadius = 1 }; }
        private void Open()
        { if (!SwitchAllowed()) return; string path = EditorUtility.OpenFilePanel("打开新版街区关卡", EditorProjectPaths.DataDirectory, "json"); if (string.IsNullOrEmpty(path)) return; try { SetDocument(StreetLevelFileStore.Load(path), path); Check(); } catch (Exception error) { EditorUtility.DisplayDialog("无法打开新版关卡", error.Message + "\n原文件保留。", "知道了"); } }
        private bool Save(bool saveAs)
        { try { string current = string.IsNullOrEmpty(filePath) ? "" : EditorProjectPaths.ResolveFilePath(filePath, fileAssetGuid); string path = saveAs || string.IsNullOrEmpty(current) ? EditorUtility.SaveFilePanel("保存新版街区关卡", EditorProjectPaths.DataDirectory, document.title, "json") : current; if (string.IsNullOrEmpty(path)) return false; StreetLevelFileStore.Save(path, document, false); filePath = EditorProjectPaths.StoreFilePath(path); fileAssetGuid = EditorProjectPaths.FileAssetGuid(path); savedJson = buffer.json; SessionState.EraseString(DraftKey); UpdateDirty(); issues.Clear(); issues.Add("草稿已保存；导出和试玩仍会执行严格检查。"); return true; } catch (Exception error) { EditorUtility.DisplayDialog("保存失败", error.Message + "\n原文件保留。", "知道了"); return false; } }
        private bool Check()
        { issues.Clear(); foreach (string issue in StreetLevelCodec.Validate(document)) issues.Add("错误：" + issue); if (issues.Count == 0) issues.Add("检查通过：可以尝试试玩；真人听感和可达性仍需人工验收。"); Repaint(); return issues.Count == 1 && issues[0].StartsWith("检查通过"); }
        private void Preview()
        { if (!Check()) return; try { StreetPreviewHost.Start(StreetLevelCodec.Clone(document)); } catch (Exception error) { issues.Add("错误：试玩准备失败：" + error.Message); } }
        private void ExportScene()
        { if (!Check()) return; string path = EditorUtility.SaveFilePanelInProject("导出可玩街区场景", document.title, "unity", "场景和 JSON 将写入 Assets 内的新路径，不会覆盖已有文件。"); if (string.IsNullOrEmpty(path)) return; try { StreetLevelFileStore.ExportScene(path, StreetLevelCodec.Clone(document)); issues.Add("可玩场景已导出：" + path); } catch (Exception error) { EditorUtility.DisplayDialog("导出失败", error.Message, "知道了"); } }
        private void Locate(string id) { if (string.IsNullOrEmpty(id)) return; foreach (var s in document.surfaces) if (s != null && s.id == id) { Select("surface", id); return; } foreach (var o in document.objects) if (o != null && o.id == id) { Select("object", id); return; } foreach (var c in document.checkpoints) if (c != null && c.id == id) { Select("checkpoint", id); return; } SelectRoute(id); }
        private void FrameAll()
        { float minX = document.spawnX, maxX = document.spawnX, minY = document.spawnY, maxY = document.spawnY; Action<float, float> add = (x, y) => { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }; foreach (var s in document.surfaces) if (s != null && s.polygon != null) foreach (var p in s.polygon) add(p.x, p.y); foreach (var o in document.objects) if (o != null && o.polygon != null) foreach (var p in o.polygon) add(p.x, p.y); foreach (var r in document.routes) if (r != null && r.points != null) foreach (var p in r.points) add(p.x, p.y); add(document.goalX, document.goalY); center = new Vector2((minX + maxX) * .5f, (minY + maxY) * .5f); scale = Mathf.Clamp(Mathf.Min(Mathf.Max(300, position.width - 600) / (maxX - minX + 8), Mathf.Max(260, position.height - 280) / (maxY - minY + 8)), 2, 50); Repaint(); }
    }
}
