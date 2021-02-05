using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;


using Device = SharpDX.Direct3D11.Device1;

using SharpDX.Direct3D11;
using SharpDX.Direct3D;
using DeviceContext = SharpDX.Direct2D1.DeviceContext;
using SharpDX.Direct2D1;
using SharpDX.WIC;
using D2DFactory = SharpDX.Direct2D1.Factory1;
using SharpDX.DXGI;

using System.Diagnostics;

using D2DBitmap = SharpDX.Direct2D1.Bitmap1;
using WICBitmap = SharpDX.WIC.Bitmap;
using SharpDX;
using PixelFormat = SharpDX.Direct2D1.PixelFormat;
using AlphaMode = SharpDX.Direct2D1.AlphaMode;
using System.Collections.Generic;

namespace ShinenginePlus
{
    /// <summary>
    /// 按照步骤 1a 或 1b 操作，然后执行步骤 2 以在 XAML 文件中使用此自定义控件。
    ///
    /// 步骤 1a) 在当前项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ShinenginePlus"
    ///
    ///
    /// 步骤 1b) 在其他项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ShinenginePlus;assembly=ShinenginePlus"
    ///
    /// 您还需要添加一个从 XAML 文件所在的项目到此项目的项目引用，
    /// 并重新生成以避免编译错误:
    ///
    ///     在解决方案资源管理器中右击目标项目，然后依次单击
    ///     “添加引用”->“项目”->[浏览查找并选择此项目]
    ///
    ///
    /// 步骤 2)
    /// 继续操作并在 XAML 文件中使用控件。
    ///
    ///     <MyNamespace:Direct2DImage/>
    ///
    /// </summary>  
    /// 
    /// 
    public enum DrawResultW
    {
        Commit,
        Death,
        Ignore
    }
    public enum DrawProcResult
    {
        Normal,
        Ignore,
        Death
    }

    public struct Direct2DInformation
    {
        public ImagingFactory ImagingFacy;
        public D2DFactory D2DFacy;
        public D2DBitmap _bufferBack;//用于D2D绘图的WIC图片
        public SharpDX.Direct3D11.Device d3DDevice;// = new SharpDX.Direct3D11.Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        public SharpDX.DXGI.Device dxgiDevice;// = d3DDevice.QueryInterface<Device>().QueryInterface<SharpDX.DXGI.Device>();

        public SharpDX.Direct2D1.Device d2DDevice;

        public DeviceContext View { get; set; }//绘图目标

    }
    public delegate void StartTask(Direct2DInformation view, WICBitmap last);
    public delegate void EndedTask();
    public delegate void EndingTask(ShinenginePlus.Direct2DImage self);
    public delegate DrawProcResult DrawProcTask(Direct2DInformation view);
    public class Direct2DImage : System.Windows.Controls.Image
    {
        static Direct2DImage()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Direct2DImage), new FrameworkPropertyMetadata(typeof(Direct2DImage)));
        }

        [DllImport("Kernel32.dll")]
        unsafe extern public static void RtlMoveMemory(void* dst, void* sur, long size);

        private int Times = 0;//每画一帧，值自增1，计算帧率时归零
        public int FrameRate { get; private set; } = 0;//表示当前的帧率

        public int TargetDpi;//目标帧率

        public WriteableBitmap buffer = null;//图片源


        public double Speed = 0;//画每帧后等待的时间
        private DispatcherTimer m_Dipter;//计算帧率的计时器
        private Thread m_Dipter2;//绘图线程


        public event EndedTask Disposed;
        public event EndingTask Disposing;

        public event DrawProcTask DrawProc;
        public event StartTask FirstDraw;
        public List<IDisposable> CleanTasks = new List<IDisposable>();

        private bool isRunning = false;//指示是否正在运行

        static public Direct2DInformation D2dInit(Size2 size)
        {
            Direct2DInformation result = new Direct2DInformation();

            result.d3DDevice = new SharpDX.Direct3D11.Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            var __dxgiDevice = result.d3DDevice.QueryInterface<Device>();
            result.dxgiDevice = __dxgiDevice.QueryInterface<SharpDX.DXGI.Device>();
            __dxgiDevice.Dispose();

            result.D2DFacy = new D2DFactory();
            result.d2DDevice = new SharpDX.Direct2D1.Device(result.D2DFacy, result.dxgiDevice);

            result.View = new DeviceContext(result.d2DDevice, DeviceContextOptions.EnableMultithreadedOptimizations);
            result.ImagingFacy = new ImagingFactory();
            result._bufferBack = new D2DBitmap(result.View, size,
                new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 72, 72, BitmapOptions.Target | BitmapOptions.CannotDraw));
            result.View.Target = result._bufferBack;

            return result;
        }

        static public void D2dRelease(Direct2DInformation info)
        {
            info._bufferBack?.Dispose();
            info.View?.Dispose();

            info.ImagingFacy?.Dispose();


            info.d2DDevice?.Dispose();
            info.D2DFacy?.Dispose();
            info.dxgiDevice?.Dispose();
            info.d3DDevice?.Dispose();
        }
        public Direct2DInformation m_d2d_info;
        public Direct2DImage()
        {
            
        }
        public void DrawStartup()
        {
            buffer = new WriteableBitmap((int)Width, (int)Height, 72, 72, System.Windows.Media.PixelFormats.Pbgra32, null);

            this.Source = buffer;
            TargetDpi = 60;

            Speed = 1.0d / TargetDpi;
            isRunning = true;



            m_d2d_info = D2dInit(new Size2((int)Width, (int)Height));

            if (FirstDraw != null)
            {
                try
                {
                    FirstDraw(m_d2d_info, null);
                    this.Dispatcher.Invoke(new Action(() => { Commit(); }));
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }

            }

            m_Dipter = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
            m_Dipter.Tick += (e, v) =>
            {

                FrameRate = Times;
                Debug.WriteLine("Speed:" + FrameRate.ToString());

                Times = 0;
            };
            m_Dipter2 = new Thread(() =>
            {//绘图代码

                while (isRunning)
                {


                    DrawProcResult? UpData = null;
                    Stopwatch sw = new Stopwatch();
                    sw.Start();

                    UpData = DrawProc?.Invoke(m_d2d_info);
                    if (!(UpData == DrawProcResult.Ignore || UpData == null)) this.Dispatcher.Invoke(new Action(() => { Commit(); }));

                    sw.Stop();

                    while (CleanTasks.Count > 0)
                    {
                        CleanTasks[0].Dispose();
                        CleanTasks.RemoveAt(0);
                    }
                    decimal time = sw.ElapsedTicks / (decimal)Stopwatch.Frequency * 1000;
                    decimal wait_time = 1000.0M / (decimal)TargetDpi - time;

                    if (wait_time < 0)
                    {
                        wait_time = 0;
                    }

                    if (UpData == DrawProcResult.Normal || UpData == null)
                    {
                        Thread.Sleep((int)wait_time);
                        Times++;
                        continue;
                    }
                    if (UpData == DrawProcResult.Ignore)
                    {
                        continue;
                    }
                    if (UpData == DrawProcResult.Death)
                    {
                        this.Dispose();
                        break;
                    }

                }

                Disposing?.Invoke(this);

                buffer = null;

                //////
                D2dRelease(m_d2d_info);
                Disposed?.Invoke();
            })
            { IsBackground = true };


            Stretch = System.Windows.Media.Stretch.Fill;
            m_Dipter.Start();
            m_Dipter2.Start();
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);

        }
        private void Dispose(bool isdisposing)
        {
            if (IsDisposed) return;
            IsDisposed = true;
            m_Dipter?.Stop();


            if (isdisposing)
            {

            }

            isRunning = false;
        }
        public bool IsDisposed { get; private set; } = false;
        public WICBitmap LastDraw
        {
            get
            {
                D2DBitmap m_local_buffer = new D2DBitmap(m_d2d_info.View, new Size2((int)ActualWidth, (int)ActualHeight),
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 72, 72, BitmapOptions.CannotDraw | BitmapOptions.CpuRead));

                m_local_buffer.CopyFromBitmap(m_d2d_info._bufferBack);
                var m_end_map = m_local_buffer.Map(MapOptions.Read);
                var m_end_bitmap_wic = new WICBitmap(m_d2d_info.ImagingFacy, (int)ActualWidth, (int)ActualHeight, SharpDX.WIC.PixelFormat.Format32bppPBGRA, new DataRectangle(m_end_map.DataPointer, m_end_map.Pitch));

                m_local_buffer.Unmap();
                m_local_buffer.Dispose();

                return m_end_bitmap_wic;
            }
        }
        ~Direct2DImage()
        {
            Dispose(false);
        }//ignore
        unsafe public void Commit()
        {
            D2DBitmap m_copied_buffer = new D2DBitmap(m_d2d_info.View, new Size2((int)ActualWidth, (int)ActualHeight), new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 72, 72, BitmapOptions.CannotDraw | BitmapOptions.CpuRead));
            m_copied_buffer.CopyFromBitmap(m_d2d_info._bufferBack);

            var m_copied_map = m_copied_buffer.Map(MapOptions.Read);

            buffer.Lock();

            for (int i = 0; i < m_copied_buffer.PixelSize.Height; i++)
            {
                int* source_base = (int*)(m_copied_map.DataPointer + i * m_copied_map.Pitch);
                int* target_base = (int*)(buffer.BackBuffer + i * buffer.BackBufferStride);

                RtlMoveMemory((void*)target_base, (void*)source_base, 4 * m_copied_buffer.PixelSize.Width);
            }

            buffer.AddDirtyRect(new Int32Rect(0, 0, (int)ActualWidth, (int)ActualHeight));
            buffer.Unlock();

            m_copied_buffer.Unmap();
            m_copied_buffer.Dispose();
        }//把后台数据呈现到前台
    }
}
