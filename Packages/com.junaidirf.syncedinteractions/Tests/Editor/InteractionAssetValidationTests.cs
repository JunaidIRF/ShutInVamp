using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using JunaidIRF.SyncedInteractions;

namespace JunaidIRF.SyncedInteractions.Tests.Editor
{
    public class InteractionAssetValidationTests
    {
        [Test]
        public void EmptyAsset_FailsValidation()
        {
            var asset = ScriptableObject.CreateInstance<InteractionAsset>();
            asset.roles.Clear();
            var problems = asset.Validate();
            Assert.IsTrue(problems.Count > 0, "Empty asset should have validation problems");
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void ValidTwoRoleAsset_PassesValidation()
        {
            var asset = ScriptableObject.CreateInstance<InteractionAsset>();
            var clipA = new AnimationClip { name = "ClipA" };
            var clipB = new AnimationClip { name = "ClipB" };

            // Add dummy curve so clip has length
            clipA.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1f, 1f));
            clipB.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1.5f, 1f));

            asset.roles[0].clip = clipA;
            asset.roles[1].clip = clipB;

            var problems = asset.Validate();
            Assert.AreEqual(0, problems.Count, "Valid asset should have no validation problems: " + string.Join(", ", problems));
            Assert.AreEqual(1.5f, asset.Length, 1e-4f);

            Object.DestroyImmediate(asset);
            Object.DestroyImmediate(clipA);
            Object.DestroyImmediate(clipB);
        }

        [Test]
        public void Cast_InOrder_MapsRolesCorrectly()
        {
            var asset = ScriptableObject.CreateInstance<InteractionAsset>();
            var goA = new GameObject("A");
            var goB = new GameObject("B");
            var a = goA.AddComponent<InteractionActor>();
            var b = goB.AddComponent<InteractionActor>();

            var cast = InteractionCast.InOrder(asset, a, b);
            Assert.AreEqual(a, cast["Initiator"]);
            Assert.AreEqual(b, cast["Receiver"]);

            Object.DestroyImmediate(asset);
            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
        }
    }
}
