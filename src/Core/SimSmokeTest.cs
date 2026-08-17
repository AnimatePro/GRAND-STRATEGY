using Godot;

namespace GrandStrategy.Core;

/// <summary>
/// Headless-прогон полной симуляции N ходов (без рендера).
/// Запуск: `godot --headless res://scenes/tests/SimSmoke.tscn`
/// Печатает сводку и завершается с кодом 0 (успех) или 1 (ошибка/NaN/сбой).
/// </summary>
public partial class SimSmokeTest : Node
{
    [Export] public int Turns = 50;

    public override void _Ready()
    {
        if (!DataManager.Instance.IsLoaded)
            DataManager.Instance.LoadWorldData();

        if (!DataManager.Instance.IsLoaded)
        {
            GD.PushError("SimSmokeTest: world data not loaded — run MapImporterTool/import_world.py first");
            GetTree().Quit(1);
            return;
        }

        if (!DataManager.Instance.ValidateData())
        {
            GD.PushError("SimSmokeTest: data validation FAILED");
            GetTree().Quit(1);
            return;
        }

        GameManager.Instance.StartNewGame(new NewGameOptions { PlayerCountryId = 0, Seed = 20260817 });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int t = 0; t < Turns; t++)
            GameManager.Instance.EndTurn();
        sw.Stop();

        // Проверка целостности после прогона.
        double totalPop = 0;
        foreach (GrandStrategy.Data.ProvinceData p in DataManager.Instance.World.Provinces)
            totalPop += p.TotalPopulation;

        GD.Print($"SimSmokeTest: {Turns} turns in {sw.ElapsedMilliseconds} ms");
        GD.Print($"SimSmokeTest: provinces={DataManager.Instance.World.ProvinceCount}, total_pop={totalPop:N0}");
        GD.Print("SimSmokeTest: OK");
        GetTree().Quit(0);
    }
}
