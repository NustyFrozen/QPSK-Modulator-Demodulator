# C# QPSK DE-/MODULATOR
This repository is a working prototype of QPSK (Quadrature phase shift keying) modulator and Demodulator in C# (with channel impairment compensation: unstable transcievers LO, multipath,etc...)
<br>

## Simulations:
### SDR Test
https://github.com/user-attachments/assets/674c7288-a754-4996-8839-5d62c6617ec3

### RF Simulation
https://github.com/user-attachments/assets/6042c9d5-f562-477f-801c-027d9fddc0c0

## capabilities
- the modulator and demodulator both uses RRC for ISI mitagation, message framing to have consistent "packets",differential encoding for phase ambiguity, and TSC (for stabling receiver PLL & symbol sync before starting to send actual data)
- you may send raw data as byte[], raw text or string of bytes (0101...)
- FIR uses SIMD techniques, all models try to implement as much as possible data as span (faster for iteration)
- demod chain: Band-Edge FLL -> MF(RRC) -> Mueller muller (TED) + cubic lagrange interpolator -> Costas Loop -> decode message frame
  
##  Solution tree:
### QPSK (main module):
- QPSKDeModulator
- QPSKModulator
- Band-Edge FLL (Frequency Locked loop)
- General FIRFilter with group delay
- general RRC FIR taps generator
-  MuellerMuller TED (time error detector) + symbol sync based on TED with linear interpolator
- Costas Loop PLL (Phase Locked Loop)
- HelperFunctions for complex[] and float[] functions

### TestBench
- ModDemodOverSDR - sdr over the air test (tested on USRP b205 mini) + ZMQ -> Gnuradio View of the results with data framing
- testFullDemodChain - simulation of an SDR and the demodulation chain using two unstable LO and each demodulation block seperatley + ZMQ -> Gnuradio View of the results
- testAtDataLevel -> simple test of the mod and demod blocks and check for bit-Errors with two unstable LO

## dependencies:
- ZeroMQ
- MathNet
- PothosWare SoapySDR .NET bindings


### N.B
this project is heavily based on the course of "[Learn SDR with Prof Jason](https://www.youtube.com/watch?v=tj_9p_rXULM&list=PLywxmTaHNUNyKmgF70q8q3QHYIw_LFbrX)" by [HarveyMuddPhysicsElectronicsLab](https://www.youtube.com/@HarveyMuddPhysicsElectronics)
