using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics;
using QPSK.Models;
namespace QPSK;
/// <summary>
/// 
/// </summary>
/// <param name="SampleRate">The sampling rate of your transciever</param>
/// <param name="SymbolRate">The symbol rate which correspond baud rate and cannot be higher than SampleRate/2</param>
/// <param name="RrcAlpha">The Root raised cosine pulse Shaping Alpha</param>
/// <param name="rrcSpan">the span of the RRC Root raise cosine filter common 4-10</param>
/// <param name="differentialEncoding">use differentialEncoding to defeat phase ambiguity</param>
public class QPSKModulator(int SampleRate,int SymbolRate,double RrcAlpha = .9,int rrcSpan = 6, bool differentialEncoding = true)
{
    private double[] rrcCoeff = RRCFilter.generateCoefficents(rrcSpan, RrcAlpha, SampleRate,SymbolRate);
    public double[] getCoeef() => rrcCoeff;
    public long baudRate = 2 * SymbolRate / 8;
    public Complex[] Modulate(string data,bool pulseShaping = true)
    {
        List<Complex> result = new List<Complex>();
        //adding delay for pulse shaping
        result.AddRange(Enumerable.Range(0, (int)(rrcCoeff.Length - 1) / 2).Select(x=>new Complex(0,0)));
        var samplesPerSymbol = Convert.ToInt32(SampleRate / SymbolRate);
        char[] data_str = data.ToCharArray();
        //simple modulation
        for (int Bit = 0; Bit < data_str.Length -1; Bit+=2)
        {
            int bI = data_str[Bit]     - '0'; // '0' -> 0, '1' -> 1
            int bQ = data_str[Bit + 1] - '0';

            double I = (2 * bI - 1) / Math.Sqrt(2); // 0 -> -1, 1 -> +1
            double Q = (2 * bQ - 1) / Math.Sqrt(2);
            result.Add(new Complex(I,Q));
            result.AddRange(Enumerable.Range(0, samplesPerSymbol-1).Select(x=> new Complex(0,0)));//upsample for Pulse Shaping
        }
        if (!pulseShaping)
            return result.ToArray();
        //adding delay for pulse shaping
        result.AddRange(Enumerable.Range(0, (int)(rrcCoeff.Length - 1) / 2).Select(x => new Complex(0, 0)));
        //pulse Shaping
        return result.ToArray().FftConvolve(rrcCoeff);
    }
}