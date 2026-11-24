using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TideCombat
{
    // The WinRT capture pool supplies GPU textures. A staging texture makes each new frame CPU-readable.
    internal sealed class GraphicsCapture : IDisposable
    {
        private static readonly Guid ItemFactory = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
        private static readonly Guid ItemInterface = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
        private static readonly Guid DxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid SurfaceAccess = new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
        private static readonly Guid Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private const uint Bgra = 87;
        private const string RuntimeType = ", Windows, ContentType=WindowsRuntime";

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureDescription
        {
            internal uint Width, Height, Levels, Layers, Format, Samples, SampleQuality, Usage, Bind, CpuAccess, Misc;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MappedTexture
        {
            internal IntPtr Data;
            internal uint RowPitch, DepthPitch;
        }

        private delegate int CaptureWindow(IntPtr instance, IntPtr window, ref Guid iid, out IntPtr item);
        private delegate int GetInterface(IntPtr instance, ref Guid iid, out IntPtr result);
        private delegate int NewTexture(IntPtr instance, ref TextureDescription description, IntPtr initial, out IntPtr texture);
        private delegate void CopyTexture(IntPtr instance, IntPtr destination, IntPtr source);
        private delegate void DescribeTexture(IntPtr instance, out TextureDescription description);
        private delegate int MapTexture(IntPtr instance, IntPtr texture, uint part, uint access, uint flags, out MappedTexture mapped);
        private delegate void UnmapTexture(IntPtr instance, IntPtr texture, uint part);

        [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
        [DllImport("combase.dll")] private static extern void RoUninitialize();
        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string text, uint length, out IntPtr value);
        [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr value);
        [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(IntPtr adapter, uint driver, IntPtr software, uint flags,
            IntPtr levels, uint levelCount, uint sdk, out IntPtr device, out uint level, out IntPtr context);
        [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr winrt);

        private object framePool, session, item, projectedDevice, displaySize;
        private readonly FrameReader reader = new FrameReader();
        private Type poolType, sessionType, frameType;
        private IntPtr device, context, staging;
        private Size stagingSize, lastContentSize;
        private Rectangle lastClient;
        private bool initialized;

        internal GraphicsCapture(IntPtr window)
        {
            try
            {
                int apartment = RoInitialize(1);
                if (apartment != unchecked((int)0x80010106))
                {
                    Check(apartment);
                    initialized = true;
                }
                Initialize(window);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Initialize(IntPtr window)
        {
            poolType = Type.GetType("Windows.Graphics.Capture.Direct3D11CaptureFramePool" + RuntimeType, true);
            sessionType = Type.GetType("Windows.Graphics.Capture.GraphicsCaptureSession" + RuntimeType, true);
            frameType = Type.GetType("Windows.Graphics.Capture.Direct3D11CaptureFrame" + RuntimeType, true);
            if (!(bool)sessionType.GetMethod("IsSupported").Invoke(null, null))
                throw new PlatformNotSupportedException("Windows 图形捕获在此设备上不可用。");
            uint level;
            int result = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out level, out context);
            if (result < 0)
                Check(D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out level, out context));
            IntPtr dxgi = Query(device, DxgiDevice);
            try
            {
                IntPtr projected;
                Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out projected));
                projectedDevice = Wrap(projected);
            }
            finally { Release(ref dxgi); }
            item = CreateItem(window);
            Type itemType = Type.GetType("Windows.Graphics.Capture.GraphicsCaptureItem" + RuntimeType, true);
            displaySize = itemType.GetProperty("Size").GetValue(item, null);
            lastContentSize = ReadSize(displaySize);
            framePool = poolType.GetMethod("CreateFreeThreaded").Invoke(null, PoolArguments(displaySize));
            session = poolType.GetMethod("CreateCaptureSession").Invoke(framePool, new[] { item });
            MethodInfo cursor = sessionType.GetMethod("put_IsCursorCaptureEnabled");
            if (cursor != null) cursor.Invoke(session, new object[] { false });
            sessionType.GetMethod("StartCapture").Invoke(session, null);
        }

        private object[] PoolArguments(object size)
        {
            Type format = Type.GetType("Windows.Graphics.DirectX.DirectXPixelFormat" + RuntimeType, true);
            return new[] { projectedDevice, Enum.ToObject(format, Bgra), (object)2, size };
        }

        private static object CreateItem(IntPtr window)
        {
            const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
            IntPtr text, factory = IntPtr.Zero;
            Guid factoryId = ItemFactory, itemId = ItemInterface;
            Check(WindowsCreateString(name, (uint)name.Length, out text));
            try
            {
                Check(RoGetActivationFactory(text, ref factoryId, out factory));
                IntPtr result;
                Check(Method<CaptureWindow>(factory, 3)(factory, window, ref itemId, out result));
                return Wrap(result);
            }
            finally
            {
                Release(ref factory);
                WindowsDeleteString(text);
            }
        }

        internal bool TryRead(IntPtr window, Rectangle client, out PixelFrame result, bool retainEcho = false)
        {
            result = default(PixelFrame);
            MethodInfo next = poolType.GetMethod("TryGetNextFrame");
            object frame = next.Invoke(framePool, null);
            if (frame == null) return false;
            object surface = null;
            IntPtr surfaceObject = IntPtr.Zero, access = IntPtr.Zero, pixels = IntPtr.Zero;
            try
            {
                for (int discard = 0; discard < 2; discard++)
                {
                    object newer = next.Invoke(framePool, null);
                    if (newer == null) break;
                    Close(ref frame);
                    frame = newer;
                }
                object size = frameType.GetProperty("ContentSize").GetValue(frame, null);
                Size content = ReadSize(size);
                if (content != lastContentSize)
                {
                    if (content.Width <= 0 || content.Height <= 0) return false;
                    // Release the old buffers before asking the pool to resize.
                    Close(ref frame);
                    poolType.GetMethod("Recreate").Invoke(framePool, PoolArguments(size));
                    lastContentSize = content;
                    lastClient = Rectangle.Empty;
                    return false;
                }
                surface = frameType.GetProperty("Surface").GetValue(frame, null);
                surfaceObject = Marshal.GetIUnknownForObject(surface);
                access = Query(surfaceObject, SurfaceAccess);
                Guid iid = Texture2D;
                Check(Method<GetInterface>(access, 3)(access, ref iid, out pixels));
                TextureDescription description;
                Method<DescribeTexture>(pixels, 10)(pixels, out description);
                Size texture = new Size((int)description.Width, (int)description.Height);
                Rectangle crop = ClientCrop(window, client, content);
                if (crop.IsEmpty || !new Rectangle(Point.Empty, texture).Contains(crop)) return false;
                PrepareStaging(texture, description);
                Method<CopyTexture>(context, 47)(context, staging, pixels);
                MappedTexture mapped;
                Check(Method<MapTexture>(context, 14)(context, staging, 0, 1, 0, out mapped));
                try
                {
                    using (var bitmap = new Bitmap(texture.Width, texture.Height, (int)mapped.RowPitch,
                        PixelFormat.Format32bppRgb, mapped.Data))
                        result = reader.Read(bitmap, crop, retainEcho);
                }
                finally { Method<UnmapTexture>(context, 15)(context, staging, 0); }
                return true;
            }
            finally
            {
                Release(ref pixels);
                Release(ref access);
                Release(ref surfaceObject);
                Close(ref surface);
                Close(ref frame);
            }
        }

        private Rectangle ClientCrop(IntPtr window, Rectangle client, Size content)
        {
            if (Native.IsIconic(window))
                return lastClient;
            // WGC includes the window's title bar but excludes the invisible resize margin.
            Native.Rect visible;
            if (Native.DwmGetWindowAttribute(window, 9, out visible, Marshal.SizeOf(typeof(Native.Rect))) != 0)
                return Rectangle.Empty;
            lastClient = CropToClient(client, new Rectangle(visible.Left, visible.Top,
                visible.Right - visible.Left, visible.Bottom - visible.Top), content);
            return lastClient;
        }

        internal static Rectangle CropToClient(Rectangle client, Rectangle border, Size content)
        {
            if (content == client.Size) return new Rectangle(Point.Empty, content);
            var crop = new Rectangle(client.Left - border.Left, client.Top - border.Top, client.Width, client.Height);
            return new Rectangle(Point.Empty, content).Contains(crop) ? crop : Rectangle.Empty;
        }

        private void PrepareStaging(Size size, TextureDescription description)
        {
            if (staging != IntPtr.Zero && stagingSize == size) return;
            Release(ref staging);
            description.Usage = 3;
            description.CpuAccess = 0x20000;
            description.Bind = description.Misc = 0;
            Check(Method<NewTexture>(device, 5)(device, ref description, IntPtr.Zero, out staging));
            stagingSize = size;
        }

        private static Size ReadSize(object size)
        {
            Type type = size.GetType();
            return new Size((int)type.GetField("Width").GetValue(size), (int)type.GetField("Height").GetValue(size));
        }

        private static T Method<T>(IntPtr pointer, int index) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(pointer);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, index * IntPtr.Size), typeof(T)) as T;
        }

        private static void Check(int code) { if (code < 0) Marshal.ThrowExceptionForHR(code); }

        private static IntPtr Query(IntPtr obj, Guid iid)
        {
            IntPtr result;
            Check(Marshal.QueryInterface(obj, ref iid, out result));
            return result;
        }

        private static object Wrap(IntPtr pointer)
        {
            try { return Marshal.GetObjectForIUnknown(pointer); }
            finally { Release(ref pointer); }
        }

        private static void Close(ref object value)
        {
            if (value == null) return;
            var resource = value as IDisposable;
            if (resource != null) resource.Dispose();
            if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
            value = null;
        }

        private static void Release(ref IntPtr value)
        {
            if (value != IntPtr.Zero) Marshal.Release(value);
            value = IntPtr.Zero;
        }

        public void Dispose()
        {
            Close(ref session);
            Close(ref framePool);
            Close(ref item);
            Close(ref projectedDevice);
            reader.Dispose();
            Release(ref staging);
            Release(ref context);
            Release(ref device);
            if (initialized) RoUninitialize();
            initialized = false;
        }
    }
}
