using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InteroExposLab.Models;
using Microsoft.Maui.Devices;

namespace InteroExposLab.Services;

/// <summary>
/// Motor en C# nativo para procesamiento psicofisiológico en tiempo real (RMSSD, primera derivada dFC/dt,
/// Tarea de Precisión Interoceptiva de Schandry) y control del motor háptico nativo de Android/Windows.
/// </summary>
public sealed class HeartRateBiofeedbackEngine
{
    private readonly Queue<int> _rrWindow = new();
    private readonly List<(DateTime TimeUtc, int HrBpm)> _hrHistory10s = new();
    private readonly List<TelemetrySample> _currentPhaseSamples = new();
    private CancellationTokenSource? _hapticLoopCts;

    public string SubjectId { get; set; } = "SUB_001";
    public string ExperimentId { get; set; } = "EXP1_MODES";
    public string PhobiaType { get; set; } = "SPIDERS";
    public string Asi3AndMaiaScores { get; set; } = string.Empty;

    public FeedbackCondition ActiveCondition { get; set; } = FeedbackCondition.Continuous1To1;
    public ProtocolPhase ActivePhase { get; private set; } = ProtocolPhase.BASELINE;

    public int BaselineHrBpm { get; set; } = 72;
    public double ThresholdPercentAboveBaseline { get; set; } = 15.0;
    public double ReliefSlopeThresholdBpmPer10s { get; set; } = -2.5;
    public double SlopeGainGamma { get; set; } = 1.0;
    public int BasePulseDurationMs { get; set; } = 55;

    public int CurrentHrBpm { get; private set; }
    public int CurrentRrMs { get; private set; }
    public double CurrentRmssdMs { get; private set; }
    public double CurrentSlope10sBpm { get; private set; }
    public int TargetHapticBpm { get; private set; }
    public int EffectivePulseDurationMs { get; private set; } = 55;
    public string ActiveHapticPatternLabel { get; private set; } = "Inactivo";

    public bool IsRecording { get; set; }
    public DateTime SessionStartUtc { get; set; } = DateTime.UtcNow;
    public string PendingEventMarker { get; set; } = string.Empty;

    private bool _peakReachedInCurrentPhase;
    private int _phasePeakHrBpm;

    public List<TelemetrySample> TimeseriesLog { get; } = new();
    public List<ExposureBlockSummary> TrialsSummaryLog { get; } = new();

    public event EventHandler<TelemetrySample>? SampleProcessed;

    public void ChangePhase(ProtocolPhase newPhase)
    {
        ActivePhase = newPhase;
        _currentPhaseSamples.Clear();
        _peakReachedInCurrentPhase = false;
        _phasePeakHrBpm = CurrentHrBpm;
        PendingEventMarker = $"PHASE_START_{newPhase}";
    }

    public void ProcessPacket(HeartRatePacketEventArgs packet)
    {
        var now = packet.TimestampUtc;
        CurrentHrBpm = packet.HeartRateBpm;

        if (packet.RrIntervalsMs.Count > 0)
        {
            CurrentRrMs = packet.RrIntervalsMs[^1];
            foreach (int rr in packet.RrIntervalsMs)
            {
                _rrWindow.Enqueue(rr);
                while (_rrWindow.Count > 30)
                {
                    _rrWindow.Dequeue();
                }
            }
            CurrentRmssdMs = ComputeRmssd(_rrWindow.ToArray());
        }

        // Ventana deslizante de 10 segundos para primera derivada dFC/dt
        _hrHistory10s.Add((now, CurrentHrBpm));
        _hrHistory10s.RemoveAll(x => (now - x.TimeUtc).TotalSeconds > 10.5);
        CurrentSlope10sBpm = ComputeSlope10s(_hrHistory10s);

        double thresholdHr = BaselineHrBpm * (1.0 + ThresholdPercentAboveBaseline / 100.0);
        if (CurrentHrBpm >= thresholdHr)
        {
            _peakReachedInCurrentPhase = true;
        }
        if (CurrentHrBpm > _phasePeakHrBpm)
        {
            _phasePeakHrBpm = CurrentHrBpm;
        }

        UpdateHapticTarget(CurrentHrBpm, thresholdHr);

        var sample = new TelemetrySample
        {
            TimestampIso = now,
            ElapsedSeconds = Math.Round((now - SessionStartUtc).TotalSeconds, 2),
            SubjectId = SubjectId,
            ExperimentId = ExperimentId,
            PhobiaType = PhobiaType,
            Condition = ActiveCondition,
            Phase = ActivePhase,
            HeartRateBpm = CurrentHrBpm,
            BaselineHrBpm = BaselineHrBpm,
            LastRrMs = CurrentRrMs,
            RmssdMs = Math.Round(CurrentRmssdMs, 1),
            Slope10sBpm = Math.Round(CurrentSlope10sBpm, 2),
            DeliveredHapticBpm = TargetHapticBpm,
            EffectivePulseDurationMs = EffectivePulseDurationMs,
            HapticPatternLabel = ActiveHapticPatternLabel,
            EventMarker = PendingEventMarker
        };

        PendingEventMarker = string.Empty;
        _currentPhaseSamples.Add(sample);

        if (IsRecording)
        {
            TimeseriesLog.Add(sample);
        }

        SampleProcessed?.Invoke(this, sample);
    }

    private void UpdateHapticTarget(int hr, double thresholdHr)
    {
        EffectivePulseDurationMs = BasePulseDurationMs;

        // Silenciar siempre en Línea Base y en el Test de Retención sin Feedback (control de señal de seguridad)
        if (ActivePhase is ProtocolPhase.BASELINE or ProtocolPhase.RETENTION_NO_FB)
        {
            TargetHapticBpm = 0;
            ActiveHapticPatternLabel = ActivePhase == ProtocolPhase.RETENTION_NO_FB
                ? "Silenciado (Test Retención)"
                : "Silenciado (Línea Base)";
            return;
        }

        bool isAboveThreshold = hr >= thresholdHr;
        bool isDecelerating = CurrentSlope10sBpm <= ReliefSlopeThresholdBpmPer10s &&
                              (_peakReachedInCurrentPhase || hr > BaselineHrBpm + 4);

        switch (ActiveCondition)
        {
            case FeedbackCondition.NoFeedbackControl:
                TargetHapticBpm = 0;
                ActiveHapticPatternLabel = "Sin vibración (Control)";
                break;

            case FeedbackCondition.Continuous1To1:
                TargetHapticBpm = hr;
                ActiveHapticPatternLabel = "Pulso 1:1 Sincronizado";
                break;

            case FeedbackCondition.DynamicThresholdAndRelief:
                if (isDecelerating)
                {
                    TargetHapticBpm = Math.Max(55, (int)Math.Round(hr * 0.85));
                    ActiveHapticPatternLabel = "Doble Pulso (Alivio ↓)";
                }
                else if (isAboveThreshold)
                {
                    TargetHapticBpm = hr;
                    ActiveHapticPatternLabel = "Alerta Umbral ↑";
                }
                else
                {
                    TargetHapticBpm = 0;
                    ActiveHapticPatternLabel = "Bajo umbral (En espera)";
                }
                break;

            case FeedbackCondition.ReliefDerivativeLocked:
                if (isDecelerating)
                {
                    TargetHapticBpm = Math.Max(55, (int)Math.Round(hr * 0.85));
                    ActiveHapticPatternLabel = "Doble Pulso (Alivio ↓)";
                }
                else
                {
                    TargetHapticBpm = 0;
                    ActiveHapticPatternLabel = "Esperando derivada < 0";
                }
                break;

            case FeedbackCondition.ArousalPeakLocked:
                if (isAboveThreshold)
                {
                    TargetHapticBpm = hr;
                    ActiveHapticPatternLabel = "Alerta Umbral ↑";
                }
                else
                {
                    TargetHapticBpm = 0;
                    ActiveHapticPatternLabel = "Bajo umbral (Silencio)";
                }
                break;

            case FeedbackCondition.SlopeGainPerturbation:
                // Exp 2: Respeta el pico cardíaco real al inicio y modula la pendiente de descenso por gamma
                int peak = Math.Max(hr, _phasePeakHrBpm > 0 ? _phasePeakHrBpm : hr);
                int dropFromPeak = peak - hr;
                int modulatedHr = (int)Math.Round(peak - SlopeGainGamma * dropFromPeak);
                TargetHapticBpm = Math.Clamp(modulatedHr, 45, 195);
                ActiveHapticPatternLabel = $"Pendiente γ={SlopeGainGamma:F2} (Pico {peak})";
                break;

            case FeedbackCondition.AdaptiveHapticFading:
                // Exp 3: Desvanecimiento progresivo de la duración del pulso del Bloque 1 al Bloque 5
                double fadeFactor = GetAdaptiveFadingFactor(ActivePhase);
                EffectivePulseDurationMs = Math.Max(8, (int)Math.Round(BasePulseDurationMs * fadeFactor));
                TargetHapticBpm = hr;
                ActiveHapticPatternLabel = $"Fading {(int)(fadeFactor * 100)}% ({EffectivePulseDurationMs}ms)";
                break;

            case FeedbackCondition.ShamYokedControl:
                double slowWave = Math.Sin(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 6000.0) * 2.5;
                TargetHapticBpm = (int)Math.Round(BaselineHrBpm + slowWave);
                ActiveHapticPatternLabel = "Sham Desacoplado";
                break;
        }
    }

    public void StartNativeHapticLoop()
    {
        StopNativeHapticLoop();
        _hapticLoopCts = new CancellationTokenSource();
        var token = _hapticLoopCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (TargetHapticBpm >= 40)
                {
                    int intervalMs = Math.Max(280, (int)Math.Round(60000.0 / TargetHapticBpm));
                    await TriggerNativeVibrationAsync(ActiveHapticPatternLabel, EffectivePulseDurationMs);
                    await Task.Delay(intervalMs, token);
                }
                else
                {
                    await Task.Delay(250, token);
                }
            }
        }, token);
    }

    public void StopNativeHapticLoop()
    {
        if (_hapticLoopCts != null)
        {
            _hapticLoopCts.Cancel();
            _hapticLoopCts.Dispose();
            _hapticLoopCts = null;
        }
    }

    private static async Task TriggerNativeVibrationAsync(string patternLabel, int pulseDurationMs)
    {
        try
        {
            if (!Vibration.Default.IsSupported) return;

            if (patternLabel.Contains("Doble Pulso", StringComparison.OrdinalIgnoreCase))
            {
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(35));
                await Task.Delay(95);
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(55));
            }
            else
            {
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(pulseDurationMs));
            }
        }
        catch
        {
            // Ignorar en entornos de escritorio sin motor háptico
        }
    }

    public ExposureBlockSummary SaveCurrentBlockSummary(
        double perceivedHrBpm,
        double threatExpectancy,
        double suds,
        double relief,
        double? schandryIacc = null)
    {
        double meanHr = _currentPhaseSamples.Count > 0
            ? Math.Round(_currentPhaseSamples.Average(s => s.HeartRateBpm), 1)
            : CurrentHrBpm;
        int peakHr = _currentPhaseSamples.Count > 0
            ? _currentPhaseSamples.Max(s => s.HeartRateBpm)
            : CurrentHrBpm;
        double meanRmssd = _currentPhaseSamples.Count > 0
            ? Math.Round(_currentPhaseSamples.Average(s => s.RmssdMs), 1)
            : Math.Round(CurrentRmssdMs, 1);

        var summary = new ExposureBlockSummary
        {
            TimeLocal = DateTime.Now.ToString("HH:mm:ss"),
            SubjectId = SubjectId,
            ExperimentId = ExperimentId,
            PhobiaType = PhobiaType,
            Asi3AndMaiaScores = Asi3AndMaiaScores,
            SchandryIaccScore = schandryIacc,
            Condition = ActiveCondition,
            Phase = ActivePhase,
            BaselineHrBpm = BaselineHrBpm,
            MeanHrBpm = meanHr,
            PeakHrBpm = peakHr,
            MeanRmssdMs = meanRmssd,
            PerceivedHrBpm = perceivedHrBpm,
            ThreatExpectancy0To100 = threatExpectancy,
            Suds0To100 = suds,
            SubjectiveRelief0To100 = relief,
            EndingSlope10sBpm = Math.Round(CurrentSlope10sBpm, 2)
        };

        TrialsSummaryLog.Add(summary);
        PendingEventMarker = $"BLOCK_SAVED_{ActivePhase}";
        return summary;
    }

    public static double CalculateSchandryInteroceptiveAccuracy(int realBeats, int reportedBeats)
    {
        if (realBeats <= 0) return 0.0;
        double score = 1.0 - Math.Abs(realBeats - reportedBeats) / (double)realBeats;
        return Math.Round(Math.Max(0.0, score), 3);
    }

    private static double GetAdaptiveFadingFactor(ProtocolPhase phase) => phase switch
    {
        ProtocolPhase.EXP_B1 => 1.00,
        ProtocolPhase.EXP_B2 => 0.80,
        ProtocolPhase.EXP_B3 => 0.60,
        ProtocolPhase.EXP_B4 => 0.35,
        ProtocolPhase.EXP_B5 => 0.15,
        _ => 1.00
    };

    private static double ComputeRmssd(IReadOnlyList<int> rrIntervals)
    {
        if (rrIntervals.Count < 2) return 0.0;
        double sumSqDiff = 0.0;
        for (int i = 1; i < rrIntervals.Count; i++)
        {
            double diff = rrIntervals[i] - rrIntervals[i - 1];
            sumSqDiff += diff * diff;
        }
        return Math.Sqrt(sumSqDiff / (rrIntervals.Count - 1));
    }

    private static double ComputeSlope10s(IReadOnlyList<(DateTime TimeUtc, int HrBpm)> history)
    {
        if (history.Count < 3) return 0.0;
        var first = history[0];
        var last = history[^1];
        double dtSec = (last.TimeUtc - first.TimeUtc).TotalSeconds;
        if (dtSec < 2.0) return 0.0;
        return ((last.HrBpm - first.HrBpm) / dtSec) * 10.0;
    }
}
