using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InteroExposLab.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace InteroExposLab.Services;

/// <summary>
/// Servicio Bluetooth Low Energy (BLE) para bandas pectorales con el estándar GATT Heart Rate (0x180D / 0x2A37)
/// e implementación de Simulador Fisiológico en C# para pruebas de laboratorio.
/// </summary>
public sealed class BleHeartRateService
{
    // UUIDs estándar Bluetooth SIG para Heart Rate Service (0x180D) y Heart Rate Measurement (0x2A37)
    private static readonly Guid HeartRateServiceUuid = Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb");
    private static readonly Guid HeartRateMeasurementCharacteristicUuid = Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb");

    private IAdapter? _adapter;
    private IDevice? _connectedDevice;
    private ICharacteristic? _hrCharacteristic;
    private CancellationTokenSource? _simulatorCts;
    private double _simulatedHr = 72.0;

    public bool IsConnectedBle => _connectedDevice != null;
    public bool IsSimulating { get; private set; }
    public string SimulatorProfile { get; set; } = "REST";
    public string ConnectedDeviceName => _connectedDevice?.Name ?? (IsSimulating ? "Simulador Fisiológico C#" : "Desconectado");

    public event EventHandler<HeartRatePacketEventArgs>? HeartRateUpdated;

    public async Task<bool> ConnectToHeartRateBandAsync(CancellationToken cancellationToken = default)
    {
        StopSimulator();

        var ble = CrossBluetoothLE.Current;
        _adapter = ble.Adapter;
        _adapter.ScanTimeout = 10000;

        IDevice? targetDevice = null;
        void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Device.Name))
            {
                targetDevice = args.Device;
            }
        }

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        try
        {
            await _adapter.StartScanningForDevicesAsync(
                serviceUuids: new[] { HeartRateServiceUuid },
                cancellationToken: cancellationToken);
        }
        finally
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
        }

        if (targetDevice == null)
        {
            return false;
        }

        await _adapter.ConnectToDeviceAsync(targetDevice, cancellationToken: cancellationToken);
        _connectedDevice = targetDevice;

        var service = await targetDevice.GetServiceAsync(HeartRateServiceUuid, cancellationToken);
        if (service == null) return false;

        _hrCharacteristic = await service.GetCharacteristicAsync(HeartRateMeasurementCharacteristicUuid);
        if (_hrCharacteristic == null) return false;

        _hrCharacteristic.ValueUpdated += OnCharacteristicValueUpdated;
        await _hrCharacteristic.StartUpdatesAsync(cancellationToken);
        return true;
    }

    public async Task DisconnectAsync()
    {
        if (_hrCharacteristic != null)
        {
            _hrCharacteristic.ValueUpdated -= OnCharacteristicValueUpdated;
            try { await _hrCharacteristic.StopUpdatesAsync(); } catch { /* ignore */ }
            _hrCharacteristic = null;
        }

        if (_adapter != null && _connectedDevice != null)
        {
            try { await _adapter.DisconnectDeviceAsync(_connectedDevice); } catch { /* ignore */ }
            _connectedDevice = null;
        }
    }

    /// <summary>
    /// Decodifica el paquete GATT 0x2A37 según la especificación oficial Bluetooth SIG:
    /// Bit 0 = formato de FC (0 = UINT8, 1 = UINT16)
    /// Bit 3 = presencia de Energy Expended
    /// Bit 4 = presencia de uno o más intervalos R-R (en unidades de 1/1024 s)
    /// </summary>
    public static HeartRatePacketEventArgs DecodeHeartRateMeasurementPacket(byte[] data)
    {
        if (data == null || data.Length < 2)
        {
            return new HeartRatePacketEventArgs { HeartRateBpm = 0 };
        }

        byte flags = data[0];
        bool is16BitHr = (flags & 0x01) != 0;
        bool hasEnergyExpended = (flags & 0x08) != 0;
        bool hasRrIntervals = (flags & 0x10) != 0;

        int offset = 1;
        int hrBpm;
        if (is16BitHr && offset + 1 < data.Length)
        {
            hrBpm = BitConverter.ToUInt16(data, offset);
            offset += 2;
        }
        else
        {
            hrBpm = data[offset];
            offset += 1;
        }

        if (hasEnergyExpended && offset + 1 < data.Length)
        {
            offset += 2;
        }

        var rrIntervalsMs = new List<int>();
        if (hasRrIntervals)
        {
            while (offset + 1 < data.Length)
            {
                ushort rawRr = BitConverter.ToUInt16(data, offset);
                int rrMs = (int)Math.Round(rawRr / 1024.0 * 1000.0);
                if (rrMs is >= 250 and <= 2000)
                {
                    rrIntervalsMs.Add(rrMs);
                }
                offset += 2;
            }
        }
        else if (hrBpm > 0)
        {
            rrIntervalsMs.Add((int)Math.Round(60000.0 / hrBpm));
        }

        return new HeartRatePacketEventArgs
        {
            HeartRateBpm = hrBpm,
            RrIntervalsMs = rrIntervalsMs,
            TimestampUtc = DateTime.UtcNow
        };
    }

    private void OnCharacteristicValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var packet = DecodeHeartRateMeasurementPacket(e.Characteristic.Value);
        if (packet.HeartRateBpm > 0)
        {
            HeartRateUpdated?.Invoke(this, packet);
        }
    }

    public void StartSimulator(int baselineHr = 72)
    {
        StopSimulator();
        IsSimulating = true;
        _simulatedHr = baselineHr;
        _simulatorCts = new CancellationTokenSource();
        var token = _simulatorCts.Token;

        _ = Task.Run(async () =>
        {
            var rng = new Random();
            while (!token.IsCancellationRequested)
            {
                double jitter = (rng.NextDouble() - 0.5) * 2.2;
                switch (SimulatorProfile)
                {
                    case "REST":
                        _simulatedHr += (baselineHr - _simulatedHr) * 0.25 + jitter;
                        break;
                    case "ANTICIPATION":
                        _simulatedHr += (baselineHr + 16 - _simulatedHr) * 0.25 + jitter;
                        break;
                    case "EXPOSURE_PEAK":
                        _simulatedHr += (baselineHr + 42 - _simulatedHr) * 0.30 + jitter * 1.3;
                        break;
                    case "RELIEF_DECEL":
                        _simulatedHr = Math.Max(baselineHr + 3, _simulatedHr - 0.85 + jitter * 0.4);
                        break;
                }

                int hr = (int)Math.Round(Math.Clamp(_simulatedHr, 48, 185));
                double baseRr = 60000.0 / hr;
                double rsaNoise = SimulatorProfile == "EXPOSURE_PEAK"
                    ? (rng.NextDouble() - 0.5) * 14.0
                    : (rng.NextDouble() - 0.5) * 40.0;
                int rrMs = (int)Math.Round(baseRr + rsaNoise);

                HeartRateUpdated?.Invoke(this, new HeartRatePacketEventArgs
                {
                    HeartRateBpm = hr,
                    RrIntervalsMs = new[] { rrMs },
                    TimestampUtc = DateTime.UtcNow
                });

                await Task.Delay(1000, token);
            }
        }, token);
    }

    public void StopSimulator()
    {
        IsSimulating = false;
        if (_simulatorCts != null)
        {
            _simulatorCts.Cancel();
            _simulatorCts.Dispose();
            _simulatorCts = null;
        }
    }
}
