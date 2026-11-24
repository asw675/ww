using System;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    internal static class GuidebookVisionChecks
    {
        internal static void ReplayShifted(string root)
        {
            var vision = new FarmVision();
            using (var reader = new FrameReader())
            {
                foreach (string file in new[] { "5.png", "6.png" })
                using (var source = new Bitmap(Path.Combine(root, "ok_templates", file)))
                using (var shifted = new Bitmap(source.Width, source.Height))
                {
                    using (Graphics graphics = Graphics.FromImage(shifted))
                    {
                        graphics.Clear(Color.Black);
                        graphics.DrawImageUnscaled(source, 6, 26);
                    }

                    Rectangle boss;
                    bool selected;
                    Require(vision.InspectGuidebook(reader.Read(shifted), out boss, out selected) &&
                        boss.Contains(new Point(69, 296)) && selected == (file == "5.png"),
                        "shifted Boss tab did not follow the verified Guidebook sidebar in " + file);
                    Rectangle quest;
                    Require(vision.FindGuidebookQuest(reader.Read(shifted), out quest) &&
                        quest.Contains(new Point(70, 184)),
                        "shifted quest tab did not follow the verified Guidebook sidebar in " + file);

                    using (Graphics graphics = Graphics.FromImage(shifted))
                        graphics.FillRectangle(Brushes.Black, 25, 249, 90, 102);
                    Require(vision.InspectGuidebook(reader.Read(shifted), out boss, out selected) &&
                        boss.IsEmpty && !selected, "an obscured Boss tab should not be clicked in " + file);
                }

                using (var world = new Bitmap(Path.Combine(root, "tests", "images", "echo2.png")))
                {
                    Rectangle boss;
                    bool selected;
                    Require(!vision.InspectGuidebook(reader.Read(world), out boss, out selected),
                        "the open world must not match the Guidebook sidebar");
                }
            }
        }

        internal static void ReplayTerminal(string root)
        {
            var vision = new FarmVision();
            using (var reader = new FrameReader())
            using (var terminal = new Bitmap(Path.Combine(root, "ok_templates", "14.png")))
            {
                Rectangle guide;
                PixelFrame frame = reader.Read(terminal);
                Require(vision.IsTerminal(frame) && vision.FindTerminalGuide(frame, out guide) &&
                    guide.Contains(new Point(793, 624)), "the Terminal must provide a verified Guidebook tile");
                Rectangle mail;
                Require(vision.FindTerminalMail(frame, out mail) && mail.Contains(new Point(1028, 860)),
                    "the Terminal envelope must be found in the bottom shortcut row");

                using (var shifted = new Bitmap(terminal.Width, terminal.Height))
                {
                    using (Graphics graphics = Graphics.FromImage(shifted))
                    {
                        graphics.Clear(Color.Black);
                        graphics.DrawImageUnscaled(terminal, 8, 12);
                    }
                    frame = reader.Read(shifted);
                    Require(vision.IsTerminal(frame) && vision.FindTerminalGuide(frame, out guide) &&
                        guide.Contains(new Point(800, 634)), "the Terminal cues must tolerate minor viewport displacement");
                    Require(vision.FindTerminalMail(frame, out mail) && mail.Contains(new Point(1034, 870)),
                        "the mail shortcut must follow a minor viewport displacement");

                    using (Graphics graphics = Graphics.FromImage(shifted))
                        graphics.FillRectangle(Brushes.Black, 1194, 1017, 90, 63);
                    Require(vision.IsTerminal(reader.Read(shifted)) &&
                        !vision.FindTerminalMail(reader.Read(shifted), out mail),
                        "a hidden envelope must not produce a click target");

                    using (Graphics graphics = Graphics.FromImage(shifted))
                        graphics.FillRectangle(Brushes.Black, 901, 686, 120, 130);
                    frame = reader.Read(shifted);
                    Require(vision.IsTerminal(frame) && !vision.FindTerminalGuide(frame, out guide),
                        "the Terminal alone must not provide an unverified Guidebook tile");

                    using (Graphics graphics = Graphics.FromImage(shifted))
                        graphics.FillRectangle(Brushes.Black, 50, 12, 100, 110);
                    Require(!vision.IsTerminal(reader.Read(shifted)),
                        "the close button and settings gear do not confirm the Terminal without its title mark");
                }

                using (var book = new Bitmap(Path.Combine(root, "ok_templates", "5.png")))
                using (var world = new Bitmap(Path.Combine(root, "tests", "images", "echo2.png")))
                    Require(!vision.IsTerminal(reader.Read(book)) && !vision.IsTerminal(reader.Read(world)),
                        "the Guidebook and overworld must not be treated as the Terminal");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
