using System;
using NAudio.Wave;
namespace LocalDictation
{
    public sealed class MicrophoneBands
    {
        readonly WaveFormat format; readonly double[] low=new double[5], coefficient=new double[5]; readonly bool floating; readonly int stride;
        public MicrophoneBands(WaveFormat value){format=value;stride=format.BitsPerSample/8;floating=format.Encoding==WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible e && e.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");double[] frequency={180,450,1100,2400,4800};for(int i=0;i<5;i++)coefficient[i]=1-Math.Exp(-2*Math.PI*frequency[i]/format.SampleRate);}
        double Sample(byte[] data,int i)=>stride==4?(floating?BitConverter.ToSingle(data,i):BitConverter.ToInt32(data,i)/2147483648.0):stride==3?((data[i]|data[i+1]<<8|data[i+2]<<16)<<8)/2147483648.0:stride==2?BitConverter.ToInt16(data,i)/32768.0:(data[i]-128)/128.0;
        public double[] Measure(byte[] data,int count){var energy=new double[5];if(stride<1||stride>4)return energy;int frames=0;for(int n=0;n+format.BlockAlign<=count;n+=format.BlockAlign){double sample=0;for(int c=0;c<format.Channels;c++)sample+=Sample(data,n+c*stride)/format.Channels;if(!double.IsFinite(sample))continue;frames++;double previous=0;for(int i=0;i<5;i++){low[i]+=coefficient[i]*(sample-low[i]);double band=low[i]-previous;previous=low[i];energy[i]+=band*band;}}for(int i=0;i<5;i++){double rms=frames>0?Math.Sqrt(energy[i]/frames):0;energy[i]=rms<=0?0:Math.Clamp((20*Math.Log10(rms)+62)/42,0,1);}return energy;}
    }
}
