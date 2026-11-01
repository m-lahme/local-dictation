using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;
using LocalDictation;
using NAudio.Wave;
internal static class MeterAudit
{
    static void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    public static void Run(string data){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var log=new List<string>();void Check(bool x,string label){if(!x)throw new Exception(label);log.Add("PASS "+label);}
        var floatFormat=WaveFormat.CreateIeeeFloatWaveFormat(16000,1);var silent=new byte[640];Check(MicrophoneMeter.Measure(silent,silent.Length,floatFormat)==0,"digital silence stays zero");
        var levels=new List<double>();foreach(double value in new[]{.001,.01,.05,.15,.3}){var b=new byte[640];for(int i=0;i<160;i++)BitConverter.GetBytes((float)(value*Math.Sin(i*.2))).CopyTo(b,i*4);levels.Add(MicrophoneMeter.Measure(b,b.Length,floatFormat));}Check(levels.Zip(levels.Skip(1),(a,b)=>b>a).All(x=>x),"meter has monotonic speech dynamic range");
        var spectrum=new MicrophoneBands(floatFormat);var tone=new byte[16000*4];for(int i=0;i<16000;i++)BitConverter.GetBytes((float)(.08*Math.Sin(2*Math.PI*130*i/16000))).CopyTo(tone,i*4);var spectrumLow=spectrum.Measure(tone,tone.Length);for(int i=0;i<16000;i++)BitConverter.GetBytes((float)(.08*Math.Sin(2*Math.PI*3200*i/16000))).CopyTo(tone,i*4);var spectrumHigh=spectrum.Measure(tone,tone.Length);Check(spectrumLow[0]>spectrumHigh[0]&&spectrumHigh[4]>spectrumLow[4],"different frequencies drive independent bars");
        var overlay=new OverlayWindow();overlay.Show();overlay.SetState(AppState.Recording,0);Pump(300);var grid=(Grid)((Border)overlay.Content).Child;var bar=(Rectangle)grid.Children[2];var frames=new List<(double t,double h)>();double previous=-1;EventHandler sample=(s,e)=>{double time=((RenderingEventArgs)e).RenderingTime.TotalMilliseconds;if(time!=previous){previous=time;frames.Add((time,bar.Height));}};CompositionTarget.Rendering+=sample;
        overlay.SetLevel(1);Pump(360);int split=frames.Count;overlay.SetLevel(0);Pump(550);CompositionTarget.Rendering-=sample;
        Check(split>=8,"frame-synchronous rise has at least 8 frames");Check(frames.Take(split).Select(x=>Math.Round(x.h,2)).Distinct().Count()>=8,"rise has distinct intermediate heights");Check(frames.Take(split).Max(x=>x.h)>13,"meter reaches speech peak");Check(frames.Last().h<5,"meter releases toward silence");Check(frames.Zip(frames.Skip(1),(a,b)=>Math.Abs(a.h-b.h)).Max()<6,"no abrupt height jumps");
        var intervals=frames.Zip(frames.Skip(1),(a,b)=>b.t-a.t).Order().ToArray();log.Add("MEASURE frames="+frames.Count+" median-frame-ms="+intervals[intervals.Length/2]);
        File.WriteAllLines(Path.Combine(data,"meter-frames.csv"),new[]{"milliseconds,height"}.Concat(frames.Select(x=>FormattableString.Invariant($"{x.t-frames[0].t:F3},{x.h:F4}"))));
        int packets=0;using(var audio=new AudioCapture()){audio.Levels+=x=>{packets++;overlay.SetLevels(x);};Pump(600);Check(audio.Ready,"live microphone ready");audio.Start();Pump(1000);audio.Cancel();Check(packets>5,"live microphone drives frame-smoothed overlay");}
        overlay.Close();app.Shutdown();File.WriteAllLines(Path.Combine(data,"meter-result.txt"),log);foreach(var line in log)Console.WriteLine(line);
    }
}
