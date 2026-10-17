using System;
using System.IO;
using TingLu.Contracts;
using TingLu.Config;
using TingLu.Input;
using TingLu.View;
using TingLu.Audio;
using TingLu.Persistence;
using TingLu.Presentation;
using TingLu.Levels;
using TingLu.UI;
using UnityEngine;

namespace TingLu.Bootstrap
{
    public sealed class StreetEntry : MonoBehaviour
    {
        [SerializeField] private StreetConfig configuration=new StreetConfig();
        public StreetSession Session{get;private set;}
        public Camera PlayerCamera{get;private set;}
        public StreetInputSampler InputSampler{get;private set;}
        private StreetCameraRig cameraRig;
        private StreetRenderer rendererView;
        private StreetHudPresenter hud;
        private UnityAudioPlayback playback;
        private string subtitle="",fatal="";
        private bool snapCamera=true,quitting;
        private MenuScreen lastScreen;
        private void Start()
        {
            try
            {
                configuration.Validate();
                var cameraObject=new GameObject("Street fixed camera");cameraObject.transform.SetParent(transform,false);
                PlayerCamera=cameraObject.AddComponent<Camera>();PlayerCamera.clearFlags=CameraClearFlags.SolidColor;PlayerCamera.backgroundColor=Color.black;PlayerCamera.cullingMask=1<<8;
                cameraRig=cameraObject.AddComponent<StreetCameraRig>();cameraRig.Configure(PlayerCamera,configuration);
                var listener=new GameObject("Street player listener");listener.transform.SetParent(transform,false);listener.AddComponent<AudioListener>();
                playback=gameObject.AddComponent<UnityAudioPlayback>();playback.ConfigureListener(listener.transform);
                var bridge=new ClockBridge();playback.Initialize(bridge,0);
                InputSampler=gameObject.AddComponent<StreetInputSampler>();InputSampler.Configure(configuration);InputSampler.SetGameplayActive(false);
                // The product path and namespace both isolate this version from the archived demo.
                string root=Application.persistentDataPath;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string diagnosticsRoot=Environment.GetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT");
                if(!string.IsNullOrWhiteSpace(diagnosticsRoot))root=Path.GetFullPath(diagnosticsRoot);
#endif
                var settingsStore=new LocalSaveService(Path.Combine(root,configuration.SaveNamespace,"Options"));
                var loadedSettings=settingsStore.LoadSettings();
                Session=new StreetSession(StreetLevelFactory.Create(),playback,playback,new StreetSaveService(root,configuration.SaveNamespace),configuration,loadedSettings.Status==SaveStatus.Success?loadedSettings.Value:null);
                bridge.Target=Session.Clock;
                Session.SettingsChanged+=settings=>{var result=settingsStore.SaveSettings(settings);if(result!=SaveStatus.Success)subtitle="设置没有保存："+result;};
                Session.Menu.Subtitle+=text=>subtitle=text;
                Session.QuitRequested+=Quit;
                Session.Error+=text=>{subtitle=text;Debug.LogError(text);};
                Session.Recovered+=()=>{snapCamera=true;InputSampler.Clear();};
                playback.Receipt+=receipt=>{if(receipt.Status==AudioStatus.Failed)Debug.LogWarning("Street audio failed: "+receipt.Error);};
                rendererView=gameObject.AddComponent<StreetRenderer>();rendererView.Configure(PlayerCamera,8,configuration);
                hud=gameObject.AddComponent<StreetHudPresenter>();hud.Configure(Session.Menu);
                lastScreen=Session.Menu.Screen;
                Debug.Log("TingLu Street ready; contract="+StreetContract.Version+"; level="+Session.Level.Id+"; saveNamespace="+configuration.SaveNamespace);
            }
            catch(Exception e){fatal="街区启动失败："+e.Message;Debug.LogException(e);if(InputSampler!=null)InputSampler.SetGameplayActive(false);}
        }
        private void Update()
        {
            if(Session==null||!string.IsNullOrEmpty(fatal))return;
            bool active=Session.Running&&Session.Menu.Screen==MenuScreen.Playing&&!Session.Clock.Paused&&!Session.Finished;
            if(active&&!InputSampler.HasFocus){Session.Pause();Session.Menu.OpenPause();active=false;}
            InputSampler.SetGameplayActive(active);
            StreetInputFrame input=InputSampler.Read();
            Session.Tick(input,Math.Min(Time.unscaledDeltaTime,0.1f));
            if(Session.Menu.Screen!=lastScreen)
            {lastScreen=Session.Menu.Screen;InputSampler.Clear();snapCamera=true;InputSampler.SetGameplayActive(Session.Running&&lastScreen==MenuScreen.Playing&&!Session.Clock.Paused&&!Session.Finished);}
            playback.SetMasterVolume(Session.Settings.MasterVolume);
            if(Session.Player!=null)
            {
                if(snapCamera||!Session.Clock.Paused)cameraRig.Follow(Session.Player.Current.Position,snapCamera);snapCamera=false;
                if(Session.Running)rendererView.Render(Session.Memory.Current,Session.Perception,Session.Player.Current,Session.Settings,Session.Clock.Now,Session.Signals);
                else rendererView.Clear();
            }
            hud.Present(Session.Player==null?null:Session.Player.Current,Session.Settings,subtitle,Session.Menu.Screen==MenuScreen.Playing);
        }
        private void OnApplicationFocus(bool focused)
        {
            if(!focused&&Session!=null&&Session.Running&&Session.Menu.Screen==MenuScreen.Playing)
            {Session.Pause();Session.Menu.OpenPause();if(InputSampler!=null){InputSampler.SetGameplayActive(false);InputSampler.Clear();}}
        }
        private void OnApplicationPause(bool paused){if(paused)OnApplicationFocus(false);}
        private void OnApplicationQuit(){quitting=true;if(InputSampler!=null)InputSampler.SetGameplayActive(false);if(Session!=null&&string.IsNullOrEmpty(fatal))Session.Save();}
        private void OnDestroy(){if(InputSampler!=null)InputSampler.SetGameplayActive(false);if(Session!=null)Session.Dispose();}
        private void OnGUI()
        {
            if(string.IsNullOrEmpty(fatal))return;
            GUILayout.BeginArea(new Rect(20,20,Mathf.Max(1,Screen.width-40),Mathf.Max(1,Screen.height-40)),GUI.skin.box);
            GUILayout.Label("暂时无法进入街区，请退出后重试。");
            GUILayout.Label(fatal);
            if(GUILayout.Button("退出游戏",GUILayout.MinHeight(42)))ExitFailedStartup();
            GUILayout.EndArea();
            if(Event.current.type==EventType.KeyDown&&(Event.current.keyCode==KeyCode.Escape||Event.current.keyCode==KeyCode.Return))
            {Event.current.Use();ExitFailedStartup();}
        }
        // A partially initialized session is not safe to save while leaving the error screen.
        private void ExitFailedStartup(){quitting=true;Application.Quit();}
        public void Quit(){if(quitting)return;quitting=true;if(Session!=null)Session.Save();if(InputSampler!=null)InputSampler.SetGameplayActive(false);Application.Quit();}
        private sealed class ClockBridge:IGameClock
        {public IGameClock Target;public double Now{get{return Target==null?0:Target.Now;}}public bool Paused{get{return Target==null||Target.Paused;}}}
    }
}


