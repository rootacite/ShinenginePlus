using System;

using ShinenginePlus;
using Device = SharpDX.Direct3D11.Device1;

using SharpDX.Direct3D11;
using SharpDX.Direct3D;
using DeviceContext = SharpDX.Direct2D1.DeviceContext;
using SharpDX.Direct2D1;
using SharpDX.WIC;
using D2DFactory = SharpDX.Direct2D1.Factory1;
using SharpDX.DXGI;

using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;

using D2DBitmap = SharpDX.Direct2D1.Bitmap1;
using Size = System.Drawing.Size;
using System.Threading.Tasks;
using SharpDX.Mathematics.Interop;
using System.Drawing;
using WICBitmap = SharpDX.WIC.Bitmap;

using SharpDX.Direct2D1.Effects;
using Blend = SharpDX.Direct2D1.Effects.Blend;
using SharpDX;
using System.Collections.Generic;
using System.Windows;
using Point = System.Drawing.Point;
using PixelFormat = SharpDX.Direct2D1.PixelFormat;
using AlphaMode = SharpDX.Direct2D1.AlphaMode;
using System.Windows.Media.Imaging;
using BitmapDecoder = SharpDX.WIC.BitmapDecoder;
using System.Windows.Threading;

using SolidColorBrush = SharpDX.Direct2D1.SolidColorBrush;
using System.Windows.Input;
using System.Numerics;
using Image = SharpDX.Direct2D1.Image;
using SharpDX.DirectWrite;
using TextAlignment = SharpDX.DirectWrite.TextAlignment;
using ShinenginePlus.Media;

using FFmpeg.AutoGen;

namespace ShinenginePlus.DrawableControls
{

    public abstract class ImageSource : IDisposable
    {
        public bool Updated { get; protected set; } = true;
        public Size PixelSize { get; protected set; }
        protected ImagingFactory iFactory = new ImagingFactory();
        public abstract WICBitmap Output { get; }

        public virtual void Dispose()
        {
            iFactory.Dispose();
        }
    }
    sealed public class VideoSource : ImageSource
    {
        VideoStreamDecoder Stream = null;
        WICBitmap PrePairedFrame = null;
        ManualResetEvent res_decode = new ManualResetEvent(false);
        public double FrameRate { get; private set; } = 0;
        unsafe public double Position
        {
            get
            {
                return clled_sealer*(1/FrameRate);
            }

        }


        long clled_sealer = 0;
        async public Task DecodeAsync()
        {
            await Task.Run(()=> 
            {
                var res = Stream.TryDecodeNextFrame(out IntPtr dataPoint, out int pitch);

                clled_sealer++;
                if (!res)
                {
                    Stream.Position(0);
                    return;
                }
                using (ImagingFactory Imf = new ImagingFactory())
                {
                    var old = PrePairedFrame;
                    PrePairedFrame = new WICBitmap(Imf, Stream.FrameSize.Width, Stream.FrameSize.Height, SharpDX.WIC.PixelFormat.Format32bppPBGRA, new DataRectangle(dataPoint, pitch));
                    old?.Dispose();
                }
                
                Updated = true;
            });

            
        }
        unsafe public VideoSource(string path)
        {
            res_decode.Set();
            Stream = new VideoStreamDecoder(path);
            PixelSize = new Size(Stream.FrameSize.Width, Stream.FrameSize.Height);

            FrameRate = (double)Stream.pStream->avg_frame_rate.num/ (double)Stream.pStream->avg_frame_rate.den;

        }
        public override WICBitmap Output
        {
            get
            {
                Updated = false;
                while (PrePairedFrame?.IsDisposed != false) 
                    Thread.Sleep(1);
                using (ImagingFactory imfc = new ImagingFactory())
                    return new WICBitmap(imfc, PrePairedFrame, BitmapCreateCacheOption.CacheOnLoad);
            }
        }

        public override void Dispose()
        {
            Stream?.Dispose();
            base.Dispose();
            if (PrePairedFrame?.IsDisposed == false) PrePairedFrame.Dispose();
        }
    }
    sealed public class DrawingImage : ImageSource
    {
        // 存在错误 绘图代码和Output可能同时被调用 并引发错误
        D2DFactory DxFac = new D2DFactory();
        WICBitmap buffer = null;
        WICBitmap load_buffer = null;
        WicRenderTarget View = null;
        public DrawingImage(Size size)
        {
            pw.Set();
            PixelSize = size;
            buffer =
               new WICBitmap(
               iFactory,
               (int)size.Width,
               (int)size.Height,
               SharpDX.WIC.PixelFormat.Format32bppPBGRA,
               BitmapCreateCacheOption.CacheOnLoad);

            View = new WicRenderTarget(DxFac, buffer, new RenderTargetProperties(
                RenderTargetType.Default,
                new PixelFormat(Format.Unknown, AlphaMode.Unknown),
                0,
                0,
                RenderTargetUsage.None,
                SharpDX.Direct2D1.FeatureLevel.Level_DEFAULT));

            load_buffer = new WICBitmap(iFactory, buffer, BitmapCreateCacheOption.CacheOnLoad);
        }
        ManualResetEvent pw = new ManualResetEvent(false);
        public void Update(DrawProc Proc)
        {
            pw.WaitOne();
            pw.Reset();
            Proc?.Invoke(View);
            var old_proc = load_buffer;
            load_buffer = new WICBitmap(iFactory, buffer, BitmapCreateCacheOption.CacheOnLoad);
            old_proc?.Dispose();
            Updated = true;
            pw.Set();
        }
        public delegate void DrawProc(WicRenderTarget view);
        public override WICBitmap Output
        {
            get
            {
                pw.WaitOne();
                Updated = false;
                if (load_buffer?.IsDisposed == false)
                    return new WICBitmap(iFactory, load_buffer, BitmapCreateCacheOption.CacheOnLoad);
                else return null;
            }
        }

        public override void Dispose()
        {
            View.Dispose();
            buffer.Dispose();
            DxFac.Dispose();
            base.Dispose();
        }
    }

    sealed public class BitmapImage : ImageSource
    {
        WICBitmap buffer = null;
        public BitmapImage(string path)
        {
            buffer = Direct2DHelper.LoadBitmap(path);
            PixelSize = new Size(buffer.Size.Width, buffer.Size.Height);
        }
        public void ReLoad(string path)
        {
            var old = buffer;
            buffer = Direct2DHelper.LoadBitmap(path);
            PixelSize = new Size(buffer.Size.Width, buffer.Size.Height);
            old.Dispose();
            Updated = true;
        }
        public override WICBitmap Output
        {
            get
            {
                Updated = false;
                return new WICBitmap(iFactory, buffer, BitmapCreateCacheOption.CacheOnLoad);
            }
        }

        public override void Dispose()
        {
            buffer?.Dispose();
            base.Dispose();
        }
    }

    sealed public class GDIBitmap : ImageSource
    {
        IntPtr buffer = (IntPtr)0;
        System.Drawing.Bitmap buffer_convert = null;
        Graphics graphics = null;
        public Graphics Graphics
        {
            get
            {
                Updated = true;
                return graphics;
            }
        }

        public GDIBitmap(Size size)
        {
            buffer = Marshal.AllocHGlobal(size.Width * 4 * size.Height);
            buffer_convert = new System.Drawing.Bitmap((int)size.Width, (int)size.Height, size.Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, buffer);

            graphics = Graphics.FromImage(buffer_convert);
            graphics.Clear(Color.Empty);
            graphics.Flush();

            PixelSize = new Size(size.Width, size.Height);
        }

        public override WICBitmap Output
        {
            get
            {
                Updated = false;
                return new WICBitmap(iFactory, buffer_convert.Width, buffer_convert.Height, SharpDX.WIC.PixelFormat.Format32bppPBGRA, new DataRectangle(buffer, buffer_convert.Width * 4));
            }
        }

        public override void Dispose()
        {
            Marshal.FreeHGlobal(buffer);
            base.Dispose();
        }
    }

 

    sealed public class ProcessBar : RenderableObject, IDisposable
    {
        public double Process { get; set; } = 0.0d;
        public Point Position { get; set; }
        public int PixelLong { get; set; } = 1280;

        BitmapBrush br = null;
        public ProcessBar(DeviceContext DC) : base(DC)
        {
            using (WICBitmap sc = Direct2DHelper.LoadBitmap("assets\\process.bmp"))
            using (D2DBitmap sc_cv = D2DBitmap.FromWicBitmap(HostDC, sc))
            {
                br = new BitmapBrush(HostDC, sc_cv);
            }
            br.ExtendModeX = ExtendMode.Mirror;
        }
        public override void Render()
        {

            HostDC.DrawLine(new RawVector2(Position.X, Position.Y - 1.5f), new RawVector2(Position.X + PixelLong, Position.Y - 1.5f), br, 3);
            using (SolidColorBrush b = new SolidColorBrush(HostDC, new RawColor4(0.85f, 0.85f, 0.85f, 1)))
                HostDC.FillEllipse(new Ellipse(new RawVector2((float)(Position.X + PixelLong * Process), Position.Y - 2f), 4, 4), b);
        }

        public void Dispose()
        {
            br.Dispose();
        }
    }

    sealed public class DrawableText : RenderableObject, IDisposable
    {
        /// <summary>
        /// word/s
        /// </summary>
        public int Speed
        {
            get;
            set;
        } = 15;//Word / S

        public RawRectangleF Range
        {
            get;
            set;
        } = new RawRectangleF(0, 0, 1280, 720);
        int drawtime = 0;
        public string text = "";
        SharpDX.DirectWrite.Factory wfactory = null;
        SharpDX.DirectWrite.TextFormat wformat = null;

        public ParagraphAlignment ParagraphAlignment
        {
            get
            {
                return wformat.ParagraphAlignment;
            }
            set
            {
                wformat.ParagraphAlignment = value;
            }
        }

        public TextAlignment TextAlignment
        {
            get
            {
                return wformat.TextAlignment;
            }
            set
            {
                wformat.TextAlignment = value;
            }
        }

        public ReadingDirection ReadingDirection
        {
            get
            {
                return wformat.ReadingDirection;
            }
            set
            {
                wformat.ReadingDirection = value;
            }
        }

        public DrawableText(string t, string name, float size, DeviceContext DC) : base(DC)
        {
            text += t;
            wfactory = new SharpDX.DirectWrite.Factory();
            wformat = new SharpDX.DirectWrite.TextFormat(wfactory, name, size);

            this.AddUpdateProcess(() =>
            {
                drawtime += 1;
                if (drawtime > 60)
                    drawtime = 0;

                int fpt = 60 / Speed;

                if (bufferChars.Count != 0 && drawtime % fpt == 0)
                {
                    text += bufferChars[0];
                    bufferChars.RemoveAt(0);
                }

                return true;
            });
        }

        public void Dispose()
        {
            wfactory.Dispose();
            wformat.Dispose();
        }
        public RawColor4 Color
        {
            set;
            get;
        } = new RawColor4(1, 1, 1, 1);
        public override void Render()
        {
            using (SolidColorBrush brush = new SharpDX.Direct2D1.SolidColorBrush(HostDC, Color))
                HostDC.DrawText(this.text, wformat, Range, brush);
        }


        List<char> bufferChars = new List<char>();
        public void PushString(string Text)
        {
            bufferChars.AddRange(Text.ToCharArray());
        }
    }
    public class InteractiveObject : RenderableImage
    {
        UIElement _mainWindow = null;
        /*
        private List<Vector> Forces = new List<Vector>();
        private Vector Acceleration
        {
            get
            {
                return new Vector();
            }
        }
        private Vector Velocity = new Vector(0, 0);
        */
        public event MouseButtonEventHandler MouseUp;
        public event MouseButtonEventHandler MouseDown;
        public event MouseEventHandler MouseMove;
        public event MouseEventHandler MouseEnter;
        public event MouseEventHandler MouseLeft;
        //************************
        //  static private List<InteractiveObject> listOfExist = new List<InteractiveObject>();
        /*   public Rectangle CollisionRange
           {
               get
               {
                   var result = new Rectangle();

                   result.X = Position.X;
                   result.Y = Position.Y;
                   result.Width = Size.Width;
                   result.Height = Size.Height;

                   return result;
               }
           }*/
        private void ButtonDownListener(object sender, MouseButtonEventArgs e)
        {
            var p = m_group.CursorPos;
            if (p.X > this.Position.X && p.X < this.Position.X + this.Size.Width && p.Y > this.Position.Y && p.Y < this.Position.Y + this.Size.Height)
            {
                MouseDown?.Invoke(this, e);
            }
        }
        private void ButtonUpListener(object sender, MouseButtonEventArgs e)
        {
            var p = m_group.CursorPos;
            if (p.X > this.Position.X && p.X < this.Position.X + this.Size.Width && p.Y > this.Position.Y && p.Y < this.Position.Y + this.Size.Height)
            {
                MouseUp?.Invoke(this, e);
            }
        }
        public bool isInArea { get; private set; } = false;
        private void MoveListener(object sender, MouseEventArgs e)
        {
            var p = m_group.CursorPos;
            if (p.X > this.Position.X && p.X < this.Position.X + this.Size.Width && p.Y > this.Position.Y && p.Y < this.Position.Y + this.Size.Height)
            {
                if (!isInArea) MouseEnter?.Invoke(this, e);
                isInArea = true;
            }
            else
            {
                if (isInArea) MouseLeft?.Invoke(this, e);
                isInArea = false;
            }
            MouseMove?.Invoke(this, e);
        }
        public InteractiveObject(ImageSource wic, DeviceContext DC) : base(wic, DC)
        {
        }
        //  public delegate bool CollisionEvent(InteractiveObject obj, double ort);
        // public bool HasCollision { get; set; } = true;
        // public event CollisionEvent Collision;

        /*   public void Move(Vector vt)
           {
               var new_pos = new Point((int)(Position.X + vt.X), (int)(Position.Y + vt.Y));
               double vtr;
               if (Math.Abs( vt.Y) > Math.Abs( vt.X))
                   vtr = Math.Asin(-vt.Y / Math.Sqrt(vt.X * vt.X + vt.Y * vt.Y));
               else
                   vtr = Math.Acos(vt.X / Math.Sqrt(vt.X * vt.X + vt.Y * vt.Y));
               if (vtr < 0)
                   vtr += 2d * Math.PI;
               if (vtr < Math.PI)
                   vtr += Math.PI;
               else
                   vtr -= Math.PI;

               var new_rect = new Rectangle()
               {
                   X = new_pos.X,
                   Y = new_pos.Y,
                   Width = Size.Width,
                   Height = Size.Height
               };

               foreach (var i in listOfExist)
               {
                   if (i == this)
                       continue;
                   if (new_rect.IntersectsWith(i.CollisionRange) && HasCollision)
                   {
                       if (Collision?.Invoke(i, vtr) == true)
                           return;
                   }
               }

               Position = new_pos;
           }*/

        new public double Orientation { get; }

        //   rale_point = Layer.Position;
        Layer m_group = null;
        public void PushTo(Layer RenderGroup, UIElement mainWindow)
        {
            m_group = RenderGroup;
            _mainWindow = mainWindow;
            _mainWindow.MouseLeftButtonUp += ButtonUpListener;
            _mainWindow.MouseMove += MoveListener;
            _mainWindow.MouseLeftButtonDown += ButtonDownListener;
            base.PushTo(RenderGroup);
        }

        new public void PopFrom()
        {
            _mainWindow.MouseLeftButtonUp -= ButtonUpListener;
            _mainWindow.MouseLeftButtonDown -= ButtonDownListener;
            _mainWindow.MouseMove -= MoveListener;
            base.PopFrom();
        }
    }


    public class RenderableImage : RenderableObject, IDisposable
    {
        bool size_changed = false;
        public float Saturation { get; set; } = 1f;
        public float Brightness { get; set; } = 0.5f;
        public double Orientation { get; set; } = 0;
        private Size2 _Size;
        public Size2 Size
        {
            get
            {
                return new Size2(_Size.Width, _Size.Height);
            }
            set
            {
                size_changed = true;
                _Size = new Size2(value.Width, value.Height);
            }
        }
        public Point RotationPoint { get; set; } = new Point(0, 0);
        private Point _Position = new Point(0, 0);
        public Point Position
        {
            get
            {
                return new Point(_Position.X, _Position.Y);
            }
            set
            {
                _Position = new Point(value.X, value.Y);
            }
        }

        public float Opacity { get; set; } = 1.0f;
        public ImageSource Source = null;
        protected SharpDX.Direct2D1.Image Output(DeviceContext rDc)
        {
            D2DBitmap ntdx = null;
            try
            {

                ntdx = D2DBitmap.FromWicBitmap(rDc, _Pelete);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());

            }
            Image result1;

            var blEf = new SharpDX.Direct2D1.Effect(rDc, Effect.Opacity);

            blEf.SetInput(0, ntdx, new RawBool());
            blEf.SetValue(0, Opacity);

            result1 = blEf.Output;
            blEf.Dispose();
            if (size_changed)
            {
                var tfEf = new SharpDX.Direct2D1.Effects.AffineTransform2D(rDc);
                tfEf.SetInput(0, result1, new RawBool());

                result1.Dispose();
                var x_rate = _Size.Width / (double)_Pelete.Size.Width;
                var y_rate = _Size.Height / (double)_Pelete.Size.Height;
                tfEf.TransformMatrix = new RawMatrix3x2((float)x_rate, 0f, 0f, (float)y_rate, 0f, 0f);
                result1 = tfEf.Output;
                tfEf.Dispose();
            }
            if (Orientation != 1.0f)
            {
                var tfEf1 = new SharpDX.Direct2D1.Effects.AffineTransform2D(rDc);
                tfEf1.SetInput(0, result1, new RawBool());

                result1.Dispose();
                var mr32 = Matrix3x2.CreateRotation((float)Orientation, new Vector2(RotationPoint.X, RotationPoint.Y));
                tfEf1.TransformMatrix = new RawMatrix3x2(mr32.M11, mr32.M12, mr32.M21, mr32.M22, mr32.M31, mr32.M32);
                result1 = tfEf1.Output;
                tfEf1.Dispose();
            }
            if (this.Saturation != 1f)
            {
                var stEf = new SharpDX.Direct2D1.Effects.Saturation(rDc);
                stEf.SetInput(0, result1, new RawBool());

                result1.Dispose();
                stEf.Value = Saturation;
                result1 = stEf.Output;
                stEf.Dispose();
            }
            if (this.Brightness != 0.5f)
            {
                var btEf = new SharpDX.Direct2D1.Effects.Brightness(rDc);
                btEf.SetInput(0, result1, new RawBool());

                result1.Dispose();
                btEf.BlackPoint = new RawVector2(1.0f - Brightness, Brightness);
                //  btEf.WhitePoint =;
                result1 = btEf.Output;
                btEf.Dispose();
            }
            ntdx.Dispose();
            return result1;

        }
        protected WICBitmap _Pelete = null;

        private bool disposedValue;
        public bool IsDisposed
        {
            get
            {
                return disposedValue;
            }
        }
        public RenderableImage(ImageSource im, DeviceContext DC) : base(DC)
        {

            Source = im;
            _Pelete = Source.Output;
            this._Size = new Size2(this._Pelete.Size.Width, this._Pelete.Size.Height);

            RotationPoint = new Point(this._Pelete.Size.Width / 2, this._Pelete.Size.Height / 2);
        }

        public override void Render()
        {
            if (Source.Updated)
            {
                _Pelete?.Dispose();
                _Pelete = Source.Output;
            }
            if (_Pelete?.IsDisposed != false)
                return;
            using (Image PrepairedImage = Output(HostDC))
                HostDC.DrawImage(PrepairedImage, new RawVector2(_Position.X, _Position.Y), null, SharpDX.Direct2D1.InterpolationMode.Linear, CompositeMode.SourceOver);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {

                disposedValue = true;
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                }

                // TODO: 释放未托管的资源(未托管的对象)并替代终结器
                // TODO: 将大型字段设置为 null
                this._Pelete.Dispose();
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~RenderableImage()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    sealed public class ClosedGemo : RenderableObject
    {
        public RawColor4 Border { get; set; }
        public RawColor4 Filler { get; set; }

        public int Thickness { get; set; } = 1;
        private readonly Point[] Path;
        public ClosedGemo(Point[] Path, RawColor4 cBorder, RawColor4 cFiller, DeviceContext DC) : base(DC)
        {
            this.Path = (Point[])Path.Clone();

            Border = new RawColor4(cBorder.R, cBorder.G, cBorder.B, cBorder.A);
            Filler = new RawColor4(cFiller.R, cFiller.G, cFiller.B, cFiller.A);
        }
        public override void Render()
        {
            using (SolidColorBrush BorderBrush = new SolidColorBrush(HostDC, Border), FillerBrush = new SolidColorBrush(HostDC, Filler))
            {
                for (int i = 0; i < Path.Length - 1; i++)
                {

                    var area = new Mesh(HostDC, new Triangle[] { new Triangle() { Point1 = new RawVector2(Path[0].X, Path[0].Y), Point2 = new RawVector2(Path[i].X, Path[i].Y), Point3 = new RawVector2(Path[i + 1].X, Path[i + 1].Y) } });

                    HostDC.FillMesh(area, FillerBrush);
                    HostDC.DrawLine(new RawVector2(Path[i].X, Path[i].Y), new RawVector2(Path[i + 1].X, Path[i + 1].Y), BorderBrush, Thickness);
                    area.Dispose();
                }
                HostDC.DrawLine(new RawVector2(Path[Path.Length - 1].X, Path[Path.Length - 1].Y), new RawVector2(Path[0].X, Path[0].Y), BorderBrush, Thickness);

            }
        }
    }
    sealed public class BackGroundLayer : Layer
    {
        private UIElement _window = null;
        private System.Windows.Point real_cursorpos = new System.Windows.Point(0, 0);
        private void MoveListener(object s, MouseEventArgs e)
        {
            real_cursorpos = e.GetPosition(_window);
        }
        public new RawRectangleF OutPutRange { get { return base.OutPutRange; } }
        bool dpd = false;
        public override Point CursorPos
        {
            get
            {
                if (dpd || !IsEnableMouse) return new Point(-1, -1);
                double x_rate = real_cursorpos.X / _window.RenderSize.Width;
                double y_rate = real_cursorpos.Y / _window.RenderSize.Height;

                double x_offset = x_rate * (Range.Right - Range.Left);
                double y_offset = y_rate * (Range.Bottom - Range.Top);

                return new Point((int)(Range.Left + x_offset), (int)(Range.Top + y_offset));
            }
        }

        public BackGroundLayer(Size size, UIElement window, RawRectangleF Output, DeviceContext DC) : base(DC)
        {
            this.Size = new Size2(size.Width, size.Height);
            base.OutPutRange = Output;
            window.MouseMove += MoveListener;
            _window = window;
        }
        public void Pop()
        {
            _window.MouseMove -= MoveListener;
            dpd = true;
        }
        ~BackGroundLayer()
        {
            _window.MouseMove -= MoveListener;
        }
    }
    sealed public class GroupLayer : Layer
    {
        public void Top()
        {
            if (IsShowed)
            {
                _father.RenderGroup.Remove(this);
                _father.RenderGroup.Add(this);
            }
        }
        public override Point CursorPos
        {
            get
            {

                double mouse_x_rate = (double)_father.CursorPos.X / (double)_father.Size.Width;
                double mouse_y_rate = (double)_father.CursorPos.Y / (double)_father.Size.Height;

                double m_left_rate = (double)OutPutRange.Left / (double)_father.Size.Width;
                double m_top_rate = (double)OutPutRange.Top / (double)_father.Size.Height;

                double m_right_rate = (double)OutPutRange.Right / (double)_father.Size.Width;
                double m_bottom_rate = (double)OutPutRange.Bottom / (double)_father.Size.Height;

                if (mouse_x_rate > m_right_rate || m_right_rate < m_left_rate || mouse_y_rate > m_bottom_rate || mouse_y_rate < m_top_rate) return new Point(-1, -1);

                double currect_x_rate = (mouse_x_rate - m_left_rate) / (m_right_rate - m_left_rate);
                double currect_y_rate = (mouse_y_rate - m_top_rate) / (m_bottom_rate - m_top_rate);

                int offset_x = (int)(currect_x_rate * (double)(Range.Right - Range.Left));
                int offset_y = (int)(currect_y_rate * (double)(Range.Bottom - Range.Top));




                return new Point((int)(Range.Left + offset_x), (int)(Range.Top + offset_y));

            }
        }
        private Layer _father = null;
        /// <summary>
        /// size
        /// </summary>
        public GroupLayer(Layer bk, Size2 Size, DeviceContext DC) : base(DC)
        {
            this.Size = Size;
            _father = bk;
        }
        public void Pop()
        {
            this.PopFrom(_father);
        }

        public void PushTo(Layer RenderGroup)
        {
            if (IsShowed) throw new Exception("This object is already showed");
            RenderGroup.RenderGroup.Add(this);

            IsShowed = true;
            Showed?.Invoke(this, null);
        }

        private void PopFrom(Layer RenderGroup)
        {
            if (!IsShowed) throw new Exception("This object is not showed yet");
            if (Removing != null)
            {
                new Thread(() =>
                {
                    Removing.Invoke(this, null);

                    RenderGroup.RenderGroup.Remove(this);
                    IsShowed = false;

                    Removed?.Invoke(this, null);
                })
                { IsBackground = true }.Start();
                return;
            }

            RenderGroup.RenderGroup.Remove(this);
            IsShowed = false;
            Removed?.Invoke(this, null);
            _father = null;
        }

        public bool IsShowed
        {
            get;
            set;
        } = false;


        public event EventHandler Showed;
        public event EventHandler Removed;
        public event EventHandler Removing;
    }

    public abstract class Layer : IDrawable
    {
        public bool Freezing { get; set; } = false;


        public bool IsEnableMouse { get; set; } = true;
        public delegate Effect EffectProc(Image Input, DeviceContext dc);
        private EffectProc Effecting = null;
        public void SetEffect(EffectProc Proc)
        {
            Effecting = Proc;
        }
        public DeviceContext HostDC { get; private set; }
        protected Layer(DeviceContext DC)
        {
            HostDC = DC;
        }
        public RawRectangleF Range { get; set; } = new RawRectangleF(0, 0, 1280, 720);
        public RawRectangleF OutPutRange { get; set; } = new RawRectangleF(0, 0, 1280, 720);
        public abstract Point CursorPos { get; }
        public List<IDrawable> RenderGroup = new List<IDrawable>();
        public void Render()
        {
            ////////////////////////////////////
            HostDC.EndDraw();

            using (D2DBitmap loadBp = new D2DBitmap(HostDC, Size,
                 new BitmapProperties1(HostDC.PixelFormat, HostDC.DotsPerInch.Width, HostDC.DotsPerInch.Height, BitmapOptions.Target | BitmapOptions.CannotDraw)), loadBp2 = new D2DBitmap(HostDC, Size,
              new BitmapProperties1(HostDC.PixelFormat, HostDC.DotsPerInch.Width, HostDC.DotsPerInch.Height, BitmapOptions.None)))
            {
                var old_target = HostDC.Target;


                HostDC.Target = loadBp;

                HostDC.BeginDraw();
                HostDC.Clear(Color);
                var RCP = RenderGroup.ToArray();
                foreach (var i in RCP)
                {
                    i?.Render();
                }

                HostDC.EndDraw();

                if (Effecting != null)
                {
                    loadBp2.CopyFromBitmap(loadBp);
                    var resultEff = Effecting(loadBp2, HostDC);
                    var resultIm = resultEff.Output;
                    if (resultEff == null)
                    {
                        Effecting = null;
                        goto tag;
                    }
                    HostDC.BeginDraw();
                    HostDC.Clear(Color);
                    HostDC.DrawImage(resultIm, new RawVector2(0, 0), null, SharpDX.Direct2D1.InterpolationMode.Linear, CompositeMode.SourceOver);
                    HostDC.EndDraw();
                    resultEff.Dispose();
                    resultIm.Dispose();
                }
                tag:
                loadBp2.CopyFromBitmap(loadBp);
                HostDC.Target = old_target;
                HostDC.BeginDraw();

                if (old_target as D2DBitmap == null)
                    old_target.Dispose();

                if (Effecting != null)
                {


                }

                HostDC.DrawBitmap(loadBp2, OutPutRange, 1f, SharpDX.Direct2D1.InterpolationMode.Linear, Range, null);


            }
        }
        private List<UpdateProcess> Updating = new List<UpdateProcess>();
        public void AddUpdateProcess(UpdateProcess Proc)
        {
            Updating.Add(Proc);
        }
        public delegate bool UpdateProcess();
        public void Update()
        {
            if (Freezing) return;
            var RCP = RenderGroup.ToArray();

            foreach (var i in RCP)
            {
                i?.Update();
            }
            List<UpdateProcess> RemoveableUpdating = new List<UpdateProcess>();
            var RUM = Updating.ToArray();
            foreach (var i in RUM)
            {
                var result = i();
                if (!result) RemoveableUpdating.Add(i);
            }
            foreach (var i in RemoveableUpdating)
            {
                Updating.Remove(i);
            }
        }

        public Size2 Size
        {
            get; set;
        }
        public RawColor4 Color
        {
            set;
            get;
        } = new RawColor4(1, 1, 1, 0);
    }
    public abstract class RenderableObject : IDrawable
    {
        protected RenderableObject(DeviceContext DC)
        {
            HostDC = DC;
        }
        public DeviceContext HostDC { get; private set; }
        public void PushTo(Layer RenderGroup)
        {
            if (IsShowed) throw new Exception("This object is already showed");
            RenderGroup.RenderGroup.Add(this);
            IsShowed = true;
            Showed?.Invoke(this, null);

            m_rg = RenderGroup;
        }
        protected Layer m_rg = null;
        public void PopFrom()
        {
            if (!IsShowed) throw new Exception("This object is not showed yet");
            if (Removing != null)
            {
                new Thread(() =>
                {
                    Removing.Invoke(this, null);

                    m_rg.RenderGroup.Remove(this);
                    IsShowed = false;

                    Removed?.Invoke(this, null);
                })
                { IsBackground = true }.Start();
                return;
            }

            m_rg.RenderGroup.Remove(this);
            IsShowed = false;
            Removed?.Invoke(this, null);
        }

        public void Top()
        {
            if (IsShowed)
            {
                m_rg.RenderGroup.Remove(this);
                m_rg.RenderGroup.Add(this);
            }
        }

        public bool IsShowed
        {
            get;
            set;
        } = false;

        public event EventHandler Showed;
        public event EventHandler Removed;
        public event EventHandler Removing;
        // public event EventHandler Updating;
        private List<UpdateProcess> Updating = new List<UpdateProcess>();
        public delegate bool UpdateProcess();

        public abstract void Render();
        public void Update()
        {
            List<UpdateProcess> RemoveableUpdating = new List<UpdateProcess>();
            var RUM = Updating.ToArray();
            foreach (var i in RUM)
            {
                var result = i();
                if (!result) RemoveableUpdating.Add(i);
            }
            foreach (var i in RemoveableUpdating)
            {
                Updating.Remove(i);
            }
        }

        public void AddUpdateProcess(UpdateProcess Proc)
        {
            Updating.Add(Proc);
        }
    }


    public interface IDrawable
    {
        void Render();
        void Update();
    }
}