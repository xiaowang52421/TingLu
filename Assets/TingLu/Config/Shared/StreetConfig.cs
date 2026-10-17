using System;
namespace TingLu.Config
{
    [Serializable]
    public sealed class StreetConfig
    {
        public GameplayConfig Gameplay = new GameplayConfig();
        // Running baselines; tune after the street playtest.
        public float ProbeAngle=100, MouseSensitivity=0.15f, CameraTilt=52, CameraSize=9, CameraDistance=20;
        public float CameraTargetOffsetX=0, CameraTargetOffsetY=0, CameraTargetOffsetZ=0, CameraFollowSharpness=10;
        public bool InvertMouse;
        public float BodyRadius=0.18f, AcousticRadius=26, SoundSeconds=0.4f, ContactRadius=0.22f;
        public double SignalGreenSeconds=14, SignalClearanceSeconds=3;
        public string SaveNamespace="TingLuStreetUpgrade";
        public void Validate()
        {
            Gameplay.Validate();
            // The street probe DTO accepts a sector strictly smaller than a full circle.
            if(Gameplay.lowFocusAngle>=360)throw new ArgumentException("Invalid lowFocusAngle");
            foreach(var field in typeof(StreetConfig).GetFields())if(field.FieldType==typeof(float))
            {float v=(float)field.GetValue(this);bool offset=field.Name.StartsWith("CameraTargetOffset");if(float.IsNaN(v)||float.IsInfinity(v)||(!offset&&v<=0))throw new ArgumentException("Invalid "+field.Name);}
            if(ProbeAngle>=360||CameraTilt>=90||double.IsNaN(SignalGreenSeconds)||double.IsInfinity(SignalGreenSeconds)||double.IsNaN(SignalClearanceSeconds)||double.IsInfinity(SignalClearanceSeconds)||SignalGreenSeconds<=0||SignalClearanceSeconds<0||string.IsNullOrWhiteSpace(SaveNamespace))throw new ArgumentException("Invalid street configuration");
        }
    }
}
