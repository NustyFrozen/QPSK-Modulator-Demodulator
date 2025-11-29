using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics;

namespace Modulation_Simulation.Models;
/// <summary>
/// Constellation Mapping layout
/// mapping = AABBCCDD = 0b00011110 gray mapping
/// phase:
/// AA= +45
/// BB= +135
/// CC= -135
/// DD= -45
/// </summary>
public class QPSKModulator(int SampleRate,int SymbolRate)
{
    private double[] rrcCoeff = RRCFilter.generateCoefficents(4,0.7,SampleRate,SymbolRate);
    public Complex[] Modulate(string data)
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
        
        //pulse Shaping
        return result.ToArray().FftConvolve(rrcCoeff);
    }
    public char[] deModulate(Complex[] iqSamples)
    {
       return Array.Empty<char>();
    }
}