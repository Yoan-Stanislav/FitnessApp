using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FitnessApp.Models;

public class Exercise : INotifyPropertyChanged
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }

	[Indexed]
	public int WorkoutId { get; set; }

	public string Name { get; set; } = string.Empty;

	public int Sets { get; set; }

	public int Reps { get; set; }

	public int SequenceNumber { get; set; }

	public int RestTimeInSeconds { get; set; } = 10;

	private string _notes = string.Empty;
	public string Notes
	{
		get => _notes;
		set
		{
			if (_notes != value)
			{
				_notes = value;
				OnPropertyChanged();
			}
		}
	}

	private bool _isActive;
	public bool IsActive 
	{ 
		get => _isActive;
		set
		{
			if (_isActive != value)
			{
				_isActive = value;
				OnPropertyChanged();
			}
		}
	}

	private TimeSpan _activeTime;
	public TimeSpan ActiveTime 
	{ 
		get => _activeTime;
		set
		{
			if (_activeTime != value)
			{
				_activeTime = value;
				OnPropertyChanged();
			}
		}
	}

	private bool _isCompleted;
	public bool IsCompleted 
	{ 
		get => _isCompleted;
		set
		{
			if (_isCompleted != value)
			{
				_isCompleted = value;
				OnPropertyChanged();
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
