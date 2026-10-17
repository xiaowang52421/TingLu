using System;
using TingLu.Contracts;
using TingLu.Config;

namespace TingLu.Input
{
    // Backends feed this same source from Input System keyboard, gamepad and touch actions.
    // Keeping the arbitration here means every control path produces identical gameplay input.
    public sealed class UnifiedInputSource : IInputSource
    {
        private readonly GameplayConfig config;
        private InputFrame keyboard, gamepad, touch;
        private long keyboardAt, gamepadAt, touchAt, sequence;
        public UnifiedInputSource(GameplayConfig tuning) { config=tuning??throw new ArgumentNullException("tuning");config.Validate(); }
        public void SubmitKeyboard(Point2 move,bool listen,bool tap,bool pause,MenuAction menu) { keyboard=new InputFrame(move,listen,tap,pause,menu);keyboardAt=++sequence; }
        public void SubmitGamepad(Point2 move,bool listen,bool tap,bool pause,MenuAction menu) { gamepad=new InputFrame(DeadZone(move),listen,tap,pause,menu);gamepadAt=++sequence; }
        public void SubmitTouch(Point2 move,bool listen,bool tap,bool pause,MenuAction menu) { touch=new InputFrame(DeadZone(move),listen,tap,pause,menu);touchAt=++sequence; }
        public void Release(InputDeviceKind device)
        { if(device==InputDeviceKind.Keyboard){keyboard=new InputFrame(new Point2(0,0),false,false,false,MenuAction.None);keyboardAt=++sequence;}else if(device==InputDeviceKind.Gamepad){gamepad=new InputFrame(new Point2(0,0),false,false,false,MenuAction.None);gamepadAt=++sequence;}else{touch=new InputFrame(new Point2(0,0),false,false,false,MenuAction.None);touchAt=++sequence;} }
        public InputFrame Read()
        {
            InputFrame selected=keyboardAt>=gamepadAt&&keyboardAt>=touchAt?keyboard:gamepadAt>=touchAt?gamepad:touch;
            // Edge-triggered actions are collected across devices; held movement/listening comes
            // from the most recently active device so a resting touchscreen cannot cancel a pad.
            bool tap=keyboard.TapPressed||gamepad.TapPressed||touch.TapPressed;
            bool pause=keyboard.PausePressed||gamepad.PausePressed||touch.PausePressed;
            MenuAction menu=keyboard.Menu!=MenuAction.None?keyboard.Menu:gamepad.Menu!=MenuAction.None?gamepad.Menu:touch.Menu;
            var result=new InputFrame(selected.Move,selected.ListenHeld,tap,pause,menu);
            keyboard=WithoutEdges(keyboard);gamepad=WithoutEdges(gamepad);touch=WithoutEdges(touch);
            return result;
        }
        private Point2 DeadZone(Point2 p){float m=(float)Math.Sqrt(p.X*p.X+p.Y*p.Y);if(m<=config.stickDeadZone)return new Point2(0,0);if(m<=1)return p;return new Point2(p.X/m,p.Y/m);}
        private static InputFrame WithoutEdges(InputFrame f){return new InputFrame(f.Move,f.ListenHeld,false,false,MenuAction.None);}
    }
    public enum InputDeviceKind { Keyboard, Gamepad, Touch }
}
