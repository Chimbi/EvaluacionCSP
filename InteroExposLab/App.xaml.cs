using Microsoft.Maui.Controls;

namespace InteroExposLab;

public partial class App : Application
{
    public App(MainPage mainPage)
    {
        InitializeComponent();
        MainPage = new NavigationPage(mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#0B0F19"),
            BarTextColor = Colors.White
        };
    }
}
