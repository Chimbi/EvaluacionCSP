using System;
using InteroExposLab.Models;
using InteroExposLab.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace InteroExposLab;

public partial class MainPage : ContentPage
{
    private readonly BleHeartRateService _bleService;
    private readonly HeartRateBiofeedbackEngine _engine;
    private readonly ExcelCsvExportService _exportService;

    public MainPage(
        BleHeartRateService bleService,
        HeartRateBiofeedbackEngine engine,
        ExcelCsvExportService exportService)
    {
        InitializeComponent();
        _bleService = bleService;
        _engine = engine;
        _exportService = exportService;

        PickerCondition.SelectedIndex = 0;
        PickerPhase.SelectedIndex = 0;

        _bleService.HeartRateUpdated += OnHeartRateUpdated;
        _engine.SampleProcessed += OnSampleProcessed;
    }

    private void OnHeartRateUpdated(object? sender, HeartRatePacketEventArgs e)
    {
        SyncParametersFromUi();
        _engine.ProcessPacket(e);
    }

    private void OnSampleProcessed(object? sender, TelemetrySample sample)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LblHr.Text = $"{sample.HeartRateBpm}";
            string sign = sample.DeltaHrBpm >= 0 ? "+" : "";
            LblDeltaLb.Text = $"Δ LB: {sign}{sample.DeltaHrBpm:F0} BPM";
            LblRr.Text = $"{sample.LastRrMs}";
            LblRmssd.Text = $"RMSSD: {sample.RmssdMs:F1} ms";
            LblSlope.Text = $"{sample.Slope10sBpm:+0.0;-0.0;0.0}";
            LblState.Text = sample.Slope10sBpm <= _engine.ReliefSlopeThresholdBpmPer10s
                ? "Alivio / Bajada ↓"
                : "Activo / Estable";
            LblHapticBpm.Text = $"{sample.DeliveredHapticBpm}";
            LblHapticPattern.Text = sample.HapticPatternLabel;
        });
    }

    private void SyncParametersFromUi()
    {
        _engine.SubjectId = EntrySubjectId.Text?.Trim() ?? "SUB_001";
        if (int.TryParse(EntryBaselineHr.Text, out int lb)) _engine.BaselineHrBpm = lb;
        if (double.TryParse(EntryThresholdPct.Text, out double pct)) _engine.ThresholdPercentAboveBaseline = pct;
        if (double.TryParse(EntryReliefSlope.Text, out double slope)) _engine.ReliefSlopeThresholdBpmPer10s = slope;
        if (double.TryParse(EntryGamma.Text, out double gamma)) _engine.SlopeGainGamma = gamma;
    }

    private async void OnConnectBleClicked(object? sender, EventArgs e)
    {
        if (_bleService.IsConnectedBle)
        {
            await _bleService.DisconnectAsync();
            _engine.StopNativeHapticLoop();
            BtnConnectBle.Text = "Conectar Banda BLE";
            return;
        }

        BtnConnectBle.Text = "Buscando...";
        bool connected = await _bleService.ConnectToHeartRateBandAsync();
        if (connected)
        {
            DeviceDisplay.Current.KeepScreenOn = true;
            _engine.StartNativeHapticLoop();
            BtnConnectBle.Text = "Desconectar BLE";
        }
        else
        {
            BtnConnectBle.Text = "Conectar Banda BLE";
            await DisplayAlert("Bluetooth BLE", "No se encontró una banda con servicio Heart Rate (0x180D).", "OK");
        }
    }

    private void OnToggleSimulatorClicked(object? sender, EventArgs e)
    {
        if (_bleService.IsSimulating)
        {
            _bleService.StopSimulator();
            _engine.StopNativeHapticLoop();
            BtnSimulate.Text = "Simulador C#";
        }
        else
        {
            SyncParametersFromUi();
            _bleService.StartSimulator(_engine.BaselineHrBpm);
            _engine.StartNativeHapticLoop();
            BtnSimulate.Text = "Detener Simulador";
        }
    }

    private void OnConditionChanged(object? sender, EventArgs e)
    {
        if (PickerCondition.SelectedItem is string selected &&
            Enum.TryParse<FeedbackCondition>(selected, out var cond))
        {
            _engine.ActiveCondition = cond;
        }
    }

    private void OnPhaseChanged(object? sender, EventArgs e)
    {
        if (PickerPhase.SelectedItem is string selected &&
            Enum.TryParse<ProtocolPhase>(selected, out var phase))
        {
            _engine.ChangePhase(phase);
        }
    }

    private void OnToggleRecordingClicked(object? sender, EventArgs e)
    {
        _engine.IsRecording = !_engine.IsRecording;
        if (_engine.IsRecording)
        {
            _engine.SessionStartUtc = DateTime.UtcNow;
            DeviceDisplay.Current.KeepScreenOn = true;
            BtnRecord.Text = "Pausar Grabación";
            BtnRecord.BackgroundColor = Color.FromArgb("#EF4444");
        }
        else
        {
            BtnRecord.Text = "Reanudar Grabación";
            BtnRecord.BackgroundColor = Color.FromArgb("#10B981");
        }
    }

    private async void OnSaveBlockClicked(object? sender, EventArgs e)
    {
        SyncParametersFromUi();
        double.TryParse(EntryPerceivedHr.Text, out double perceivedHr);
        double.TryParse(EntryExpectancy.Text, out double exp);
        double.TryParse(EntrySuds.Text, out double suds);
        double.TryParse(EntryRelief.Text, out double relief);

        var summary = _engine.SaveCurrentBlockSummary(perceivedHr, exp, suds, relief);
        await DisplayAlert(
            "Bloque Registrado",
            $"Fase: {summary.Phase}\nFC Media: {summary.MeanHrBpm} BPM\nError Interoceptivo: {summary.InteroceptiveCalibrationErrorBpm:+0.0;-0.0;0.0} BPM",
            "OK");
    }

    private async void OnExportTimeseriesClicked(object? sender, EventArgs e)
    {
        if (_engine.TimeseriesLog.Count == 0)
        {
            await DisplayAlert("Exportar", "No hay muestras grabadas en la serie temporal.", "OK");
            return;
        }
        await _exportService.ExportTimeseriesToExcelCsvAsync(_engine.SubjectId, _engine.TimeseriesLog);
    }

    private async void OnExportBlocksClicked(object? sender, EventArgs e)
    {
        if (_engine.TrialsSummaryLog.Count == 0)
        {
            await DisplayAlert("Exportar", "No hay bloques guardados todavía.", "OK");
            return;
        }
        await _exportService.ExportBlockSummariesToExcelCsvAsync(_engine.SubjectId, _engine.TrialsSummaryLog);
    }

    private void OnEnterBlindModeClicked(object? sender, EventArgs e)
    {
        DeviceDisplay.Current.KeepScreenOn = true;
        BlindOverlay.IsVisible = true;
    }

    private void OnExitBlindModeClicked(object? sender, EventArgs e)
    {
        BlindOverlay.IsVisible = false;
    }
}
