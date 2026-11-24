using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TideCombat
{
    internal sealed class CloudBrowser : IDisposable
    {
        internal const string OfficialPage = "https://mc.kurogames.com/cloud/#/";
        private static readonly string Profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TideCombat", "CloudBrowser");

        private readonly string profile;
        private readonly Func<string, bool> allowsPage;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private readonly FrameReader reader = new FrameReader(true);
        private readonly Dictionary<BattleButton, BrowserButton> held = new Dictionary<BattleButton, BrowserButton>();
        private ClientWebSocket socket;
        private Rectangle viewport;
        private int commandId, mouseButtons;
        private bool pageVerified;
        internal int RouteVersion { get; private set; }
        private BrowserButton? pendingTap;

        private struct BrowserButton
        {
            internal int Key;
            internal string Mouse;
            internal int MouseMask;
            internal Point? MousePosition;
        }

        internal CloudBrowser() : this(Profile, IsOfficialPage) { }

        internal CloudBrowser(string profile, Func<string, bool> allowsPage)
        {
            this.profile = profile;
            this.allowsPage = allowsPage;
        }

        internal static bool IsOfficialPage(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.IsDefaultPort &&
                uri.Host == "mc.kurogames.com" && uri.AbsolutePath.StartsWith("/cloud/", StringComparison.Ordinal);
        }

        internal static void Open()
        {
            string browser = FindBrowser();
            if (browser == null) throw new InvalidOperationException("请安装 Microsoft Edge 或 Google Chrome。");
            var start = new ProcessStartInfo(browser, LaunchArguments(Profile, OfficialPage, false)) { UseShellExecute = false };
            using (Process spawned = Process.Start(start)) { }
        }

        internal static string FindBrowser()
        {
            string[] locations = { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            foreach (string root in locations.Where(path => !string.IsNullOrEmpty(path)).Distinct())
                foreach (string program in new[] { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" })
                {
                    string executable = Path.Combine(root, program);
                    if (File.Exists(executable)) return executable;
                }
            return null;
        }

        internal static string LaunchArguments(string profile, string page, bool headless)
        {
            string options = "--no-first-run --remote-debugging-port=0 --remote-debugging-address=127.0.0.1 " +
                "--disable-backgrounding-occluded-windows --disable-renderer-backgrounding --force-color-profile=srgb " +
                "--disable-direct-composition --disable-features=CalculateNativeWinOcclusion --window-size=1600,900 ";
            return (headless ? "--headless=new --disable-gpu --disable-extensions --disable-background-networking --no-default-browser-check " : "") +
                options + "--user-data-dir=\"" + profile + "\" " + (headless ? "" : "--app=") + "\"" + page + "\"";
        }

        internal bool TryRead(CancellationToken token, out PixelFrame frame, out Size size, bool nativePixels = false)
        {
            frame = default(PixelFrame);
            size = Size.Empty;
            using (var call = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                call.CancelAfter(1450);
                try
                {
                    if (socket == null || socket.State != WebSocketState.Open)
                    {
                        Reset();
                        Uri endpoint;
                        if (!TryFindEndpoint(out endpoint)) return false;
                        socket = new ClientWebSocket();
                        socket.Options.Proxy = null;
                        socket.ConnectAsync(endpoint, call.Token).GetAwaiter().GetResult();
                        RouteVersion++;
                    }
                    if (!RefreshViewport(call.Token)) return false;
                    size = viewport.Size;
                    var clip = new { x = viewport.X, y = viewport.Y, width = viewport.Width, height = viewport.Height, scale = 1 };
                    var result = Command("Page.captureScreenshot", new { format = "jpeg", quality = 90, fromSurface = true,
                        captureBeyondViewport = false, clip }, call.Token);
                    byte[] bytes = Convert.FromBase64String((string)result["data"]);
                    using (var stream = new MemoryStream(bytes, false))
                    using (var bitmap = new Bitmap(stream)) frame = reader.Read(bitmap, nativePixels);
                    return true;
                }
                catch (OperationCanceledException) { Reset(); return false; }
                catch (WebSocketException) { Reset(); return false; }
                catch (InvalidDataException) { Reset(); return false; }
                catch (InvalidOperationException) { Reset(); return false; }
                catch (ArgumentException) { Reset(); return false; }
            }
        }

        private bool TryFindEndpoint(out Uri endpoint)
        {
            endpoint = null;
            try
            {
                int port;
                string file = Path.Combine(profile, "DevToolsActivePort");
                if (!File.Exists(file) || !int.TryParse(File.ReadLines(file).FirstOrDefault(), out port) ||
                    port < 1 || port > 65535) return false;
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
                request.Proxy = null;
                request.Timeout = request.ReadWriteTimeout = 600;
                using (var response = request.GetResponse())
                using (var body = new StreamReader(response.GetResponseStream()))
                {
                    var pages = json.Deserialize<Dictionary<string, string>[]>(body.ReadToEnd());
                    foreach (Dictionary<string, string> page in pages)
                    {
                        string type, url, address;
                        if (page.TryGetValue("type", out type) && type == "page" && page.TryGetValue("url", out url) && allowsPage(url) &&
                            page.TryGetValue("webSocketDebuggerUrl", out address) && Uri.TryCreate(address, UriKind.Absolute, out endpoint) &&
                            endpoint.Scheme == "ws" && endpoint.Host == "127.0.0.1" && endpoint.Port == port) return true;
                    }
                }
            }
            catch (IOException) { }
            catch (WebException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return false;
        }

        private bool RefreshViewport(CancellationToken token)
        {
            const string expression = @"(() => {
                const W = window.innerWidth, H = window.innerHeight;
                const candidates = Array.from(document.querySelectorAll('video,canvas,iframe')).map(el => {
                    const b = el.getBoundingClientRect();
                    const r = { x: Math.max(0,b.left), y: Math.max(0,b.top),
                        width: Math.min(W,b.right)-Math.max(0,b.left), height: Math.min(H,b.bottom)-Math.max(0,b.top) };
                    const ratio = el.videoWidth && el.videoHeight ? el.videoWidth/el.videoHeight : 16/9;
                    return Object.assign(r,{ratio, area:r.width*r.height});
                }).filter(r => r.width >= W*0.65 && r.height >= H*0.65).sort((a,b)=>b.area-a.area);
                const r = candidates[0] || {x:0,y:0,width:W,height:H,ratio:16/9};
                const width = Math.min(r.width,r.height*r.ratio), height = Math.min(r.height,r.width/r.ratio);
                return {url:location.href,x:Math.round(r.x+(r.width-width)/2),y:Math.round(r.y+(r.height-height)/2),
                    width:Math.floor(width),height:Math.floor(height)};
            })()";
            var result = Command("Runtime.evaluate", new { expression, returnByValue = true }, token);
            var remote = result["result"] as Dictionary<string, object>;
            object value;
            var detail = remote != null && remote.TryGetValue("value", out value) ? value as Dictionary<string, object> : null;
            if (detail == null || !allowsPage((string)detail["url"]))
            {
                ReleaseAll();
                Reset();
                return false;
            }
            viewport = new Rectangle((int)detail["x"], (int)detail["y"], (int)detail["width"], (int)detail["height"]);
            pageVerified = viewport.Width >= 950 && viewport.Height >= 530;
            return pageVerified;
        }

        internal bool Send(BattleAction action, CancellationToken token)
        {
            if (!pageVerified || socket == null || action.Kind == ActionKind.Wait || token.IsCancellationRequested) return false;
            using (var call = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                call.CancelAfter(700);
                try
                {
                    if (action.Kind == ActionKind.Release)
                    {
                        Release(action.Button, call.Token);
                        return true;
                    }
                    BrowserButton button = Translate(action);
                    if (button.Key == 0 && button.Mouse == null) return false;
                    if (action.Kind == ActionKind.Hold && held.ContainsKey(action.Button))
                    {
                        if (action.ReassertHold) SetButton(button, true, call.Token);
                        return true;
                    }
                    if (action.Kind == ActionKind.Hold) held.Add(action.Button, button);
                    else pendingTap = button;
                    SetButton(button, true, call.Token);
                    if (pendingTap.HasValue)
                    {
                        call.Token.WaitHandle.WaitOne(action.DownMilliseconds > 0 ? action.DownMilliseconds : 35);
                        SetButton(button, false, call.Token);
                        pendingTap = null;
                    }
                    return true;
                }
                catch (Exception error)
                {
                    if (!(error is WebSocketException || error is InvalidDataException || error is OperationCanceledException)) throw;
                    ReleaseAll();
                    Reset();
                    return false;
                }
            }
        }

        internal bool Click(Point framePoint, Size frameSize, CancellationToken token, int hold = 0)
        {
            if (!pageVerified || socket == null || token.IsCancellationRequested || frameSize.IsEmpty ||
                framePoint.X < 0 || framePoint.Y < 0 || framePoint.X >= frameSize.Width || framePoint.Y >= frameSize.Height)
                return false;
            var mouse = new BrowserButton
            {
                Mouse = "left", MouseMask = 1,
                MousePosition = new Point(viewport.X + framePoint.X * viewport.Width / frameSize.Width,
                    viewport.Y + framePoint.Y * viewport.Height / frameSize.Height)
            };
            using (var call = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                call.CancelAfter(700);
                try
                {
                    pendingTap = mouse;
                    SetButton(mouse, true, call.Token);
                    if (hold > 0 && call.Token.WaitHandle.WaitOne(hold)) call.Token.ThrowIfCancellationRequested();
                    SetButton(mouse, false, call.Token);
                    pendingTap = null;
                    return true;
                }
                catch (Exception error)
                {
                    if (!(error is WebSocketException || error is InvalidDataException || error is OperationCanceledException)) throw;
                    ReleaseAll();
                    Reset();
                    return false;
                }
            }
        }

        private static BrowserButton Translate(BattleAction action)
        {
            if (action.Kind == ActionKind.Target || action.Button == BattleButton.MiddleClick)
                return new BrowserButton { Mouse = "middle", MouseMask = 4 };
            if (action.Kind == ActionKind.Switch) return new BrowserButton { Key = '1' + action.Slot };
            if (action.Kind == ActionKind.Cast) return new BrowserButton { Key = action.Key };
            if (action.Kind == ActionKind.Attack || action.Button == BattleButton.Attack)
                return new BrowserButton { Mouse = "left", MouseMask = 1 };
            if (action.Button == BattleButton.Dodge) return new BrowserButton { Mouse = "right", MouseMask = 2 };
            return new BrowserButton { Key = action.NativeKey };
        }

        private void SetButton(BrowserButton button, bool down, CancellationToken token)
        {
            if (button.Mouse != null)
            {
                mouseButtons = down ? mouseButtons | button.MouseMask : mouseButtons & ~button.MouseMask;
                Point position = button.MousePosition ?? new Point(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
                Command("Input.dispatchMouseEvent", new { type = down ? "mousePressed" : "mouseReleased", button = button.Mouse,
                    buttons = mouseButtons, clickCount = 1, x = position.X, y = position.Y }, token);
                return;
            }
            char letter = (char)button.Key;
            string code = letter == ' ' ? "Space" : letter >= '0' && letter <= '9' ? "Digit" + letter : "Key" + letter;
            Command("Input.dispatchKeyEvent", new { type = down ? "rawKeyDown" : "keyUp", code,
                key = char.ToLowerInvariant(letter).ToString(), windowsVirtualKeyCode = button.Key,
                nativeVirtualKeyCode = button.Key }, token);
        }

        private Dictionary<string, object> Command(string method, object parameters, CancellationToken token)
        {
            int id = ++commandId;
            byte[] payload = Encoding.UTF8.GetBytes(json.Serialize(new { id, method, @params = parameters }));
            socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, token).GetAwaiter().GetResult();
            byte[] buffer = new byte[32768];
            using (var message = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult received = socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).GetAwaiter().GetResult();
                    if (received.MessageType == WebSocketMessageType.Close || message.Length + received.Count > json.MaxJsonLength)
                        throw new InvalidDataException("云游戏浏览器连接已关闭或返回了过大的画面。");
                    message.Write(buffer, 0, received.Count);
                    if (!received.EndOfMessage) continue;
                    var reply = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    message.SetLength(0);
                    object sequence;
                    if (!reply.TryGetValue("id", out sequence) || !Equals(sequence, id)) continue;
                    object data;
                    if (!reply.TryGetValue("result", out data)) throw new InvalidDataException("云游戏浏览器未接受操作。");
                    return (Dictionary<string, object>)data;
                }
            }
        }

        private void Release(BattleButton button, CancellationToken token)
        {
            BrowserButton native;
            if (!held.TryGetValue(button, out native)) return;
            SetButton(native, false, token);
            held.Remove(button);
        }

        internal bool ReleaseAll()
        {
            if (held.Count == 0 && !pendingTap.HasValue) return false;
            using (var time = new CancellationTokenSource(450))
            {
                if (pendingTap.HasValue && socket != null && socket.State == WebSocketState.Open)
                {
                    try { SetButton(pendingTap.Value, false, time.Token); }
                    catch (WebSocketException) { }
                    catch (OperationCanceledException) { }
                    catch (InvalidDataException) { }
                }
                foreach (BattleButton button in held.Keys.ToArray())
                {
                    if (socket == null || socket.State != WebSocketState.Open) break;
                    try { Release(button, time.Token); }
                    catch (WebSocketException) { break; }
                    catch (OperationCanceledException) { break; }
                    catch (InvalidDataException) { break; }
                }
            }
            held.Clear();
            pendingTap = null;
            mouseButtons = 0;
            return true;
        }

        private void Reset()
        {
            ReleaseAll();
            if (socket != null) socket.Dispose();
            socket = null;
            pageVerified = false;
        }

        public void Dispose()
        {
            Reset();
            reader.Dispose();
        }
    }
}
