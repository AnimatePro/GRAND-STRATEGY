using System;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;

namespace GrandStrategy.UI;

/// <summary>
/// Экран новой игры: дата, сложность, выбор страны (поиск), зерно, ironman.
/// По кнопке «Начать» — стартует партию и переходит в игровую сцену.
/// </summary>
public partial class NewGameSetup : Control
{
    private OptionButton _yearDropdown = null!;
    private OptionButton _difficultyDropdown = null!;
    private OptionButton _countryDropdown = null!;
    private LineEdit _countrySearch = null!;
    private CheckBox _ironman = null!;
    private LineEdit _seedInput = null!;

    private CountryData[] _countries = Array.Empty<CountryData>();

    public override void _Ready()
    {
        _countries = CollectCountries();

        AddChild(UiKit.Background());
        VBoxContainer col = UiKit.CenterColumn(this, 620f);
        col.AddChild(UiKit.Title(L("NEWGAME_TITLE")));

        // Дата.
        col.AddChild(UiKit.Label(L("NEWGAME_DATE"), 14));
        _yearDropdown = UiKit.Dropdown(new[]
        {
            new UiKit.DropdownOption("2024", 2024),
            new UiKit.DropdownOption("1936", 1936),
            new UiKit.DropdownOption("1914", 1914),
            new UiKit.DropdownOption("1815", 1815),
        }, 0);
        col.AddChild(_yearDropdown);

        // Сложность.
        col.AddChild(UiKit.Label(L("NEWGAME_DIFFICULTY"), 14));
        _difficultyDropdown = UiKit.Dropdown(new[]
        {
            new UiKit.DropdownOption(L("DIFF_VERY_EASY"), 0),
            new UiKit.DropdownOption(L("DIFF_EASY"), 1),
            new UiKit.DropdownOption(L("DIFF_NORMAL"), 2),
            new UiKit.DropdownOption(L("DIFF_HARD"), 3),
            new UiKit.DropdownOption(L("DIFF_VERY_HARD"), 4),
        }, 2);
        col.AddChild(_difficultyDropdown);

        // Страна.
        col.AddChild(UiKit.Label(L("NEWGAME_COUNTRY"), 14));
        _countrySearch = new LineEdit { PlaceholderText = L("NEWGAME_SEARCH"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _countrySearch.TextChanged += _ => RebuildCountryList(_countrySearch.Text);
        col.AddChild(_countrySearch);

        _countryDropdown = new OptionButton();
        _countryDropdown.AddThemeFontSizeOverride("font_size", 15);
        col.AddChild(_countryDropdown);
        RebuildCountryList("");

        // Зерно.
        col.AddChild(UiKit.Label(L("NEWGAME_SEED"), 14));
        _seedInput = new LineEdit
        {
            Text = new Random().NextInt64(1, int.MaxValue).ToString(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        col.AddChild(_seedInput);

        // Ironman.
        _ironman = new CheckBox { Text = L("NEWGAME_IRONMAN") };
        _ironman.AddThemeFontSizeOverride("font_size", 15);
        col.AddChild(_ironman);

        // Кнопки.
        var start = UiKit.Button(L("NEWGAME_START"), StartGame);
        col.AddChild(start);
        col.AddChild(UiKit.Button(L("MENU_BACK"), () =>
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn")));
    }

    private CountryData[] CollectCountries()
    {
        if (!DataManager.Instance.IsLoaded)
            return Array.Empty<CountryData>();
        var list = new System.Collections.Generic.List<CountryData>();
        foreach (CountryData c in DataManager.Instance.World.Countries)
            if (c != null && c.IsAlive)
                list.Add(c);
        list.Sort((a, b) => string.Compare(a.NameKey, b.NameKey, StringComparison.Ordinal));
        return list.ToArray();
    }

    private void RebuildCountryList(string filter)
    {
        _countryDropdown.Clear();
        filter = filter.Trim().ToLowerInvariant();
        foreach (CountryData c in _countries)
        {
            string label = $"{c.NameKey} ({c.Code})";
            if (filter.Length == 0 ||
                c.NameKey.ToLowerInvariant().Contains(filter) ||
                c.Code.ToLowerInvariant().Contains(filter))
            {
                _countryDropdown.AddItem(label, c.Id);
            }
        }
        if (_countryDropdown.ItemCount > 0)
            _countryDropdown.Select(0);
    }

    private void StartGame()
    {
        int countryId = _countryDropdown.ItemCount > 0
            ? (int)_countryDropdown.GetItemId(_countryDropdown.Selected)
            : 0;

        var options = new NewGameOptions
        {
            StartYear = (int)_yearDropdown.GetItemId(_yearDropdown.Selected),
            PlayerCountryId = countryId,
            Seed = ParseSeed(_seedInput.Text),
            Difficulty = _difficultyDropdown.GetItemId(_difficultyDropdown.Selected).ToString(),
            Ironman = _ironman.ButtonPressed,
        };

        GameManager.Instance.StartNewGame(options);
        GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
    }

    private static long ParseSeed(string text)
    {
        return long.TryParse(text, out long v) ? v : new Random().NextInt64(1, int.MaxValue);
    }

    private static string L(string key) => LocalizationManager.Instance.Get(key);
}
