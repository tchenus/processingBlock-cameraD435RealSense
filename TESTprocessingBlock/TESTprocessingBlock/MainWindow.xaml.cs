using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Emgu.CV.Util;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Diagnostics;
using Intel.RealSense;
using System.Windows.Threading;

namespace TESTprocessingBlock
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        PipelineProfile pp;
        Pipeline pipeline;
        CustomProcessingBlock processingBlock;
        Colorizer colorizer;
        public MainWindow()
        {
            InitializeComponent();

            Action<VideoFrame> updateDepth;
            Action<VideoFrame> updateColor;

            // Create and config the pipeline to strem color and depth frames.
            pipeline = new Pipeline();

            var cfg = new Config();
            // Setup config settings
            cfg.EnableStream(Stream.Depth, 848, 480, Format.Z16, 90);
            cfg.EnableStream(Stream.Color, 848, 480, Format.Rgb8, 60);


            // Start streaming with Json configuration
            pp = pipeline.Start(cfg);

            SetupWindow(out updateDepth,out updateColor, pp);

            SetupFilters(out DecimationFilter decimate, out SpatialFilter spatial, out TemporalFilter temp, out HoleFillingFilter holeFill, out ThresholdFilter threshold);
            // Setup frame processing
            SetupProcessingBlock(pipeline, colorizer, decimate, spatial, temp, holeFill, threshold);

            // Start frame processing
            StartProcessingBlock(updateDepth, updateColor);
        }

        private void StartProcessingBlock(Action<VideoFrame> depth, Action<VideoFrame> color)
        {
            processingBlock.Start(f =>
            {
                using (var frames = FrameSet.FromFrame(f))
                {
                    VideoFrame colorFrame = frames.ColorFrame.DisposeWith(frames);
                    Intrinsics depthintr = (pp.GetStream(Stream.Depth).As<VideoStreamProfile>()).GetIntrinsics();
                    DepthFrame depthFrame = frames.DepthFrame.DisposeWith(frames);
                    VideoFrame colorizedDepth = colorizer.Process<VideoFrame>(depthFrame).DisposeWith(frames);

                    Dispatcher.Invoke(DispatcherPriority.Render, depth, colorizedDepth);
                    Dispatcher.Invoke(DispatcherPriority.Render, color, colorFrame);
                }
            });

            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    using (var frames = pipeline.WaitForFrames())
                    {
                        // Invoke custom processing block
                        processingBlock.Process(frames);
                    }
                }
            });

        }

        private void SetupWindow(out Action<VideoFrame> depth, out Action<VideoFrame> color, PipelineProfile pp)
        {
            //Display Depth
            using (VideoStreamProfile p = pp.GetStream(Intel.RealSense.Stream.Depth).As<VideoStreamProfile>())
                imgDepth.Source = new WriteableBitmap(p.Width, p.Height, 96d, 96d, PixelFormats.Rgb24, null);
            depth = UpdateImage(imgDepth);
            
            //Display Color
            using (VideoStreamProfile p = pp.GetStream(Intel.RealSense.Stream.Color).As<VideoStreamProfile>())
                imgColor.Source = new WriteableBitmap(p.Width, p.Height, 96d, 96d, PixelFormats.Rgb24, null);
            color = UpdateImage(imgColor);
        }

        static Action<VideoFrame> UpdateImage(System.Windows.Controls.Image img)
        {
            var wbmp = img.Source as WriteableBitmap;
            return new Action<VideoFrame>(frame =>
            {
                var rect = new Int32Rect(0, 0, frame.Width, frame.Height);
                wbmp.WritePixels(rect, frame.Data, frame.Stride * frame.Height, frame.Stride);
            });
        }

        private void SetupFilters(out DecimationFilter decimate, out SpatialFilter spatial, out TemporalFilter temp, out HoleFillingFilter holeFill, out ThresholdFilter threshold)
        {
            // Colorizer is used to visualize depth data
            colorizer = new Colorizer();
            // Decimation filter reduces the amount of data (while preserving best samples)
            decimate = new DecimationFilter();
            decimate.Options[Option.FilterMagnitude].Value = 1.0F;//change scall

            // Define spatial filter (edge-preserving)
            spatial = new SpatialFilter();
            // Enable hole-filling
            // Hole filling is an agressive heuristic and it gets the depth wrong many times
            // However, this demo is not built to handle holes
            // (the shortest-path will always prefer to "cut" through the holes since they have zero 3D distance)
            spatial.Options[Option.HolesFill].Value = 1.0F; //change resolution on the edge of image
            spatial.Options[Option.FilterMagnitude].Value = 5.0F;
            spatial.Options[Option.FilterSmoothAlpha].Value = 1.0F;
            spatial.Options[Option.FilterSmoothDelta].Value = 50.0F;

            // Define temporal filter
            temp = new TemporalFilter();

            // Define holefill filter
            holeFill = new HoleFillingFilter();

            // Aline color to depth

            //align_to = new Align(Stream.Depth);

            //try to define depth max
            threshold = new ThresholdFilter();
            threshold.Options[Option.MinDistance].Value = 0;
            threshold.Options[Option.MaxDistance].Value = 1;
        }

        private void SetupProcessingBlock(Pipeline pipeline, Colorizer colorizer, DecimationFilter decimate, SpatialFilter spatial, TemporalFilter temp, HoleFillingFilter holeFill, ThresholdFilter threshold)
        {
            // Setup / start frame processing
            processingBlock = new CustomProcessingBlock((f, src) =>
            {
                // We create a FrameReleaser object that would track
                // all newly allocated .NET frames, and ensure deterministic finalization
                // at the end of scope. 
                using (var releaser = new FramesReleaser())
                {
                    using (var frames = pipeline.WaitForFrames().DisposeWith(releaser))
                    {
                        var processedFrames = frames
                        .ApplyFilter(decimate).DisposeWith(releaser)
                        .ApplyFilter(spatial).DisposeWith(releaser)
                        .ApplyFilter(temp).DisposeWith(releaser)
                        .ApplyFilter(holeFill).DisposeWith(releaser)
                        .ApplyFilter(colorizer).DisposeWith(releaser)
                        .ApplyFilter(threshold).DisposeWith(releaser);

                        // Send it to the next processing stage
                        src.FrameReady(processedFrames);
                    }
                }
            });
        }


    }
}
