using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using InteroExposLab.Models;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace InteroExposLab.Services;

/// <summary>
/// Exportador de datos en formato CSV con codificación UTF-8 BOM compatible directamente
/// con Microsoft Excel, OneDrive, SharePoint, Power BI, SPSS y JASP.
/// </summary>
public sealed class ExcelCsvExportService
{
    public async Task<string> ExportTimeseriesToExcelCsvAsync(string subjectId, IReadOnlyList<TelemetrySample> samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp_ISO,Elapsed_Sec,Subject_ID,Experiment,Phobia,Condition,Phase,HR_BPM,Baseline_HR,Delta_HR_BPM,RR_ms,RMSSD_ms,Slope_10s_BPM,Haptic_BPM_Delivered,Effective_Pulse_ms,Haptic_Pattern,Event_Marker");

        foreach (var s in samples)
        {
            sb.AppendLine(string.Join(",",
                s.TimestampIso.ToString("O"),
                s.ElapsedSeconds.ToString(CultureInfo.InvariantCulture),
                Escape(s.SubjectId),
                Escape(s.ExperimentId),
                Escape(s.PhobiaType),
                s.Condition,
                s.Phase,
                s.HeartRateBpm,
                s.BaselineHrBpm,
                s.DeltaHrBpm.ToString(CultureInfo.InvariantCulture),
                s.LastRrMs,
                s.RmssdMs.ToString(CultureInfo.InvariantCulture),
                s.Slope10sBpm.ToString(CultureInfo.InvariantCulture),
                s.DeliveredHapticBpm,
                s.EffectivePulseDurationMs,
                Escape(s.HapticPatternLabel),
                Escape(s.EventMarker)));
        }

        string fileName = $"SerieTemporal_{subjectId}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        string fullPath = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(fullPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Exportar Serie Temporal a Microsoft Excel / OneDrive",
            File = new ShareFile(fullPath)
        });

        return fullPath;
    }

    public async Task<string> ExportBlockSummariesToExcelCsvAsync(string subjectId, IReadOnlyList<ExposureBlockSummary> blocks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Time_Local,Subject_ID,Experiment,Phobia,ASI3_MAIA,Schandry_Iacc,Condition,Phase,Baseline_HR,Mean_HR,Peak_HR,Mean_RMSSD_ms,Perceived_HR,Interoceptive_Error_BPM,Expectancy_0_100,SUDS_0_100,Relief_0_100,Slope_10s_End");

        foreach (var b in blocks)
        {
            sb.AppendLine(string.Join(",",
                Escape(b.TimeLocal),
                Escape(b.SubjectId),
                Escape(b.ExperimentId),
                Escape(b.PhobiaType),
                Escape(b.Asi3AndMaiaScores),
                b.SchandryIaccScore?.ToString(CultureInfo.InvariantCulture) ?? "NA",
                b.Condition,
                b.Phase,
                b.BaselineHrBpm,
                b.MeanHrBpm.ToString(CultureInfo.InvariantCulture),
                b.PeakHrBpm,
                b.MeanRmssdMs.ToString(CultureInfo.InvariantCulture),
                b.PerceivedHrBpm.ToString(CultureInfo.InvariantCulture),
                b.InteroceptiveCalibrationErrorBpm.ToString(CultureInfo.InvariantCulture),
                b.ThreatExpectancy0To100.ToString(CultureInfo.InvariantCulture),
                b.Suds0To100.ToString(CultureInfo.InvariantCulture),
                b.SubjectiveRelief0To100.ToString(CultureInfo.InvariantCulture),
                b.EndingSlope10sBpm.ToString(CultureInfo.InvariantCulture)));
        }

        string fileName = $"ResumenBloques_{subjectId}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        string fullPath = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(fullPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Exportar Resumen de Bloques a Microsoft Excel / OneDrive",
            File = new ShareFile(fullPath)
        });

        return fullPath;
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
