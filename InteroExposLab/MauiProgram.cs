using InteroExposLab.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace InteroExposLab;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        builder.Services.AddSingleton<BleHeartRateService>();
        builder.Services.AddSingleton<HeartRateBiofeedbackEngine>();
        builder.Services.AddSingleton<ExcelCsvExportService>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
