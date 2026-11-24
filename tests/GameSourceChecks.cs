using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace TideCombat
{
    internal static class GameSourceChecks
    {
        private static readonly GameSource[] ExistingModes =
        {
            GameSource.Foreground, GameSource.Background, GameSource.Cloud, GameSource.ManagedCloud
        };

        internal static void AutomaticMode()
        {
            Require(GameWindow.ResolveMode(GameSource.AutoLocal, true) == GameSource.Foreground,
                "an active local game needs desktop capture and system input");
            Require(GameWindow.ResolveMode(GameSource.AutoLocal, false) == GameSource.Background,
                "an inactive local game needs window capture and window messages");
            Require(GameWindow.ForTask(GameSource.AutoLocal).Mode == GameSource.Background &&
                GameWindow.ForTask(GameSource.Background).Mode == GameSource.Background &&
                GameWindow.ForTask(GameSource.Foreground).Mode == GameSource.Foreground,
                "menu tasks must preserve configured capture modes before discovering the game window");
            Require(CombatSettings.Defaults().Source == GameSource.AutoLocal,
                "fresh settings must discover the local window automatically");
            foreach (GameSource source in ExistingModes)
            {
                Require(GameWindow.ResolveMode(source, true) == source &&
                    GameWindow.ResolveMode(source, false) == source,
                    "a manually selected mode changed with focus: " + source);
            }

            var input = new WindowInput(new GameWindow(GameSource.AutoLocal));
            Require(!input.Send(BattleAction.Attack(), CancellationToken.None),
                "automatic mode sent input before locating a game window");
        }

        internal static void SavedSource()
        {
            for (int source = 0; source < ExistingModes.Length; source++)
                Require((GameSource)source == ExistingModes[source], "an old saved mode changed at value " + source);

            string path = Path.Combine(Path.GetTempPath(), "TideCombat-source-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                CombatSettings settings = CombatSettings.Defaults();
                settings.Source = GameSource.AutoLocal;
                SettingsFile.Save(path, settings);
                Require(SettingsFile.Load(path).Source == GameSource.AutoLocal, "the automatic mode was not restored");
                settings.Source = GameSource.Foreground;
                SettingsFile.Save(path, settings);
                Require(SettingsFile.Load(path).Source == GameSource.Foreground,
                    "an explicitly saved foreground source must remain foreground");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        internal static void MonthlyCard(string root)
        {
            using (var image = new Bitmap(Path.Combine(root, "ok_templates", "monthly_card.png")))
            using (var reader = new FrameReader())
            {
                var matcher = new FarmVision();
                PixelFrame frame = reader.Read(image);
                Rectangle card;
                Require(matcher.FindMonthlyCard(frame, out card) && !card.IsEmpty &&
                    MonthlyCardDismissal.Button(frame) == new Point(frame.Width / 2, (int)(frame.Height * .89)),
                    "the archived monthly card uses the bottom-center dismissal control");
                var empty = new PixelFrame(new byte[frame.Width * frame.Height * 4], frame.Width, frame.Height);
                Require(!matcher.FindMonthlyCard(empty, out card),
                    "a frame without the prompt must not send a dismissal");
            }
        }

        internal static void MonthlyCardGeometry(string root)
        {
            string file = Path.Combine(root, "assets", "templates", "monthly_card_1920x1080_841_582.png");
            using (var label = new Bitmap(file))
            using (var screenshot = new Bitmap(2560, 1080))
            using (var reader = new FrameReader())
            using (var graphics = Graphics.FromImage(screenshot))
            {
                var vision = new FarmVision();
                Rectangle found;
                graphics.Clear(Color.Black);
                graphics.DrawImageUnscaled(label, 1033, 636);
                Require(vision.FindMonthlyCard(reader.Read(screenshot), out found) &&
                    Math.Abs(found.Left - 861) < 2 && found.Top == 530,
                    "both axes use screen-center anchoring and the five percent search range");
                graphics.Clear(Color.Black);
                graphics.DrawImageUnscaled(label, 1033, 660);
                Require(!vision.FindMonthlyCard(reader.Read(screenshot), out found), "a card outside the search range is excluded");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
