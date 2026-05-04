using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FitnessApp.Services;
using FitnessApp.ViewModels;
using SQLitePCL;
using Plugin.LocalNotification;

namespace FitnessApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		Batteries_V2.Init();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseLocalNotification()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<DatabaseService>();
		builder.Services.AddTransient<MainViewModel>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddSingleton<App>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
