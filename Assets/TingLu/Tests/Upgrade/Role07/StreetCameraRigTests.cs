using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.View;
using UnityEngine;

namespace TingLu.Tests.Upgrade.Role07
{
    public sealed class StreetCameraRigTests
    {
        private GameObject cameraObject;
        private Camera streetCamera;
        private StreetCameraRig rig;

        [TearDown]
        public void TearDown()
        {
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void ProjectionUsesFrozenNorthEastBasisAndCentersPlayer()
        {
            StreetConfig config = new StreetConfig();
            CreateRig(config);
            Point2 player = new Point2(2f, -3f);

            rig.Follow(player, true);

            Vector3 scene = streetCamera.WorldToViewportPoint(Scene(player));
            Vector3 east = streetCamera.WorldToViewportPoint(Scene(new Point2(player.X + 1f, player.Y)));
            Vector3 north = streetCamera.WorldToViewportPoint(Scene(new Point2(player.X, player.Y + 1f)));
            Assert.That(scene.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(scene.y, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(scene.z, Is.GreaterThan(0f));
            Assert.That(east.x, Is.GreaterThan(scene.x), "Point2 +X must project to screen right.");
            Assert.That(north.y, Is.GreaterThan(scene.y), "Point2 +Y must project toward screen north.");
            Assert.That(east.y, Is.EqualTo(scene.y).Within(1e-4f));
            Assert.That(north.x, Is.EqualTo(scene.x).Within(1e-4f));
            Assert.That(streetCamera.orthographic, Is.True);
            Assert.That(streetCamera.orthographicSize, Is.EqualTo(config.CameraSize).Within(1e-5f));
        }

        [Test]
        public void FollowAppliesConfiguredTargetOffsetAndSnap()
        {
            StreetConfig config = new StreetConfig
            {
                CameraTargetOffsetX = 1.25f,
                CameraTargetOffsetY = -0.4f,
                CameraTargetOffsetZ = 2.5f
            };
            CreateRig(config);
            Point2 player = new Point2(-4f, 6f);
            StreetVector3 mapped = StreetCoordinates.ToScene(player);
            Vector3 expectedTarget = new Vector3(
                mapped.X + config.CameraTargetOffsetX,
                mapped.Y + config.CameraTargetOffsetY,
                mapped.Z + config.CameraTargetOffsetZ);

            rig.Follow(player, true);

            Assert.That(streetCamera.transform.position,
                Is.EqualTo(StreetCameraRig.CameraPosition(expectedTarget, config)));
            Vector3 projectedTarget = streetCamera.WorldToViewportPoint(expectedTarget);
            Assert.That(projectedTarget.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(projectedTarget.y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void FollowReassertsFixedCameraOrientationAndSnapMovesToNewPosition()
        {
            StreetConfig config = new StreetConfig();
            CreateRig(config);
            rig.Follow(new Point2(1f, 2f), true);
            streetCamera.transform.rotation = Quaternion.Euler(10f, 73f, 4f);

            rig.Follow(new Point2(-5f, 7f), true);

            Quaternion expected = Quaternion.Euler(config.CameraTilt, StreetCoordinates.CameraYaw, 0f);
            Assert.That(Quaternion.Angle(streetCamera.transform.rotation, expected), Is.LessThan(1e-4f));
            Assert.That(streetCamera.transform.eulerAngles.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(streetCamera.transform.eulerAngles.z, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(streetCamera.transform.forward.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(streetCamera.transform.forward.y, Is.LessThan(0f));
            Assert.That(streetCamera.transform.forward.z, Is.GreaterThan(0f));
        }

        [Test]
        public void ExponentialFollowIsFrameStepIndependentAndZeroTimeDoesNotMove()
        {
            Vector3 current = new Vector3(-3f, 2f, 1f);
            Vector3 desired = new Vector3(8f, 6f, -9f);
            const float sharpness = 4.5f;

            Vector3 oneStep = StreetCameraRig.SmoothPosition(current, desired, sharpness, 0.2f);
            Vector3 twoSteps = StreetCameraRig.SmoothPosition(
                StreetCameraRig.SmoothPosition(current, desired, sharpness, 0.1f), desired, sharpness, 0.1f);
            Vector3 pausedStep = StreetCameraRig.SmoothPosition(current, desired, sharpness, 0f);

            Assert.That(Vector3.Distance(oneStep, twoSteps), Is.LessThan(1e-5f));
            Assert.That(pausedStep, Is.EqualTo(current));
            Assert.That(Vector3.Distance(current, oneStep), Is.GreaterThan(0f));
            Assert.That(Vector3.Distance(oneStep, desired), Is.GreaterThan(0f));
        }

        [Test]
        public void NonSnapFollowMovesTowardTargetWithoutTeleporting()
        {
            StreetConfig config = new StreetConfig { CameraFollowSharpness = 2f };
            CreateRig(config);
            rig.Follow(new Point2(0f, 0f), true);
            Vector3 original = streetCamera.transform.position;

            rig.Follow(new Point2(5f, 4f), false, 0.05f);
            float distanceAfterFollow = Vector3.Distance(streetCamera.transform.position,
                StreetCameraRig.CameraPosition(Scene(new Point2(5f, 4f)), config));
            rig.AdvanceFollow(0.1f);
            float distanceAfterExplicitStep = Vector3.Distance(streetCamera.transform.position,
                StreetCameraRig.CameraPosition(Scene(new Point2(5f, 4f)), config));

            Assert.That(Vector3.Distance(streetCamera.transform.position, original), Is.GreaterThan(0f));
            Assert.That(distanceAfterExplicitStep, Is.LessThan(distanceAfterFollow));
        }

        private void CreateRig(StreetConfig config)
        {
            cameraObject = new GameObject("Street camera test");
            streetCamera = cameraObject.AddComponent<Camera>();
            streetCamera.aspect = 16f / 9f;
            rig = cameraObject.AddComponent<StreetCameraRig>();
            rig.Configure(streetCamera, config);
        }

        private static Vector3 Scene(Point2 position)
        {
            StreetVector3 scene = StreetCoordinates.ToScene(position);
            return new Vector3(scene.X, scene.Y, scene.Z);
        }
    }
}
