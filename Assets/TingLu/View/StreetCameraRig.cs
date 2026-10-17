using System;
using System.Runtime.CompilerServices;
using TingLu.Config;
using TingLu.Contracts;
using UnityEngine;

[assembly: InternalsVisibleTo("TingLu.Tests.Upgrade.Role07")]

namespace TingLu.View
{
    /// <summary>
    /// Fixed north-facing orthographic camera for the street plane. The caller supplies
    /// only the player position; player facing and world geometry are intentionally unused.
    /// </summary>
    public sealed class StreetCameraRig : MonoBehaviour, IStreetCamera
    {
        private Camera streetCamera;
        private StreetConfig config;
        private Vector3 target;
        private bool configured;
        private bool hasTarget;

        public void Configure(Camera camera, StreetConfig streetConfig)
        {
            if (camera == null) throw new ArgumentNullException("camera");
            if (streetConfig == null) throw new ArgumentNullException("streetConfig");
            streetConfig.Validate();

            streetCamera = camera;
            config = streetConfig;
            configured = true;
            hasTarget = false;

            streetCamera.orthographic = true;
            streetCamera.orthographicSize = config.CameraSize;
            ApplyFixedRotation();
        }

        public void Follow(Point2 position, bool snap)
        {
            Follow(position, snap, Time.unscaledDeltaTime);
        }

        internal void Follow(Point2 position, bool snap, float deltaTime)
        {
            EnsureConfigured();

            StreetVector3 scenePosition = StreetCoordinates.ToScene(position);
            target = new Vector3(
                scenePosition.X + config.CameraTargetOffsetX,
                scenePosition.Y + config.CameraTargetOffsetY,
                scenePosition.Z + config.CameraTargetOffsetZ);
            hasTarget = true;

            streetCamera.orthographic = true;
            streetCamera.orthographicSize = config.CameraSize;
            ApplyFixedRotation();

            if (snap)
            {
                streetCamera.transform.position = CameraPosition(target, config);
            }
            else
            {
                AdvanceFollow(deltaTime);
            }
        }

        /// <summary>
        /// Advances the smoothing by an explicit time step. Production Follow uses
        /// unscaled time; an explicit step keeps EditMode tests deterministic and a
        /// zero-time step leaves the camera position unchanged.
        /// </summary>
        internal void AdvanceFollow(float deltaTime)
        {
            EnsureConfigured();
            if (!hasTarget) return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException("deltaTime");

            Vector3 desiredPosition = CameraPosition(target, config);
            streetCamera.transform.position = SmoothPosition(
                streetCamera.transform.position,
                desiredPosition,
                config.CameraFollowSharpness,
                deltaTime);
            ApplyFixedRotation();
        }

        internal static Vector3 CameraPosition(Vector3 lookTarget, StreetConfig streetConfig)
        {
            float tilt = streetConfig.CameraTilt * Mathf.Deg2Rad;
            float distance = streetConfig.CameraDistance;
            return lookTarget + new Vector3(0f, Mathf.Sin(tilt) * distance, -Mathf.Cos(tilt) * distance);
        }

        internal static Vector3 SmoothPosition(Vector3 current, Vector3 desired, float sharpness, float deltaTime)
        {
            if (float.IsNaN(sharpness) || float.IsInfinity(sharpness) || sharpness <= 0f)
                throw new ArgumentOutOfRangeException("sharpness");
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException("deltaTime");

            float blend = 1f - Mathf.Exp(-sharpness * deltaTime);
            return Vector3.LerpUnclamped(current, desired, blend);
        }

        private void ApplyFixedRotation()
        {
            // CameraYaw is frozen by the shared coordinate contract at zero.
            streetCamera.transform.rotation = Quaternion.Euler(config.CameraTilt, StreetCoordinates.CameraYaw, 0f);
        }

        private void EnsureConfigured()
        {
            if (!configured || streetCamera == null || config == null)
                throw new InvalidOperationException("Configure must be called before following a street position.");
        }
    }
}
