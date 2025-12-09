using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
    public class QPSKDeModulator(int SampleRate, int SymbolRate, double RrcAlpha = 0.7, int rrcSpan = 6)
    {
    ComplexFIRFilter rrc = new ComplexFIRFilter(RRCFilter.generateCoefficents(rrcSpan, RrcAlpha, SampleRate, SymbolRate).Select(x => new Complex(x, 0)).ToArray());
    MuellerMuller symbolSync = new MuellerMuller(SampleRate / SymbolRate, 1e-3, 1e-5);
    public string DeModulate(Complex[] Samples)
            {
        return null;
            }
    }

