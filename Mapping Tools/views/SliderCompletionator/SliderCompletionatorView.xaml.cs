﻿using Mapping_Tools.Classes.BeatmapHelper;
using Mapping_Tools.Classes.SystemTools;
using Mapping_Tools.Classes.SystemTools.QuickRun;
using Mapping_Tools.Classes.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Mapping_Tools.Classes.SliderPathStuff;

namespace Mapping_Tools.Views {
    /// <summary>
    /// Interaktionslogik für UserControl1.xaml
    /// </summary>
    [SmartQuickRunUsage(SmartQuickRunTargets.AnySelection)]
    public partial class SliderCompletionatorView : IQuickRun {
        public event EventHandler RunFinished;

        public static readonly string ToolName = "Slider Completionator";

        public static readonly string ToolDescription = $@"Change the length and duration of marked sliders and this tool will automatically handle the SliderVelocity for you.";

        /// <inheritdoc />
        public SliderCompletionatorView() {
            InitializeComponent();
            Width = MainWindow.AppWindow.content_views.Width;
            Height = MainWindow.AppWindow.content_views.Height;
        }

        protected override void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e) {
            var bgw = sender as BackgroundWorker;
            e.Result = Complete_Sliders((Arguments) e.Argument, bgw, e);
        }

       
        private void Start_Click(object sender, RoutedEventArgs e) {
            RunTool(MainWindow.AppWindow.GetCurrentMaps(), quick: false);
        }

        public void QuickRun() {
            RunTool(new[] { IOHelper.GetCurrentBeatmap() }, quick: true);
        }

        private void RunTool(string[] paths, bool quick = false) {
            if (!CanRun) return;

            IOHelper.SaveMapBackup(paths);

            BackgroundWorker.RunWorkerAsync(new Arguments(paths, TemporalBox.GetDouble(), SpatialBox.GetDouble(), ScaleAnchorsBox.IsChecked.GetValueOrDefault(), SelectionModeBox.SelectedIndex, quick));
            CanRun = false;
        }

        private struct Arguments {
            public string[] Paths;
            public double TemporalLength;
            public double SpatialLength;
            public bool ScaleAnchors;
            public int SelectionMode;
            public bool Quick;
            public Arguments(string[] paths, double temporal, double spatial, bool scaleAnchors, int selectionMode, bool quick)
            {
                Paths = paths;
                TemporalLength = temporal;
                SpatialLength = spatial;
                ScaleAnchors = scaleAnchors;
                SelectionMode = selectionMode;
                Quick = quick;
            }
        }

        private string Complete_Sliders(Arguments arg, BackgroundWorker worker, DoWorkEventArgs _) {
            int slidersCompleted = 0;

            bool editorRead = EditorReaderStuff.TryGetFullEditorReader(out var reader);

            foreach (string path in arg.Paths) {
                var selected = new List<HitObject>();
                BeatmapEditor editor = editorRead ? EditorReaderStuff.GetNewestVersion(path, out selected, reader) : new BeatmapEditor(path);
                Beatmap beatmap = editor.Beatmap;
                Timing timing = beatmap.BeatmapTiming;
                List<HitObject> markedObjects = arg.SelectionMode == 0 ? selected :
                                                arg.SelectionMode == 1 ? beatmap.GetBookmarkedObjects() :
                                                                         beatmap.HitObjects;

                for (int i = 0; i < markedObjects.Count; i++) {
                    HitObject ho = markedObjects[i];
                    if (ho.IsSlider) {
                        double oldSpatialLength = ho.PixelLength;
                        double newSpatialLength = arg.SpatialLength != -1 ? ho.GetSliderPath(fullLength: true).Distance * arg.SpatialLength : oldSpatialLength;

                        double oldTemporalLength = timing.CalculateSliderTemporalLength(ho.Time, ho.PixelLength);
                        double newTemporalLength = arg.TemporalLength != -1 ? timing.GetMpBAtTime(ho.Time) * arg.TemporalLength : oldTemporalLength;

                        double oldSv = timing.GetSvAtTime(ho.Time);
                        double newSv = oldSv / ((newSpatialLength / oldSpatialLength) / (newTemporalLength / oldTemporalLength));

                        ho.SliderVelocity = newSv;
                        ho.PixelLength = newSpatialLength;

                        // Scale anchors to completion
                        if (arg.ScaleAnchors) {
                            var sliderPath = ho.GetSliderPath();
                            switch (sliderPath.Type) {
                                case PathType.Bezier:
                                    if (arg.SpatialLength > 1) {
                                        // Extend linearly
                                    } else {
                                        // Find the last bezier segment and the pixel length at that part
                                        // Find T for the remaining pixel length
                                        // ScaleRight the BezierSubdivision
                                        // Replace the anchors
                                    }
                                    ho.CurvePoints[1] = sliderPath.PositionAt(1);
                                    break;
                                case PathType.PerfectCurve:
                                    ho.CurvePoints[0] = sliderPath.PositionAt(0.5);
                                    ho.CurvePoints[1] = sliderPath.PositionAt(1);
                                    break;
                                default:
                                    ho.CurvePoints[1] = sliderPath.PositionAt(1);
                                    break;
                            }
                        }

                        slidersCompleted++;
                    }
                    if (worker != null && worker.WorkerReportsProgress) {
                        worker.ReportProgress(i / markedObjects.Count);
                    }
                }

                // Reconstruct SliderVelocity
                List<TimingPointsChange> timingPointsChanges = new List<TimingPointsChange>();
                // Add Hitobject stuff
                foreach (HitObject ho in beatmap.HitObjects) {
                    if (ho.IsSlider) // SliderVelocity changes
                    {
                        TimingPoint tp = ho.TimingPoint.Copy();
                        tp.Offset = ho.Time;
                        tp.MpB = ho.SliderVelocity;
                        timingPointsChanges.Add(new TimingPointsChange(tp, mpb: true));
                    }
                }

                // Add the new SliderVelocity changes
                TimingPointsChange.ApplyChanges(timing, timingPointsChanges);

                // Save the file
                editor.SaveFile();
            }

            // Complete progressbar
            if (worker != null && worker.WorkerReportsProgress)
            {
                worker.ReportProgress(100);
            }

            // Do stuff
            if (arg.Quick)
                RunFinished?.Invoke(this, new RunToolCompletedEventArgs(true, editorRead));

            // Make an accurate message
            string message = "";
            if (Math.Abs(slidersCompleted) == 1)
            {
                message += "Successfully completed " + slidersCompleted + " slider!";
            }
            else
            {
                message += "Successfully completed " + slidersCompleted + " sliders!";
            }
            return arg.Quick ? "" : message;
        }
    }
}
