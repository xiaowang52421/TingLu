#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Contracts;
using TingLu.Presentation;

namespace TingLu.Tests.Presentation
{
    public sealed class PresentationRulesTests
    {
        [Test]
        public void SoundLocationCreatesLocatorWithoutInventedContour()
        {
            var observation=new Observation("source",ObjectCategory.Dynamic,MemoryPolicy.Never,ObservationSource.PassiveSound,
                InformationKind.SoundLocation,new Point2(0,2),new Segment2[0],"person",0,1);
            var composer=new PresentationFrameComposer();
            var frame=composer.Compose(new Perception(observation),new Memory(),new Player(),new UserSettings(DisplayMode.Standard,OutputMode.Headphones,1,true,false),0.5);
            Assert.AreEqual(1,frame.Marks.Count);
            Assert.AreEqual(VoiceShape.LocatorArc,frame.Marks[0].Shape);
            Assert.AreEqual(0,frame.Marks[0].Segments.Count);
        }
        [Test]
        public void AudioOnlyRemovesAllGameVisuals()
        {
            var composer=new PresentationFrameComposer();
            var frame=composer.Compose(new Perception(),new Memory(),new Player(),new UserSettings(DisplayMode.AudioOnly,OutputMode.Speakers,1,false,false),0);
            Assert.IsFalse(frame.Visible);Assert.AreEqual(0,frame.Marks.Count);Assert.AreEqual(0,frame.ViewSector.Count);Assert.IsFalse(frame.PainFlash);
        }
        [Test]
        public void PainFlashExpiresAtOneSimulationSecond()
        {
            var composer=new PresentationFrameComposer();composer.Accept(new GameplayEvent("hit",0,"l1-1","pole",new Point2(0,0),SemanticType.Collision,FeedbackPriority.CriticalThreat,new CollisionPayload(10,0,new Point2(0,0))));
            var settings=new UserSettings(DisplayMode.NoUI,OutputMode.Headphones,1,false,false);
            Assert.IsTrue(composer.Compose(new Perception(),new Memory(),new Player(),settings,0.99).PainFlash);
            Assert.IsFalse(composer.Compose(new Perception(),new Memory(),new Player(),settings,1).PainFlash);
        }
        [Test]
        public void NewGenerationClearsOldPainWhenSimulationRestarts()
        {
            var composer=new PresentationFrameComposer();
            composer.Accept(new GameplayEvent("old-hit",10,"l1-1","pole",new Point2(0,0),SemanticType.Collision,FeedbackPriority.CriticalThreat,new CollisionPayload(10,0,new Point2(0,0))));
            composer.ResetFeedback();
            var settings=new UserSettings(DisplayMode.Standard,OutputMode.Headphones,1,true,false);
            Assert.IsFalse(composer.Compose(new Perception(),new Memory(),new Player(),settings,0).PainFlash);
        }
        private sealed class Perception:IPerceptionReader{public PerceptionFrame Current{get;private set;}public Perception(params Observation[] values){Current=new PerceptionFrame(1,0,values);}}
        private sealed class Memory:IMemoryReader{public IReadOnlyList<StaticMemorySnapshot> Fragments{get{return new StaticMemorySnapshot[0];}}}
        private sealed class Player:IPlayerStateReader{public PlayerState Current{get{return new PlayerState(PlayerMainState.Active,ActionMode.Walking,new Point2(0,0),new Point2(0,1),100,100,0,0,0);}}}
    }
}
#endif
