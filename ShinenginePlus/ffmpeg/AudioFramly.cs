
using FFmpeg.AutoGen;
using SharpDX;
using SharpDX.WIC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ShinenginePlus
{
    public struct AudioFrame
    {
        public IntPtr data;
        public double time_base;
    }
    unsafe class AudioFramly
    {
        private AVFormatContext* pFormatContext;
        private AVCodecContext* pcodecContext_A;
        private SwrContext* au_convert_ctx;
        readonly private AVStream* aStream = null;

        public int Out_nb_samples { get; private set; }
        public int Bit_per_sample { get; private set; }
        public int Out_sample_rate { get; private set; }
        public int Out_channels { get; private set; }
        public int Out_buffer_size { get; private set; }
        public int index { get; private set; }

        public AudioFramly(string path)
        {
            #region 转码共通
            // 分配音视频格式上下文
            pFormatContext = ffmpeg.avformat_alloc_context();

            var _pFormatContext = pFormatContext;
            //打开流
            ffmpeg.avformat_open_input(&_pFormatContext, path, null, null);
            // 读取媒体流信息
            ffmpeg.avformat_find_stream_info(pFormatContext, null);
            #endregion

            for (var i = 0; i < pFormatContext->nb_streams; i++)
            {
                if (pFormatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    aStream = pFormatContext->streams[i];
                }
            }

            if (aStream == null) throw new ApplicationException(@"Could not found audio stream.");
            var cedecparA = *aStream->codecpar;
            // 根据编码器ID获取对应的解码器
            var pCodec_A = ffmpeg.avcodec_find_decoder(cedecparA.codec_id);

            if (pCodec_A == null) throw new ApplicationException(@"Unsupported codec.");

            pcodecContext_A = ffmpeg.avcodec_alloc_context3(pCodec_A);

            ffmpeg.avcodec_parameters_to_context(pcodecContext_A, &cedecparA);

            ffmpeg.avcodec_open2(pcodecContext_A, pCodec_A, null);

            //////////////////////////////////////////////////
            ulong out_channel_layout = ffmpeg.AV_CH_LAYOUT_STEREO;
            //nb_samples: AAC-1024 MP3-1152
            Out_nb_samples = pcodecContext_A->frame_size;
            Bit_per_sample = 16;// pcodecContext_A->bits_per_coded_sample;
            AVSampleFormat out_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
            Out_sample_rate = pcodecContext_A->sample_rate;
            Out_channels = ffmpeg.av_get_channel_layout_nb_channels(out_channel_layout);
            //Out Buffer Size
            Out_buffer_size = ffmpeg.av_samples_get_buffer_size((int*)0, Out_channels, Out_nb_samples, out_sample_fmt, 1);


            //////////////////////////////////
            long in_channel_layout = ffmpeg.av_get_default_channel_layout(pcodecContext_A->channels);
            //Swr
            au_convert_ctx = ffmpeg.swr_alloc();
            au_convert_ctx = ffmpeg.swr_alloc_set_opts(au_convert_ctx, (long)out_channel_layout, out_sample_fmt, Out_sample_rate,
                in_channel_layout, pcodecContext_A->sample_fmt, pcodecContext_A->sample_rate, 0, (void*)0);
            ffmpeg.swr_init(au_convert_ctx);
            pPacket = ffmpeg.av_packet_alloc();

        }

        readonly private AVPacket* pPacket = null;
        public void Decode()
        {
            var _pFormatContext = pFormatContext;
            var _au_convert_ctx = au_convert_ctx;
            var _pcodecContext_A = pcodecContext_A;

            var frameNumber = 0;
            var pAudioFrame = ffmpeg.av_frame_alloc();

            byte* out_buffer = (byte*)Marshal.AllocHGlobal(19200 * 2);
            int got_picture = 0;

            while (true)
            {
                int error = ffmpeg.av_read_frame(pFormatContext, pPacket);
                if (error == ffmpeg.AVERROR_EOF) break;
                if (pPacket->stream_index == aStream->index)
                {
                    int ret = ffmpeg.avcodec_decode_audio4(pcodecContext_A, pAudioFrame, &got_picture, pPacket);
                    if (ret < 0)
                    {
                        return;
                    }

                    double timeset = ffmpeg.av_frame_get_best_effort_timestamp(pAudioFrame) * ffmpeg.av_q2d(aStream->time_base);
                    if (got_picture > 0)
                    {
                        ffmpeg.swr_convert(au_convert_ctx, &out_buffer, 19200, (byte**)&pAudioFrame->data, pAudioFrame->nb_samples);

                        var mbuf = Marshal.AllocHGlobal(Out_buffer_size);

                        RtlMoveMemory((void*)mbuf, out_buffer, Out_buffer_size);
                        abits.Add(new AudioFrame() { data = mbuf, time_base = timeset });

                        index++;

                    }
                    ffmpeg.av_packet_unref(pPacket);//释放数据包对象引用
                    ffmpeg.av_frame_unref(pAudioFrame);//释放解码帧对象引用
                    continue;
                }
            }

            Marshal.FreeHGlobal((IntPtr)out_buffer);
            ffmpeg.swr_close(au_convert_ctx);
            var __au_convert_ctx = au_convert_ctx;
            ffmpeg.swr_free(&__au_convert_ctx);
            var __pcodecContext_A = pcodecContext_A;
            ffmpeg.avcodec_free_context(&__pcodecContext_A);
        }
        [DllImport("Kernel32.dll")]
        unsafe extern public static void RtlMoveMemory(void* dst, void* sur, long size);
        public List<AudioFrame?> abits = new List<AudioFrame?>();
        [DllImport("Shinehelper.dll")]
        extern public static bool waveInit(IntPtr hWnd, int channels, int sample_rate, int bits_per_sample, int size);
        [DllImport("Shinehelper.dll")]
        unsafe extern public static void waveWrite(byte* in_buf, int in_buf_len);
        [DllImport("Shinehelper.dll")]
        extern public static void waveClose();

    }
}
