using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace TideCombat
{
    // Synchronous world helpers shared by the task ports: capture, vision, OCR,
    // clicks, keys and waits. Each helper ticks the frame when needed.
    internal sealed class WorldSession : IDisposable
    {
        internal readonly CombatSettings Settings;
        internal readonly CancellationToken Token;
        internal readonly GameWindow Window;
        internal readonly FarmInput Input;
        internal readonly WindowInput BattleInput;
        internal readonly FarmVision Vision;
        internal readonly OcrReader Ocr;
        internal PixelFrame Frame;
        internal Rectangle Bounds;
        internal HudSnapshot Hud;
        internal FarmUi Ui;
        internal bool Captured;

        private readonly BattleLoop.WindowFrames capture;
        private readonly HudVision hudVision;
        private long lastTick;

        internal WorldSession(CombatSettings settings, CancellationToken token, OcrReader ocr = null)
        {
            Settings = settings;
            Token = token;
            Window = new GameWindow(settings.Source);
            Input = new FarmInput(Window);
            BattleInput = new WindowInput(Window);
            Vision = new FarmVision();
            capture = new BattleLoop.WindowFrames(Window);
            hudVision = new HudVision(settings);
            Ocr = ocr ?? new OcrReader(OcrReader.Directory);
        }

        internal bool Tick(bool detailed = true)
        {
            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (Captured && now - lastTick < 40)
                return true;
            lastTick = now;
            Captured = false;
            string reason;
            if (!Window.TryLocate(out Bounds, out reason))
                return false;
            if (Window.InputBlockedByIntegrity)
                throw new InputElevationRequiredException();
            PixelFrame frame;
            try
            {
                if (!capture.Read(Bounds, out frame) || !Window.Available())
                    return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
            Frame = frame;
            bool cloud = Settings.Source == GameSource.Cloud || Settings.Source == GameSource.ManagedCloud;
            try { Hud = hudVision.Read(frame, false, cloud, -1, false); }
            catch (Exception) { Hud = null; }
            try { Ui = Vision.Read(frame, detailed); }
            catch (Exception) { Ui = null; }
            Captured = true;
            return true;
        }

        internal void Sleep(int milliseconds)
        {
            Token.WaitHandle.WaitOne(Math.Max(0, milliseconds));
            Tick();
        }

        internal bool Click(double x, double y, int hold = 60)
        {
            if (!Tick())
                return false;
            var size = new Size(Frame.Width, Frame.Height);
            return Input.Click(new Point((int)(x * Frame.Width), (int)(y * Frame.Height)), size, Bounds,
                FarmMouse.Left, hold, Token);
        }

        internal bool Click(Rectangle box, int hold = 60)
        {
            if (box.IsEmpty || !Tick())
                return false;
            return Input.Click(new Point(box.X + box.Width / 2, box.Y + box.Height / 2),
                new Size(Frame.Width, Frame.Height), Bounds, FarmMouse.Left, hold, Token);
        }

        internal bool Key(char key, int hold = 60)
        {
            if (!Tick())
                return false;
            return Input.Key(key, hold, Token);
        }

        internal bool KeyDown(char key)
        {
            return Tick() && Input.KeyDown(key);
        }

        internal bool KeyUp(char key)
        {
            return Input.KeyUp(key);
        }

        internal bool FunctionKey(int number, int hold = 60)
        {
            return Tick() && Input.KeyCode((ushort)(0x6f + number), hold, Token);
        }

        internal bool Send(BattleAction action)
        {
            if (!Tick())
                return false;
            return BattleInput.Send(action, Token);
        }

        internal List<OcrLine> Read(double left, double top, double right, double bottom)
        {
            if (!Tick())
                return new List<OcrLine>();
            var area = new Rectangle((int)(Frame.Width * left), (int)(Frame.Height * top),
                Math.Max(1, (int)(Frame.Width * (right - left))),
                Math.Max(1, (int)(Frame.Height * (bottom - top))));
            try { return Ocr.Read(Frame, area); }
            catch (Exception) { return new List<OcrLine>(); }
        }

        internal bool FindNear(string label, double threshold, out Rectangle box)
        {
            box = Rectangle.Empty;
            if (!Tick())
                return false;
            return Vision.FindLabel(Frame, label, threshold, out box);
        }

        internal bool FindIn(string label, double left, double top, double right, double bottom, int step,
            double threshold, double light, out Rectangle box)
        {
            box = Rectangle.Empty;
            if (!Tick())
                return false;
            var area = new Rectangle((int)(Frame.Width * left), (int)(Frame.Height * top),
                Math.Max(1, (int)(Frame.Width * (right - left))),
                Math.Max(1, (int)(Frame.Height * (bottom - top))));
            return Vision.FindFirst(Frame, label, area, step, threshold, light, out box);
        }

        internal List<Rectangle> FindAll(string label, double left, double top, double right, double bottom,
            int step, double threshold, double light)
        {
            if (!Tick())
                return new List<Rectangle>();
            var area = new Rectangle((int)(Frame.Width * left), (int)(Frame.Height * top),
                Math.Max(1, (int)(Frame.Width * (right - left))),
                Math.Max(1, (int)(Frame.Height * (bottom - top))));
            try { return Vision.FindAll(Frame, label, area, step, threshold, light); }
            catch (Exception) { return new List<Rectangle>(); }
        }

        internal bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (Token.IsCancellationRequested)
                    return false;
                if (condition())
                    return true;
                if (!Captured)
                    Tick();
                Token.WaitHandle.WaitOne(60);
            }
            return false;
        }

        internal bool WaitFind(string label, int timeoutMs, double threshold = .86)
        {
            return WaitUntil(delegate
            {
                Rectangle box;
                return FindNear(label, threshold, out box);
            }, timeoutMs);
        }

        internal bool WaitClick(string label, int timeoutMs, double threshold = .86)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                Rectangle box;
                if (FindNear(label, threshold, out box))
                {
                    Sleep(150);
                    return Click(box);
                }
                Token.WaitHandle.WaitOne(80);
            }
            return false;
        }

        internal bool InRealm { get { return Ui != null && Ui.InRealm; } }
        internal bool TeamHud { get { return Hud != null && Hud.Valid; } }

        public void Dispose()
        {
            try { BattleInput.ReleaseAll(); } catch (Exception) { }
            try { Input.ReleaseAll(); } catch (Exception) { }
            capture.Dispose();
            Ocr.Dispose();
        }
    }
}
