using SQLite;

namespace FitnessApp.Models;

public class Workout
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public DateTime Date { get; set; }

	public string Notes { get; set; } = string.Empty;
}
