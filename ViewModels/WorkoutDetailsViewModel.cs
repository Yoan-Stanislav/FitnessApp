using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using FitnessApp.Models;
using FitnessApp.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;



namespace FitnessApp.ViewModels;

public partial class WorkoutDetailsViewModel : INotifyPropertyChanged
{
	private readonly DatabaseService _databaseService;
	private int _currentIndex = -1;
	private bool _isWorkoutRunning = false;
	private IDispatcherTimer? _globalTimer;
	private int _restSeconds = 0;
	private bool _isResting = false;
	private bool _isGridView;
	private readonly Stack<(Exercise exercise, int originalIndex)> _historyStack = new();
	private string _notes;
	private Exercise? _currentExercise;
	private Exercise? _draggedExercise;
	private TimeSpan _totalWorkoutDuration = TimeSpan.Zero;
	private DateTime _workoutStartTime;
	private DateTime _exerciseStartTime;

	public WorkoutDetailsViewModel(Workout workout, DatabaseService databaseService)
	{
		Workout = workout;
		_notes = workout.Notes;
		_databaseService = databaseService;
		
		// Initialize global timer
		_globalTimer = Application.Current?.Dispatcher.CreateTimer();
		if (_globalTimer != null)
		{
			_globalTimer.Interval = TimeSpan.FromSeconds(1);
			_globalTimer.Tick += OnGlobalTimerTick;
		}
		
		// Ensure all exercises have IsActive = false initially
		foreach (var exercise in Exercises)
		{
			exercise.IsActive = false;
		}
		
		AddExerciseCommand = new Command(async () => await AddExerciseAsync());
		SkipRestCommand = new Command(SkipRest);
		StartWorkoutCommand = new Command(StartWorkout);
		FinishSetCommand = new Command<Exercise>(CompleteExercise);
		DragStartingCommand = new Command<Exercise>(DragStarting);
		DropCommand = new Command<Exercise>(Drop);
		UndoDeleteCommand = new Command(async () => await UndoDeleteAsync());
		UndoCommand = new Command(async () => await UndoAsync());
		SaveNotesCommand = new Command(async () => await SaveNotesAsync());
		ToggleLayoutCommand = new Command(ToggleLayout);
	}

	public event PropertyChangedEventHandler PropertyChanged;

	protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	public Workout Workout { get; }

	public ObservableCollection<Exercise> Exercises { get; } = new();

	public ICommand AddExerciseCommand { get; }
	public ICommand SkipRestCommand { get; }
	public ICommand StartWorkoutCommand { get; }
	public ICommand FinishSetCommand { get; }
	public ICommand DragStartingCommand { get; }
	public ICommand DropCommand { get; }
	public ICommand UndoDeleteCommand { get; }
	public ICommand UndoCommand { get; }
	public ICommand SaveNotesCommand { get; }
	public ICommand ToggleLayoutCommand { get; }

	public bool IsGridView
	{
		get => _isGridView;
		set
		{
			_isGridView = value;
			OnPropertyChanged(nameof(IsGridView));
			OnPropertyChanged(nameof(IsListView));
		}
	}

	public bool IsListView => !IsGridView;

	public string Notes
	{
		get => _notes;
		set
		{
			_notes = value;
			OnPropertyChanged();
		}
	}

	public bool HasDeletedExercises => _historyStack.Count > 0;

	public int RestSeconds
	{
		get => _restSeconds;
		set
		{
			_restSeconds = value;
			OnPropertyChanged();
		}
	}

	public bool IsResting
	{
		get => _isResting;
		set
		{
			_isResting = value;
			OnPropertyChanged();
		}
	}

	public int CurrentIndex
	{
		get => _currentIndex;
		set
		{
			_currentIndex = value;
			OnPropertyChanged();
		}
	}

	public bool IsWorkoutRunning
	{
		get => _isWorkoutRunning;
		set
		{
			_isWorkoutRunning = value;
			OnPropertyChanged();
		}
	}

	public bool IsWorkoutNotRunning
	{
		get => !IsWorkoutRunning;
	}

	public TimeSpan TotalWorkoutDuration
	{
		get => _totalWorkoutDuration;
		set
		{
			_totalWorkoutDuration = value;
			OnPropertyChanged();
		}
	}

	public async Task LoadExercisesAsync()
	{
		try
		{
			// Refresh workout data including notes from database
			await RefreshWorkoutData();
			
			var list = await _databaseService.GetExercisesForWorkoutAsync(Workout.Id);
			list = list.OrderBy(e => e.SequenceNumber).ToList();
			MainThread.BeginInvokeOnMainThread(() =>
			{
				Exercises.Clear();
				foreach (var exercise in list)
					Exercises.Add(exercise);
			});
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.Message);
		}
	}

	private async Task RefreshWorkoutData()
	{
		try
		{
			// Get fresh workout data from database to refresh notes
			var workouts = await _databaseService.GetWorkoutsAsync();
			var freshWorkout = workouts.FirstOrDefault(w => w.Id == Workout.Id);
			if (freshWorkout != null)
			{
				Workout.Notes = freshWorkout.Notes;
				// Update the Notes property to trigger UI update
				OnPropertyChanged(nameof(Notes));
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to refresh workout data: {ex.Message}");
		}
	}

	private async Task AddExerciseAsync()
	{
		var host = Application.Current?.MainPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (host is null)
			return;

		var name = await host.DisplayPromptAsync("Exercise", "Exercise name:");
		if (string.IsNullOrWhiteSpace(name))
			return;

		var setsText = await host.DisplayPromptAsync("Sets", "Number of sets:", initialValue: "3", maxLength: 4, keyboard: Keyboard.Numeric);
		var repsText = await host.DisplayPromptAsync("Reps", "Reps per set:", initialValue: "10", maxLength: 4, keyboard: Keyboard.Numeric);

		var sets = int.TryParse(setsText, out var s) ? Math.Max(1, s) : 3;
		var reps = int.TryParse(repsText, out var r) ? Math.Max(1, r) : 10;

		var exercise = new Exercise
		{
			WorkoutId = Workout.Id,
			Name = name.Trim(),
			Sets = sets,
			Reps = reps,
			SequenceNumber = Exercises.Count + 1
		};

		try
		{
			await _databaseService.CreateExerciseAsync(exercise);
		}
		catch (Exception ex)
		{
			await host.DisplayAlert("Error", ex.Message, "OK");
			return;
		}

		MainThread.BeginInvokeOnMainThread(() => Exercises.Add(exercise));
	}

	#region Workout Flow Methods

	private void OnGlobalTimerTick(object? sender, EventArgs e)
	{
		if (_isWorkoutRunning)
		{
			// Increment total workout duration by 1 second
			TotalWorkoutDuration = TotalWorkoutDuration.Add(TimeSpan.FromSeconds(1));
		}
		
		if (_isResting && _restSeconds > 0)
		{
			_restSeconds--;
			OnPropertyChanged(nameof(RestSeconds));
			
			if (_restSeconds == 0)
			{
				// Rest finished - move to next exercise
				CompleteRest();
			}
		}
		else if (_isWorkoutRunning && !_isResting && _currentExercise != null)
		{
			// Increment ActiveTime of currently active exercise by 1 second
			_currentExercise.ActiveTime = _currentExercise.ActiveTime.Add(TimeSpan.FromSeconds(1));
			// Exercise model handles its own OnPropertyChanged
		}
	}

	

private void CompleteRest()
{
	
    _isResting = false;
    OnPropertyChanged(nameof(IsResting));
    
    try
    {
        Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(1000)); // 1 секунда вибрация
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Vibration error: {ex.Message}");
    }
	_currentIndex++;
    
    if (_currentIndex < Exercises.Count)
    {
        foreach (var exercise in Exercises)
        {
            exercise.IsActive = false;
        }
        Exercises[_currentIndex].IsActive = true;
        _currentExercise = Exercises[_currentIndex];
        _exerciseStartTime = DateTime.Now; 
        OnPropertyChanged(nameof(Exercises));
        OnPropertyChanged(nameof(CurrentIndex));
    }
	else
    {
        CompleteWorkout();
    }
}

	public void StartWorkout()
	{
		if (_isWorkoutRunning || Exercises.Count == 0)
			return;
		
		_isWorkoutRunning = true;
		_currentIndex = 0;
		_workoutStartTime = DateTime.Now;
		_exerciseStartTime = DateTime.Now;
		_totalWorkoutDuration = TimeSpan.Zero;
		
		// Activate first exercise
		foreach (var exercise in Exercises)
		{
			exercise.IsActive = false;
			exercise.ActiveTime = TimeSpan.Zero;
		}
		Exercises[0].IsActive = true;
		_currentExercise = Exercises[0];
		
		// Start global timer
		_globalTimer?.Start();
		
		OnPropertyChanged(nameof(IsWorkoutRunning));
		OnPropertyChanged(nameof(IsWorkoutNotRunning));
		OnPropertyChanged(nameof(Exercises));
		OnPropertyChanged(nameof(CurrentIndex));
		OnPropertyChanged(nameof(TotalWorkoutDuration));
	}

	public void CompleteExercise(Exercise exercise)
	{
		if (!exercise.IsActive)
			return;
		
		// Set current exercise as inactive
		exercise.IsActive = false;
		exercise.IsCompleted = true;
		_currentExercise = null;
		
		// Start 15s rest
		_restSeconds = 15;
		_isResting = true;
		
		OnPropertyChanged(nameof(Exercises));
		OnPropertyChanged(nameof(IsResting));
		OnPropertyChanged(nameof(RestSeconds));
	}

	private void SkipRest()
	{
		if (_isResting)
		{
			// Call the SAME logic as timer finishing
			CompleteRest();
		}
	}

	private void CompleteWorkout()
	{
		_isWorkoutRunning = false;
		_globalTimer?.Stop();
		_isResting = false;
		_currentIndex = -1;
		_totalWorkoutDuration = TimeSpan.Zero;

		try
        {
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(1500));
        }
        catch { }
        
		
		// Reset all exercises
		foreach (var exercise in Exercises)
		{
			exercise.IsActive = false;
			exercise.IsCompleted = false;
			exercise.ActiveTime = TimeSpan.Zero;
		}
		
		// Show completion message
		var host = Application.Current?.MainPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (host != null)
		{
			host.DisplayAlert("Workout Finished!", "Great job! You completed the workout.", "OK");
		}
		
		OnPropertyChanged(nameof(IsWorkoutRunning));
		OnPropertyChanged(nameof(IsWorkoutNotRunning));
		OnPropertyChanged(nameof(Exercises));
		OnPropertyChanged(nameof(CurrentIndex));
		OnPropertyChanged(nameof(TotalWorkoutDuration));
	}

	#endregion

	#region Drag and Drop

	private void DragStarting(Exercise exercise)
	{
		_draggedExercise = exercise;
	}

	private void Drop(Exercise targetExercise)
	{
		if (_draggedExercise == null || targetExercise == null || _draggedExercise == targetExercise)
			return;

		// Get current positions
		var draggedIndex = Exercises.IndexOf(_draggedExercise);
		var targetIndex = Exercises.IndexOf(targetExercise);

		if (draggedIndex < 0 || targetIndex < 0)
			return;

		// Call ReorderExercises method
		ReorderExercises(draggedIndex, targetIndex);

		// Clear dragged exercise
		_draggedExercise = null;
	}

	private void ReorderExercises(int draggedIndex, int targetIndex)
	{
		Console.WriteLine($"=== REORDER EXERCISES START ===");
		Console.WriteLine($"Dragged Index: {draggedIndex}, Target Index: {targetIndex}");
		
		// Physically move the item in the ObservableCollection
		if (draggedIndex >= 0 && draggedIndex < Exercises.Count && 
			targetIndex >= 0 && targetIndex < Exercises.Count && 
			draggedIndex != targetIndex)
		{
			// Get the dragged exercise
			var draggedExercise = Exercises[draggedIndex];
			
			// Remove from original position
			Exercises.RemoveAt(draggedIndex);
			
			// Insert at new position
			Exercises.Insert(targetIndex, draggedExercise);
			
			Console.WriteLine($"REORDER: Moved {draggedExercise.Name} from index {draggedIndex} to {targetIndex}");

			RecalculateSequenceNumbers();
			OnPropertyChanged(nameof(Exercises));
			_ = PersistExerciseOrderAsync();
		}
		else
		{
			Console.WriteLine("REORDER: Invalid indices or same position - no action taken");
		}
		
		Console.WriteLine($"=== REORDER EXERCISES END ===");
	}

	public void ReorderExercises(Exercise source, Exercise target)
	{
		if (source == null || target == null || source == target)
			return;

		var oldIndex = Exercises.IndexOf(source);
		var newIndex = Exercises.IndexOf(target);

		if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
			return;

		Exercises.Move(oldIndex, newIndex);
		RecalculateSequenceNumbers();

		OnPropertyChanged(nameof(Exercises));
		_ = PersistExerciseOrderAsync();
	}

	private void RecalculateSequenceNumbers()
	{
		for (int i = 0; i < Exercises.Count; i++)
		{
			Exercises[i].SequenceNumber = i + 1;
		}
	}

	private async Task PersistExerciseOrderAsync()
	{
		try
		{
			for (int i = 0; i < Exercises.Count; i++)
			{
				await _databaseService.UpdateExerciseAsync(Exercises[i]);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to persist exercise order: {ex.Message}");
		}
	}

	#endregion

	#region Exercise Management

	[RelayCommand]
	private async Task DeleteExercise(Exercise exercise)
	{
		if (exercise == null)
			return;

		await DeleteExerciseAsync(exercise);
	}

	[RelayCommand]
	private async Task AutoSaveExerciseNotes(Exercise exercise)
	{
		if (exercise == null)
			return;

		try
		{
			await _databaseService.UpdateExerciseAsync(exercise);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to auto-save exercise notes: {ex.Message}");
		}
	}

	private async Task DeleteExerciseAsync(Exercise exercise)
	{
		try
		{
			var originalIndex = Exercises.IndexOf(exercise);
			if (originalIndex < 0)
				return;

			// Add to history stack with original position
			_historyStack.Push((exercise, originalIndex));

			// Remove from UI
			Exercises.Remove(exercise);
			OnPropertyChanged(nameof(HasDeletedExercises));
			
			// Delete from database
			await _databaseService.DeleteExerciseAsync(exercise);
		}
		catch (Exception ex)
		{
			var host = Application.Current?.Windows.FirstOrDefault()?.Page;
			if (host != null)
			{
				await host.DisplayAlert("Error", $"Failed to delete exercise: {ex.Message}", "OK");
			}
		}
	}

	private async Task UndoDeleteAsync()
	{
		if (_historyStack.Count == 0) return;
		
		try
		{
			var (exercise, originalIndex) = _historyStack.Pop();
			
			// Restore at original index; fallback to end if out of bounds
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (originalIndex >= 0 && originalIndex <= Exercises.Count)
				{
					Exercises.Insert(originalIndex, exercise);
				}
				else
				{
					Exercises.Add(exercise);
				}

				OnPropertyChanged(nameof(Exercises));
			});
			
			// Recreate in database
			await _databaseService.CreateExerciseAsync(exercise);
			
			OnPropertyChanged(nameof(HasDeletedExercises));
		}
		catch (Exception ex)
		{
			var host = Application.Current?.Windows.FirstOrDefault()?.Page;
			if (host != null)
			{
				await host.DisplayAlert("Error", $"Failed to undo delete: {ex.Message}", "OK");
			}
		}
	}

	private async Task SaveNotesAsync()
	{
		try
		{
			Workout.Notes = Notes;
			await _databaseService.UpdateWorkoutAsync(Workout);
		}
		catch (Exception ex)
		{
			var host = Application.Current?.Windows.FirstOrDefault()?.Page;
			if (host != null)
			{
				await host.DisplayAlert("Error", $"Failed to save notes: {ex.Message}", "OK");
			}
		}
	}

	private void ToggleLayout()
	{
		IsGridView = !IsGridView;
	}

	private async Task UndoAsync()
	{
		await UndoDeleteAsync();
	}

	#endregion
}
