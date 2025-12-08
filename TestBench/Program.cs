using Modulation_Simulation.Models;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Diagnostics;
using System.Numerics;
using TestBench;

const string address = "tcp://*:5555";
const int samplesPerFrame = 4096;     // must match "items per message" in GRC
const int floatsPerSample = 2;        // I and Q
const int bytesPerFloat = 4;

Console.WriteLine($"Binding PUB socket at {address}");
int sampleRate = 1_000_000, SymbolRate = 1600,fc = 1_000;
var FLL_offset = new NCO(fc, sampleRate);
var PLL_offset = new NCO(6, sampleRate);
QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate);
FLLBandEdgeFilter fllBandEdgeFilter = new FLLBandEdgeFilter(SymbolRate, (float)0.7, 33, (float)(2.0 * Math.PI / SymbolRate / 100.0));
var DATA = "0110100001100101011011000110110001101111001000000111011101101111011100100110110001100100";//"hello world"
var modulatedSignal = modulator.Modulate(DATA, true);
var noise = NoiseGenerator.GenerateIqNoise(-60, modulatedSignal.Length);
using (var pub = new PublisherSocket())
{
    pub.Bind(address);

    // PUB/SUB gotcha: subscribers won’t receive anything
    // until subscription has propagated. Often you:
    // - sleep a little,
    // - or repeatedly send frames (which we do anyway).
    Thread.Sleep(500);
    testCostas.RunTest(pub);
    int pos = 0;
    while (true)
    {
        // Prepare interleaved IQ as float[]
        float[] iqFloats_withOffset = new float[samplesPerFrame * floatsPerSample];
        float[] iqFLLCheck = new float[samplesPerFrame * floatsPerSample];

        for (int n = 0; n < samplesPerFrame; n++)
        {
            if (pos == modulatedSignal.Length)
                pos = 0;
            var sample = modulatedSignal[pos++];
            var sample_withOffset = sample * FLL_offset.NextSample();

            iqFloats_withOffset[2 * n] = (float)sample.Real; // I
            iqFloats_withOffset[2 * n + 1] = (float)sample.Imaginary; // Q

            var sample_withFLL = fllBandEdgeFilter.Process(sample_withOffset);
            iqFLLCheck[2*n] = (float)sample_withFLL.Real;
            iqFLLCheck[2*n + 1] = (float)sample_withFLL.Imaginary;
        }
        
        // Convert float[] to byte[] (little-endian)
        byte[] frame = new byte[iqFloats_withOffset.Length * bytesPerFloat];
        Buffer.BlockCopy(iqFloats_withOffset, 0, frame, 0, frame.Length);

        byte[] frame_2 = new byte[iqFLLCheck.Length * bytesPerFloat];
        Buffer.BlockCopy(iqFLLCheck, 0, frame, 0, frame.Length);
        // Send single ZMQ frame without topic
        pub.SendMoreFrame("baseband").SendFrame(frame);
        pub.SendMoreFrame("baseband_PostFLL").SendFrame(frame_2);


       
        Thread.Sleep(10); // Adjust for your data rate
    }
}
