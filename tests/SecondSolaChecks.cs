using System;
using System.Drawing;

namespace TideCombat
{
    internal static class SecondSolaChecks
    {
        internal static void Decisions()
        {
            using (var image = new Bitmap(1600, 900))
            using (var canvas = Graphics.FromImage(image))
            using (var reader = new FrameReader())
            {
                canvas.Clear(Color.Black);
                canvas.FillEllipse(Brushes.Red, 692, 422, 32, 33);
                canvas.FillRectangle(Brushes.White, 705, 428, 6, 19);
                var labels = new[]
                {
                    Text("０９／０２ 周二", 68, 48),
                    Text("８２％", 1000, 60),
                    Text("教学楼", 633, 468),
                    Text("24", 1030, 380),
                    Text("25", 1030, 470)
                };
                var screen = new SecondSolaScreen(reader.Read(image), labels);
                Require(screen.Date == 902 && screen.Fatigue() == 82, "normalize dates and fatigue as displayed");
                var marker = screen.MapMarker();
                Require(marker.Item1 != null && marker.Item1.Text == "教学楼", "prefer a recognized map event marker's nearby building");
                OcrLine answer = SecondSolaRunner.ExamAnswer(screen.In(.60, .3, .8, .7), screen.Date);
                Require(answer != null && answer.Text == "25", "select the answer for the current exam date");
                Require(SecondSolaRunner.ExamAnswer(screen.Lines, 101) == null, "unrelated dates do not use an exam answer");
                OcrLine focused = screen.ResumeJourney(() => new[] { Text("继续 旅程", 980, 630) });
                Require(focused != null && focused.Text == "继续旅程",
                    "use focused OCR for the continuation button when full-screen OCR misses it");
                var alreadyFound = new SecondSolaScreen(screen.Frame, new[] { Text("继续旅程", 1170, 640) });
                Require(alreadyFound.ResumeJourney(() => { throw new InvalidOperationException(); }) != null,
                    "use an already recognized continuation button without running OCR again");
            }
        }

        private static OcrLine Text(string value, int x, int y)
        {
            return new OcrLine { Text = value, Box = new Rectangle(x, y, 100, 20), Score = .95f };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
