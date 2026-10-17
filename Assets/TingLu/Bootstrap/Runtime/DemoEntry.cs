using System;
using TingLu.Contracts;
using TingLu.Config;
using TingLu.Audio;
using TingLu.Persistence;
using TingLu.Presentation;
using TingLu.UI;
using TingLu.Levels;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TingLu.Bootstrap
{
    public sealed class DemoEntry : MonoBehaviour
    {
        [SerializeField] private Material voiceMaterial;
        [SerializeField] private bool showTouchControls;
        [SerializeField] private TextAsset levelAsset;
        // Set only by the editor preview host before Start; persistent production saves are never used.
        [SerializeField, HideInInspector] private string previewJson;
        [SerializeField, HideInInspector] private string previewSaveDirectory;
        public void ConfigurePreview(string json,string saveDirectory)
        {if(string.IsNullOrEmpty(json)||string.IsNullOrEmpty(saveDirectory))throw new ArgumentException("试玩配置或存档目录缺失。");previewJson=json;previewSaveDirectory=saveDirectory;}
        private DemoSession session;
        private UnityAudioPlayback playback;
        private VoiceRenderer rendererView;
        private Camera playerCamera;
        private string subtitle = "", fatal = "";
        private bool touchTap, touchPause, touchListen;
        private Vector2 touchMove;
        private MenuAction touchMenu;
        private double nextPadMenu;
        private MenuScreen lastInputScreen;
        private MenuAction heldPadDirection;
        private int rendererGeneration=-1;

        private void Start()
        {
            try
            {
                var cameraObject = new GameObject("Player view"); cameraObject.transform.SetParent(transform, false);
                playerCamera = cameraObject.AddComponent<Camera>(); playerCamera.orthographic = true; playerCamera.orthographicSize = 7;
                playerCamera.clearFlags = CameraClearFlags.SolidColor; playerCamera.backgroundColor = Color.black; playerCamera.cullingMask = 1 << 8;
                var listenerObject = new GameObject("Player audio listener"); listenerObject.transform.SetParent(transform, false);
                listenerObject.AddComponent<AudioListener>();
                playback = gameObject.AddComponent<UnityAudioPlayback>(); playback.ConfigureListener(listenerObject.transform);
                // The backend needs the same clock before a first-launch menu utterance can be queued.
                var clockBridge = new StartupClock(); playback.Initialize(clockBridge, 0);
                string json=!string.IsNullOrEmpty(previewJson)?previewJson:levelAsset!=null?levelAsset.text:null;
                if(string.IsNullOrEmpty(json))throw new InvalidOperationException("场景未配置关卡，请在入口绑定关卡文件。");
                FirstStepLevel level=LevelCompiler.Compile(LevelCodec.Decode(json));
                if(!string.IsNullOrEmpty(previewJson)&&string.IsNullOrEmpty(previewSaveDirectory))throw new InvalidOperationException("试玩缺少隔离存档目录。");
                string saveDirectory=!string.IsNullOrEmpty(previewJson)?previewSaveDirectory:Application.persistentDataPath;
                session = new DemoSession(playback, playback, new LocalSaveService(saveDirectory),null,level);
                clockBridge.Target = session.Clock;
                var voiceRoot = new GameObject("Known sound shapes"); rendererView = voiceRoot.AddComponent<VoiceRenderer>();
                rendererView.Configure(playerCamera, 8, voiceMaterial); rendererView.Initialize(new PresentationFrameComposer());
                var panel = gameObject.AddComponent<AccessibleMenuPanel>(); panel.Initialize(session.Menu);
                session.Menu.Subtitle += text => subtitle = text;
                session.Feedback += rendererView.OnEvent;
                session.Error += text => { subtitle = text; Debug.LogError(text); };
                playback.Receipt += receipt =>
                {
                    if(receipt.Status==AudioStatus.Failed && receipt.Error!=null)
                    { Debug.LogWarning("Audio " + receipt.EventId + ": " + receipt.Error); subtitle="语音或声音暂时不可用，请检查音频设备。"; }
                };
                if(!string.IsNullOrEmpty(previewJson))session.Start(false);
            }
            catch(Exception error){fatal="启动失败："+error.Message;Debug.LogException(error);enabled=false;}
        }
        private void Update()
        {
            if(session==null)return;
            if(!session.Running && rendererView!=null)rendererView.Clear();
            session.Tick(ReadInput(), Math.Min(Time.unscaledDeltaTime, 0.1f));
            if(rendererGeneration!=session.Generation){rendererView.ResetFeedback();rendererGeneration=session.Generation;}
            playback.SetMasterVolume(session.Settings.MasterVolume);
            foreach(var source in session.Sources)playback.UpdateSource(source.SourceId,source.ObservedSourcePosition,source.Discernibility,source.Occlusion);
            if(session.Player==null)return;
            var p=session.Player.Current.Position;playerCamera.transform.position=new Vector3(p.X,p.Y,-10);
            if(session.Blackout)rendererView.Clear();
            else rendererView.Render(session.Perception,session.Memory,session.Player,session.Settings,session.Clock.Now);
        }
        private InputFrame ReadInput()
        {
            bool screenChanged = lastInputScreen != session.Menu.Screen;
            if(screenChanged) { nextPadMenu=0; heldPadDirection=MenuAction.None; lastInputScreen=session.Menu.Screen; }
            Vector2 move=Vector2.zero;bool listen=touchListen,tap=touchTap,pause=touchPause;MenuAction menu=touchMenu;
            var keys=Keyboard.current;
            if(keys!=null)
            {
                float x=(keys.dKey.isPressed||keys.rightArrowKey.isPressed?1:0)-(keys.aKey.isPressed||keys.leftArrowKey.isPressed?1:0);
                float y=(keys.wKey.isPressed||keys.upArrowKey.isPressed?1:0)-(keys.sKey.isPressed||keys.downArrowKey.isPressed?1:0);
                move=new Vector2(x,y);listen|=keys.leftShiftKey.isPressed||keys.rightShiftKey.isPressed;tap|=keys.spaceKey.wasPressedThisFrame;pause|=keys.escapeKey.wasPressedThisFrame;
                if(keys.gKey.wasPressedThisFrame)menu=MenuAction.QueryGoal;
                else if(keys.enterKey.wasPressedThisFrame||keys.spaceKey.wasPressedThisFrame)menu=MenuAction.Confirm;
                else if(keys.upArrowKey.wasPressedThisFrame||keys.wKey.wasPressedThisFrame)menu=MenuAction.Previous;
                else if(keys.downArrowKey.wasPressedThisFrame||keys.sKey.wasPressedThisFrame)menu=MenuAction.Next;
                else if(keys.leftArrowKey.wasPressedThisFrame||keys.aKey.wasPressedThisFrame)menu=MenuAction.Decrease;
                else if(keys.rightArrowKey.wasPressedThisFrame||keys.dKey.wasPressedThisFrame)menu=MenuAction.Increase;
                else if(keys.backspaceKey.wasPressedThisFrame)menu=MenuAction.Back;
            }
            var pad=Gamepad.current;
            if(pad!=null)
            {
                Vector2 stick=pad.leftStick.ReadValue();if(stick.sqrMagnitude>0.03f)move=stick;
                Vector2 dpad=pad.dpad.ReadValue();if(dpad.sqrMagnitude>0.03f)move=dpad;
                listen|=pad.leftShoulder.isPressed;tap|=pad.buttonSouth.wasPressedThisFrame;pause|=pad.startButton.wasPressedThisFrame;
                if(pad.buttonSouth.wasPressedThisFrame)menu=MenuAction.Confirm;
                else if(pad.buttonEast.wasPressedThisFrame)menu=MenuAction.Back;
                else if(pad.buttonNorth.wasPressedThisFrame)menu=MenuAction.QueryGoal;
                else if(session.Menu.Screen!=MenuScreen.Playing)
                {
                    Vector2 navigation=dpad.sqrMagnitude>0.03f?dpad:stick;
                    MenuAction direction=navigation.sqrMagnitude>0.3f
                        ?(Math.Abs(navigation.y)>=Math.Abs(navigation.x)?(navigation.y>0?MenuAction.Previous:MenuAction.Next):(navigation.x>0?MenuAction.Increase:MenuAction.Decrease))
                        :MenuAction.None;
                    if(screenChanged && direction!=MenuAction.None) { heldPadDirection=direction; nextPadMenu=double.PositiveInfinity; }
                    else if(direction!=MenuAction.None && (direction!=heldPadDirection || Time.unscaledTime>=nextPadMenu))
                    { menu=direction;nextPadMenu=Time.unscaledTime+(direction!=heldPadDirection?0.4:0.25); }
                    if(direction==MenuAction.None)nextPadMenu=0;
                    heldPadDirection=direction;
                }
            }
            if(touchMove.sqrMagnitude>0.01f)move=touchMove;
            move=Vector2.ClampMagnitude(move,1);
            touchTap=touchPause=false;touchMenu=MenuAction.None;
            return new InputFrame(new Point2(move.x,move.y),listen,tap,pause,menu);
        }
        private void OnGUI()
        {
            if(!string.IsNullOrEmpty(fatal)){GUI.Label(new Rect(20,20,Screen.width-40,100),fatal);return;}
            if(session==null)return;
            if(session.Menu.Screen==MenuScreen.Playing && session.Settings.Display==DisplayMode.Standard && session.Player!=null)
            {
                GUI.Label(new Rect(20,20,400,28),"勇气 "+(int)session.Player.Current.Courage+"　专注 "+(int)session.Player.Current.Focus);
                if(session.Settings.Subtitles)GUI.Label(new Rect(20,52,Screen.width-40,70),subtitle);
            }
            // Large touch controls also serve as a PC adaptation test entry.
            if(!showTouchControls && Touchscreen.current==null)return;
            if(session.Menu.Screen==MenuScreen.Playing)
            {
                float y=Screen.height-170;
                bool up=GUI.RepeatButton(new Rect(80,y,70,55),"前");bool down=GUI.RepeatButton(new Rect(80,y+110,70,55),"后");
                bool left=GUI.RepeatButton(new Rect(5,y+55,70,55),"左");bool right=GUI.RepeatButton(new Rect(155,y+55,70,55),"右");
                touchMove=new Vector2((right?1:0)-(left?1:0),(up?1:0)-(down?1:0));
                touchListen=GUI.RepeatButton(new Rect(Screen.width-170,y,160,70),"按住聆听");
                if(GUI.Button(new Rect(Screen.width-170,y+80,160,70),"敲杖"))touchTap=true;
                if(GUI.Button(new Rect(Screen.width-90,10,80,45),"暂停"))touchPause=true;
                if(GUI.Button(new Rect(Screen.width-180,10,85,45),"重听目标"))touchMenu=MenuAction.QueryGoal;
            }
            else
            {
                touchMove=Vector2.zero;touchListen=false;
                if(GUI.Button(new Rect(Screen.width-110,Screen.height-160,100,45),"上一项"))touchMenu=MenuAction.Previous;
                if(GUI.Button(new Rect(Screen.width-110,Screen.height-110,100,45),"下一项"))touchMenu=MenuAction.Next;
                if(GUI.Button(new Rect(Screen.width-110,Screen.height-60,100,45),"确认"))touchMenu=MenuAction.Confirm;
                if(GUI.Button(new Rect(Screen.width-220,Screen.height-160,100,45),"返回"))touchMenu=MenuAction.Back;
                if(GUI.Button(new Rect(Screen.width-220,Screen.height-110,100,45),"调低"))touchMenu=MenuAction.Decrease;
                if(GUI.Button(new Rect(Screen.width-220,Screen.height-60,100,45),"调高"))touchMenu=MenuAction.Increase;
            }
        }
        private void OnApplicationPause(bool paused){if(paused && session!=null){session.Pause();session.Menu.OpenPause();}}
        private void OnApplicationQuit(){if(session!=null)session.ReturnToMenu();}
        private void OnDestroy(){if(session!=null)session.Dispose();if(rendererView!=null)Destroy(rendererView.gameObject);}
        private sealed class StartupClock:IGameClock
        {public IGameClock Target;public double Now{get{return Target==null?0:Target.Now;}}public bool Paused{get{return Target!=null&&Target.Paused;}}}
    }
}
