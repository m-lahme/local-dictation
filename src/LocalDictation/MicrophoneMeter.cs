using System;
using NAudio.Wave;
namespace LocalDictation
{
    public static class MicrophoneMeter
    {
        static readonly Guid Float = new("00000003-0000-0010-8000-00aa00389b71");
        public static double Measure(byte[] data,int count,WaveFormat format)
        {
            int stride=format.BitsPerSample/8;bool floating=format.Encoding==WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat==Float;
            if(stride<1 || stride>4)return 0;
            double sum=0,peak=0;int samples=0;
            for(int i=0;i+stride<=count;i+=stride) {
                double x=stride==4?(floating?BitConverter.ToSingle(data,i):BitConverter.ToInt32(data,i)/2147483648.0):stride==3?((data[i]|data[i+1]<<8|data[i+2]<<16)<<8)/2147483648.0:stride==2?BitConverter.ToInt16(data,i)/32768.0:(data[i]-128)/128.0;
                if(!double.IsFinite(x))continue;x=Math.Clamp(x,-1,1);sum+=x*x;peak=Math.Max(peak,Math.Abs(x));samples++;
            }
            if(samples==0)return 0;
            // Speech energy dominates; a small peak component keeps consonants visible without pinning all bars.
            double amplitude=.8*Math.Sqrt(sum/samples)+.2*peak;
            return amplitude<=0?0:Math.Clamp((20*Math.Log10(amplitude)+55)/43,0,1);
        }
    }
}
