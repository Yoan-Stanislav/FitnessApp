using SQLite;
using FitnessApp.Models;

namespace FitnessApp.Services;

public class DatabaseService
{
	private SQLiteAsyncConnection? _database;

	private async Task<SQLiteAsyncConnection> GetConnectionAsync()
	{
		if (_database is not null)
			return _database;

		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "fitness.db3");
		_database = new SQLiteAsyncConnection(
			dbPath,
			SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

		await _database.CreateTableAsync<Workout>();
		await _database.CreateTableAsync<Exercise>();
		await EnsureExerciseNotesColumnAsync(_database);
		return _database;
	}

	private static async Task EnsureExerciseNotesColumnAsync(SQLiteAsyncConnection db)
	{
		var hasNotesColumn = await db.ExecuteScalarAsync<int>(
			"SELECT COUNT(*) FROM pragma_table_info('Exercise') WHERE name = 'Notes';");

		if (hasNotesColumn == 0)
		{
			await db.ExecuteAsync("ALTER TABLE Exercise ADD COLUMN Notes TEXT NOT NULL DEFAULT '';");
		}
	}

	public async Task<int> CreateWorkoutAsync(Workout workout)
	{
		var db = await GetConnectionAsync();
		return await db.InsertAsync(workout);
	}

	public async Task<List<Workout>> GetWorkoutsAsync()
	{
		var db = await GetConnectionAsync();
		var list = await db.Table<Workout>().ToListAsync();
		return list.OrderByDescending(w => w.Date).ToList();
	}

	public async Task<List<Exercise>> GetExercisesForWorkoutAsync(int workoutId)
	{
		var db = await GetConnectionAsync();
		var list = await db.Table<Exercise>().Where(e => e.WorkoutId == workoutId).ToListAsync();
		return list.OrderBy(e => e.Name).ToList();
	}

	public async Task<int> CreateExerciseAsync(Exercise exercise)
	{
		var db = await GetConnectionAsync();
		return await db.InsertAsync(exercise);
	}

	public async Task<int> DeleteExerciseAsync(Exercise exercise)
	{
		var db = await GetConnectionAsync();
		return await db.DeleteAsync(exercise);
	}

	public async Task<int> UpdateExerciseAsync(Exercise exercise)
	{
		var db = await GetConnectionAsync();
		return await db.UpdateAsync(exercise);
	}

	public async Task<int> UpdateWorkoutAsync(Workout workout)
	{
		var db = await GetConnectionAsync();
		return await db.UpdateAsync(workout);
	}
	public async Task<int> DeleteWorkoutAsync(Workout workout)
    {
        var db = await GetConnectionAsync();
        
        var exercisesToDelete = await db.Table<Exercise>().Where(e => e.WorkoutId == workout.Id).ToListAsync();
        foreach (var exercise in exercisesToDelete)
        {
            await db.DeleteAsync(exercise);
        }

        // триемсамата тренировка
        return await db.DeleteAsync(workout);
    }
}
