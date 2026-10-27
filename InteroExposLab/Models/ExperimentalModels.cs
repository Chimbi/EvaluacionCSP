using System;
using System.Collections.Generic;

namespace InteroExposLab.Models;

/// <summary>
/// Condiciones experimentales inéditas para la serie de 3 experimentos traslacionales.
/// </summary>
public enum FeedbackCondition
{
    /// <summary>Exp 1: Pulso sincronizado latido a latido (1:1).</summary>
    Continuous1To1,

    /// <summary>Exp 1: Umbral de activación (+% LB) + Doble pulso suave en derivada negativa (Alivio).</summary>
    DynamicThresholdAndRelief,

    /// <summary>Exp 1 (Inédito): Bloqueado exclusivamente a la derivada de desaceleración (dFC/dt <= umbral).</summary>
    ReliefDerivativeLocked,

    /// <summary>Exp 1 (Inédito): Bloqueado exclusivamente al pico simpático sobre umbral (+% LB).</summary>
    ArousalPeakLocked,

    /// <summary>Exp 2 (Inédito): Pico inicial 100% verídico + Perturbación de ganancia de la pendiente de recuperación (gamma).</summary>
    SlopeGainPerturbation,

    /// <summary>Exp 3 (Inédito): Desvanecimiento háptico adaptativo (B1 100% -> B5 15%) para evitar efecto de señal de seguridad.</summary>
    AdaptiveHapticFading,

    /// <summary>Control Activo Sham / Yoked: Vibración tónica sinusoidal alrededor de línea base desacoplada.</summary>
    ShamYokedControl,

    /// <summary>Control Exposición Estándar: Sin vibración (solo registro psicofisiológico BLE).</summary>
    NoFeedbackControl
}

public enum ProtocolPhase
{
    BASELINE,
    PRE_EXP,
    EXP_B1,
    EXP_B2,
    EXP_B3,
    EXP_B4,
    EXP_B5,
    RECOVERY,
    RETENTION_NO_FB
}

public sealed class HeartRatePacketEventArgs : EventArgs
{
    public int HeartRateBpm { get; init; }
    public IReadOnlyList<int> RrIntervalsMs { get; init; } = Array.Empty<int>();
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public sealed class TelemetrySample
{
    public DateTime TimestampIso { get; init; }
    public double ElapsedSeconds { get; init; }
    public string SubjectId { get; init; } = "SUB_001";
    public string ExperimentId { get; init; } = "EXP1_MODES";
    public string PhobiaType { get; init; } = "SPIDERS";
    public FeedbackCondition Condition { get; init; }
    public ProtocolPhase Phase { get; init; }
    public int HeartRateBpm { get; init; }
    public int BaselineHrBpm { get; init; }
    public double DeltaHrBpm => HeartRateBpm - BaselineHrBpm;
    public int LastRrMs { get; init; }
    public double RmssdMs { get; init; }
    public double Slope10sBpm { get; init; }
    public int DeliveredHapticBpm { get; init; }
    public int EffectivePulseDurationMs { get; init; }
    public string HapticPatternLabel { get; init; } = string.Empty;
    public string EventMarker { get; init; } = string.Empty;
}

public sealed class ExposureBlockSummary
{
    public string TimeLocal { get; init; } = string.Empty;
    public string SubjectId { get; init; } = "SUB_001";
    public string ExperimentId { get; init; } = "EXP1_MODES";
    public string PhobiaType { get; init; } = "SPIDERS";
    public string Asi3AndMaiaScores { get; init; } = string.Empty;
    public double? SchandryIaccScore { get; init; }
    public FeedbackCondition Condition { get; init; }
    public ProtocolPhase Phase { get; init; }
    public int BaselineHrBpm { get; init; }
    public double MeanHrBpm { get; init; }
    public int PeakHrBpm { get; init; }
    public double MeanRmssdMs { get; init; }
    public double PerceivedHrBpm { get; init; }
    public double InteroceptiveCalibrationErrorBpm => PerceivedHrBpm - MeanHrBpm;
    public double ThreatExpectancy0To100 { get; init; }
    public double Suds0To100 { get; init; }
    public double SubjectiveRelief0To100 { get; init; }
    public double EndingSlope10sBpm { get; init; }
}
