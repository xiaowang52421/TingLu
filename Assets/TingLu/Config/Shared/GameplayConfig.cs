using System;
namespace TingLu.Config
{
    [Serializable]
    public sealed class GameplayConfig
    {
        public float walkSpeed = 1.2f, listenSpeed = 0.6f;
        public float walkAngle = 75, walkRadius = 4, listenAngle = 110, listenRadius = 5.5f;
        public float lowFocusThreshold = 30, lowFocusAngle = 60, lowFocusRadius = 3;
        public float focusWalkDrain = 2, focusListenGain = 8, caneFocusCost = 3;
        public float caneRadius = 7, caneCooldown = 3, carbonCaneCooldown = 2, echoSeconds = 0.8f;
        public float confusionWindow = 8, confusionSeconds = 4, confusionMultiplier = 0.5f;
        public int confusionTapCount = 3;
        public float damageStatic = 10, damageBicycle = 15, damageFall = 25, damageElectric = 40;
        public float fallStunSeconds = 1.5f, thunderStunSeconds = 0.5f, painSeconds = 1;
        public float contactDebounceSeconds = 0.5f, contactExitSeconds = 0.1f;
        public float checkpointFocusGain = 50, failureSeconds = 2, tactileRange = 6;
        public float perceptionHz = 20, warningSeconds = 0.8f, warningTimeout = 3;
        public float spawnCourage = 100, spawnFocus = 100, stickDeadZone = 0.15f;
        public void Validate()
        {
            foreach (var field in typeof(GameplayConfig).GetFields())
            {
                if (field.FieldType != typeof(float)) continue;
                float value = (float)field.GetValue(this);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentException("Invalid " + field.Name);
            }
            if (walkSpeed <= 0 || listenSpeed <= 0 || caneCooldown <= 0 || perceptionHz <= 0 || confusionTapCount < 2 || stickDeadZone >= 1 || confusionMultiplier > 1 || warningSeconds < 0.6f || warningSeconds > 1 || spawnCourage > 100 || spawnFocus > 100 || lowFocusThreshold > 100 || walkAngle > 360 || listenAngle > 360 || lowFocusAngle > 360) throw new ArgumentException("Invalid gameplay configuration");
        }
    }
}
