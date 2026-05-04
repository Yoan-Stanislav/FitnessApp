using FitnessApp.Models;
using FitnessApp.Services;
using FitnessApp.ViewModels;

namespace FitnessApp;

public partial class MainPage : ContentPage
{
	private readonly MainViewModel _viewModel;
	private readonly DatabaseService _databaseService;

	public MainPage(MainViewModel viewModel, DatabaseService databaseService)
	{
		InitializeComponent();
		_viewModel = viewModel;
		_databaseService = databaseService;
		BindingContext = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.LoadWorkoutsAsync();
	}

	private async void OnWorkoutSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is not Workout workout)
			return;

		if (sender is CollectionView cv)
			cv.SelectedItem = null;
		_viewModel.SelectedWorkout = null;

		await Navigation.PushAsync(new WorkoutDetailsPage(workout, _databaseService));
	}
}
