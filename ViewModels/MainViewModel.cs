using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using FitnessApp.Models;
using FitnessApp.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using OpenAI.Chat;

namespace FitnessApp.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _databaseService;

    private const string OpenAiApiKey = ApiConfig.OpenAiApiKey;

    private const string OpenAiModel = "gpt-4o-mini";

    private static readonly string AiPrompt =
        """
        Генерирай тренировката и упражненията изцяло на български език.
        Върни САМО валиден JSON в следния формат:
        {
          "title": "кратко име на тренировка",
          "exercises": [
            { "name": "име на упражнение", "sets": 3, "reps": 10, "restTimeInSeconds": 15 }
          ]
        }
        Изисквания:
        - exercises трябва да съдържа между 6 и 8 упражнения.
        - Всички имена и текстове да са само на български.
        - Без markdown, без обяснения, без допълнителен текст.
        """;

    private bool _isAiBusy;

    private Workout? _selectedWorkout;

    public MainViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        AddWorkoutCommand = new Command(async () => await AddWorkoutAsync());
        GenerateAiWorkoutCommand = new Command(async () => await GenerateAiWorkoutAsync(), () => !IsAiBusy);
        
        // Зареждаме съществуващите тренировки при старт
        _ = LoadWorkoutsAsync();
    }

    public ObservableCollection<Workout> Workouts { get; } = new();

    public ICommand AddWorkoutCommand { get; }

    public ICommand GenerateAiWorkoutCommand { get; }

    public bool IsAiBusy
    {
        get => _isAiBusy;
        set
        {
            if (_isAiBusy == value)
                return;
            _isAiBusy = value;
            OnPropertyChanged();
            if (GenerateAiWorkoutCommand is Command cmd)
                cmd.ChangeCanExecute();
        }
    }

    /// <summary>Bound to the CollectionView selected row (single selection).</summary>
    public Workout? SelectedWorkout
    {
        get => _selectedWorkout;
        set
        {
            if (ReferenceEquals(_selectedWorkout, value))
                return;
            _selectedWorkout = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadWorkoutsAsync()
    {
        try
        {
            var workouts = await _databaseService.GetWorkoutsAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Workouts.Clear();
                foreach (var w in workouts)
                    Workouts.Add(w);
            });
        }
        catch (Exception ex)
        {
            var page = ResolveAlertPage();
            if (page is not null)
                await page.DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task AddWorkoutAsync()
    {
        var promptHost = ResolveAlertPage();
        if (promptHost is null)
            return;

        string result = await promptHost.DisplayPromptAsync("New Workout", "Enter workout name:");
        if (string.IsNullOrWhiteSpace(result))
            return;

        var newWorkout = new Workout
        {
            Name = result.Trim(),
            Date = DateTime.Now
        };

        try
        {
            await _databaseService.CreateWorkoutAsync(newWorkout);
            MainThread.BeginInvokeOnMainThread(() => Workouts.Add(newWorkout));
        }
        catch (Exception ex)
        {
            await promptHost.DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task GenerateAiWorkoutAsync()
    {
        if (IsAiBusy) return;

        var alertHost = ResolveAlertPage();
        if (alertHost is null) return;

        var userInput = await alertHost.DisplayPromptAsync(
            "AI Треньор",
            "Какво искаш да тренираш? (напр. гърди, крака...)",
            "Генерирай",
            "Отказ");
        if (userInput is null)
            return;

        IsAiBusy = true;
        try
        {
            // Свързваме се с ChatGPT
            var chatClient = new ChatClient(OpenAiModel, OpenAiApiKey);
            var prompt = BuildAiPrompt(userInput);
            var completionResult = await chatClient.CompleteChatAsync([new UserChatMessage(prompt)]).ConfigureAwait(false);

            var aiText = ExtractAssistantText(completionResult.Value);
            var aiWorkout = ParseAiWorkout(aiText);
            if (aiWorkout is null || string.IsNullOrWhiteSpace(aiWorkout.Title))
                throw new InvalidOperationException("AI did not return a valid workout.");

            var workout = new Workout
            {
                Name = aiWorkout.Title.Trim(),
                Date = DateTime.Now
            };

            // Save workout first to get its Id.
            await _databaseService.CreateWorkoutAsync(workout).ConfigureAwait(false);

            var generatedExercises = BuildExercisesFromAi(aiWorkout, workout.Id);
            foreach (var exercise in generatedExercises)
            {
                await _databaseService.CreateExerciseAsync(exercise).ConfigureAwait(false);
            }

            // Update list in UI.
            MainThread.BeginInvokeOnMainThread(() => Workouts.Add(workout));
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (ResolveAlertPage() is Page host)
                    await host.DisplayAlert("AI Coach Error", ex.Message, "OK");
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsAiBusy = false);
        }
    }

    private static string ExtractAssistantText(ChatCompletion completion)
    {
        foreach (var part in completion.Content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrWhiteSpace(part.Text))
                return part.Text.Trim();
        }
        return string.Empty;
    }

    private static AiWorkoutResponse? ParseAiWorkout(string aiText)
    {
        if (string.IsNullOrWhiteSpace(aiText))
            return null;

        var normalized = aiText.Trim();
        if (normalized.StartsWith("```"))
        {
            var lines = normalized.Split('\n');
            normalized = string.Join('\n', lines.Skip(1)).Trim();
            if (normalized.EndsWith("```"))
                normalized = normalized[..^3].Trim();
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<AiWorkoutResponse>(normalized, options);
    }

    private static List<Exercise> BuildExercisesFromAi(AiWorkoutResponse aiWorkout, int workoutId)
    {
        var items = aiWorkout.Exercises?
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Take(8)
            .ToList() ?? [];

        if (items.Count < 6)
        {
            var fallbackNames = new[] { "Клекове", "Лицеви опори", "Планк", "Напади", "Глутеус мост", "Коремни преси" };
            for (int i = items.Count; i < 6; i++)
            {
                items.Add(new AiExerciseResponse { Name = fallbackNames[i], Sets = 3, Reps = 10, RestTimeInSeconds = 60 });
            }
        }

        var result = new List<Exercise>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            result.Add(new Exercise
            {
                WorkoutId = workoutId,
                Name = item.Name!.Trim(),
                Sets = item.Sets.GetValueOrDefault(3) > 0 ? item.Sets!.Value : 3,
                Reps = item.Reps.GetValueOrDefault(10) > 0 ? item.Reps!.Value : 10,
                RestTimeInSeconds = item.RestTimeInSeconds.GetValueOrDefault(60) > 0 ? item.RestTimeInSeconds!.Value : 60,
                SequenceNumber = i + 1
            });
        }

        return result;
    }

    private string BuildAiPrompt(string userInput)
    {
        return $"""
        {AiPrompt}

        Потребителят има специфично изискване за тренировката: "{userInput.Trim()}".
        Съобрази упражненията изцяло с това изискване.
        """;
    }

    private static Page? ResolveAlertPage()
    {
        return Application.Current?.MainPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
    }

    [RelayCommand]
    private void DeleteWorkout(Workout workout)
    {
        if (workout is null)
            return;

        MainThread.BeginInvokeOnMainThread(() => Workouts.Remove(workout));
    }

    [RelayCommand]
    private async Task WorkoutTapped(Workout workout)
    {
        if (workout is null)
            return;

        var page = ResolveAlertPage();
        if (page is null)
            return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await page.Navigation.PushAsync(new WorkoutDetailsPage(workout, _databaseService));
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class AiWorkoutResponse
    {
        public string? Title { get; set; }
        public List<AiExerciseResponse>? Exercises { get; set; }
    }

    private sealed class AiExerciseResponse
    {
        public string? Name { get; set; }
        public int? Sets { get; set; }
        public int? Reps { get; set; }
        public int? RestTimeInSeconds { get; set; }
    }
}