using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using JunaidIRF.SyncedInteractions;

namespace JunaidIRF.SyncedInteractions.Tests.Editor
{
    public class InteractionRuntimeTests
    {
        class TestParticipant : MonoBehaviour, IInteractionParticipant
        {
            public int startCount;
            public int endCount;
            public bool wasCancelled;

            public void OnInteractionStart(InteractionHandle handle, string role) => startCount++;
            public void OnInteractionEnd(InteractionHandle handle, string role, bool cancelled)
            {
                endCount++;
                wasCancelled = cancelled;
            }
        }

        [Test]
        public void Events_FireInOrder_WhenStepped()
        {
            var asset = ScriptableObject.CreateInstance<InteractionAsset>();
            asset.align = AlignMode.None;
            var clipA = new AnimationClip { name = "ClipA" };
            var clipB = new AnimationClip { name = "ClipB" };
            clipA.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1f, 1f));
            clipB.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1f, 1f));
            asset.roles[0].clip = clipA;
            asset.roles[1].clip = clipB;

            var firedEvents = new List<string>();
            var evt1 = new InteractionEvent { name = "First", time = 0.2f };
            var evt2 = new InteractionEvent { name = "Second", time = 0.5f };
            asset.events.Add(evt1);
            asset.events.Add(evt2);

            var goA = new GameObject("A");
            var goB = new GameObject("B");
            var a = goA.AddComponent<InteractionActor>();
            var b = goB.AddComponent<InteractionActor>();
            goA.AddComponent<Animator>();
            goB.AddComponent<Animator>();

            var cast = InteractionCast.InOrder(asset, a, b);
            var handle = new InteractionHandle(asset, cast, new InteractionOptions());
            handle.EventFired += (h, e) => firedEvents.Add(e.name);
            handle.Begin();

            // Step before first event
            handle.Tick(0.1f, 0.1f);
            Assert.AreEqual(0, firedEvents.Count);

            // Step past first event (t = 0.3)
            handle.Tick(0.2f, 0.2f);
            Assert.AreEqual(1, firedEvents.Count);
            Assert.AreEqual("First", firedEvents[0]);

            // Step past second event (t = 0.6)
            handle.Tick(0.3f, 0.3f);
            Assert.AreEqual(2, firedEvents.Count);
            Assert.AreEqual("Second", firedEvents[1]);

            handle.Cancel();

            Object.DestroyImmediate(asset);
            Object.DestroyImmediate(clipA);
            Object.DestroyImmediate(clipB);
            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
        }

        [Test]
        public void Cancel_RestoresActorsAndNotifiesParticipants()
        {
            var asset = ScriptableObject.CreateInstance<InteractionAsset>();
            asset.align = AlignMode.None;
            var clipA = new AnimationClip { name = "ClipA" };
            var clipB = new AnimationClip { name = "ClipB" };
            clipA.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1f, 1f));
            clipB.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1f, 1f));
            asset.roles[0].clip = clipA;
            asset.roles[1].clip = clipB;

            var goA = new GameObject("A");
            var goB = new GameObject("B");
            var a = goA.AddComponent<InteractionActor>();
            var b = goB.AddComponent<InteractionActor>();
            goA.AddComponent<Animator>();
            goB.AddComponent<Animator>();
            var part = goB.AddComponent<TestParticipant>();

            var cast = InteractionCast.InOrder(asset, a, b);
            var handle = new InteractionHandle(asset, cast, new InteractionOptions());
            handle.Begin();

            Assert.IsTrue(a.Busy);
            Assert.IsTrue(b.Busy);
            Assert.AreEqual(1, part.startCount);

            handle.Cancel();

            Assert.IsFalse(a.Busy);
            Assert.IsFalse(b.Busy);
            Assert.AreEqual(1, part.endCount);
            Assert.IsTrue(part.wasCancelled);

            Object.DestroyImmediate(asset);
            Object.DestroyImmediate(clipA);
            Object.DestroyImmediate(clipB);
            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
        }
    }
}
