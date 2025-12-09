using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics;

namespace Modulation_Simulation.Models;
/// <summary>
/// Constellation Mapping layout
///
/// mapping = AABBCCDD = 0b00011110 gray mapping
/// phase:
/// AA= +45
/// BB= +135
/// CC= -135
/// DD= -45
/// </summary>
public class QPSKModulator(int SampleRate,int SymbolRate,double RrcAlpha = 0.7)
{
    private double[] rrcCoeff = RRCFilter.generateCoefficents(6, RrcAlpha, SampleRate,SymbolRate);
    public Complex[] Modulate(string data,bool pulseShaping = true)
    {
        List<Complex> result = new List<Complex>();
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
        //pulse Shaping
        return result.ToArray().FftConvolve(rrcCoeff);
    }


    private FLLBandEdgeFilter fllBandEdgeFilter = new FLLBandEdgeFilter(SymbolRate, (float)RrcAlpha, 33,(float)(2.0 * Math.PI / SymbolRate / 100.0));
    private SymbolSync symbolSync = new SymbolSync(RRCFilter.generateCoefficents(4, RrcAlpha, SampleRate, SymbolRate), SymbolRate);
   // private CostasLoopQpsk costasLoopQpsk = new CostasLoopQpsk();
    public void deModulate(Complex[] iqSamples)
    {
       iqSamples = fllBandEdgeFilter.Process(iqSamples);
        for(int i =0;i<iqSamples.Length;i++)
       symbolSync.ProcessSample(i,(y,yp,e_timing) =>
       {
         //  var results = costasLoopQpsk.Process(y);
       //    Console.WriteLine($"({results})");
       });
    }
}