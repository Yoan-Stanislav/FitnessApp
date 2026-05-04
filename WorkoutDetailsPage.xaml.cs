using FitnessApp.Models;
using FitnessApp.Services;
using FitnessApp.ViewModels;
using Microsoft.Maui.Controls;

namespace FitnessApp;

public partial class WorkoutDetailsPage : ContentPage
{
	private readonly WorkoutDetailsViewModel _viewModel;

	public WorkoutDetailsPage(Workout workout, DatabaseService databaseService)
	{
		InitializeComponent();
		_viewModel = new WorkoutDetailsViewModel(workout, databaseService);
		BindingContext = _viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.LoadExercisesAsync();
		
		foreach (var exercise in _viewModel.Exercises)
		{
			exercise.IsActive = false;
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
	}

	public void ScrollToExercise(Exercise exercise)
	{
		// Get the currently visible CollectionView
		CollectionView targetCollection = _viewModel.IsListView ? ListView : GridView;
		if (targetCollection != null)
		{
			var index = _viewModel.Exercises.IndexOf(exercise);
			if (index >= 0)
			{
				targetCollection.ScrollTo(index, position: ScrollToPosition.Start, animate: true);
			}
		}
	}

	private void OnDragStarting(object sender, DragStartingEventArgs e)
	{
		if (sender is Element element && element.BindingContext is Exercise exercise)
		{
			e.Data.Properties.Add("Item", element.BindingContext);
		}
	}

	private void OnDrop(object sender, DropEventArgs e)
	{
		if (sender is Element element &&
			e.Data.Properties.TryGetValue("Item", out var item) &&
			item is Exercise source &&
			element.BindingContext is Exercise target)
		{
			_viewModel.ReorderExercises(source, target);
		}
	}

	private void OnExerciseNotesUnfocused(object sender, FocusEventArgs e)
	{
		if (sender is Editor editor && editor.BindingContext is Exercise exercise)
		{
			_viewModel.AutoSaveExerciseNotesCommand.Execute(exercise);
		}
	}
}
