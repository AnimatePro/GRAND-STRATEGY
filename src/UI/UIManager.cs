using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Economy;

namespace GrandStrategy.UI;

/// <summary>
/// Менеджер UI (Autoload #15). Программно строит HUD: верхнюю панель (дата, казна,
/// доход/расход, ВВП, население, стабильность, управление скоростью, конец хода),
/// панели провинции/страны и всплывающие уведомления. Обновляется по сигналам EventBus.
/// </summary>
public partial class UIManager : Node
{
    public static UIManager Instance { get; private set; } = null!;

    private CanvasLayer _canvas = null!;
    private PanelContainer _topBar = null!;
    private Label _dateLabel = null!;
    private Label _treasuryLabel = null!;
    private Label _incomeLabel = null!;
    private Label _gdpLabel = null!;
    private Label _popLabel = null!;
    private Label _stabilityLabel = null!;

    private PanelContainer _provincePanel = null!;
    private Label _provinceText = null!;
    private PanelContainer _countryPanel = null!;
    private Label _countryText = null!;

    private Label _toast = null!;

    public override void _Ready()
    {
        Instance = this;
        EventBus.Instance.GameStarted += OnGameStarted;
        EventBus.Instance.TurnEnded += _ => UpdateTopBar();
        EventBus.Instance.EconomyUpdated += UpdateTopBar;
        EventBus.Instance.ProvinceSelected += OnProvinceSelected;
        EventBus.Instance.CountrySelected += OnCountrySelected;
        EventBus.Instance.UINotification += ShowToast;
    }

    private void OnGameStarted()
    {
        if (_canvas == null)
            BuildHud();
        UpdateTopBar();
    }

    private void BuildHud()
    {
        _canvas = new CanvasLayer();
        AddChild(_canvas);

        // --- Верхняя панель ---
        _topBar = new PanelContainer();
        _topBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _canvas.AddChild(_topBar);

        var topHBox = new HBoxContainer();
        topHBox.AddThemeConstantOverride("separation", 16);
        _topBar.AddChild(topHBox);

        _dateLabel = MakeLabel(topHBox, "");
        _treasuryLabel = MakeLabel(topHBox, "");
        _incomeLabel = MakeLabel(topHBox, "");
        _gdpLabel = MakeLabel(topHBox, "");
        _popLabel = MakeLabel(topHBox, "");
        _stabilityLabel = MakeLabel(topHBox, "");

        var pauseBtn = new Button { Text = "⏸" };
        pauseBtn.Pressed += () => GameManager.Instance.Pause();
        topHBox.AddChild(pauseBtn);

        var speedMinus = new Button { Text = "-" };
        speedMinus.Pressed += () => TimeManager.Instance.SetSpeed(TimeManager.Instance.Speed - 1);
        topHBox.AddChild(speedMinus);

        var speedPlus = new Button { Text = "+" };
        speedPlus.Pressed += () => TimeManager.Instance.SetSpeed(TimeManager.Instance.Speed + 1);
        topHBox.AddChild(speedPlus);

        var endTurnBtn = new Button { Text = "END TURN" };
        endTurnBtn.Pressed += () => GameManager.Instance.EndTurn();
        topHBox.AddChild(endTurnBtn);

        // --- Панель провинции (слева) ---
        _provincePanel = MakePanel(8, 80, 360, 160);
        _provinceText = new Label();
        _provincePanel.AddChild(_provinceText);

        // --- Панель страны (слева, ниже) ---
        _countryPanel = MakePanel(8, 260, 380, 200);
        _countryText = new Label();
        _countryPanel.AddChild(_countryText);

        // --- Тост-уведомление (низ) ---
        _toast = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _toast.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _toast.OffsetTop = -40;
        _canvas.AddChild(_toast);
    }

    private Label MakeLabel(HBoxContainer parent, string text)
    {
        var label = new Label { Text = text };
        parent.AddChild(label);
        return label;
    }

    private PanelContainer MakePanel(float left, float top, float right, float bottom)
    {
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        panel.OffsetLeft = left;
        panel.OffsetTop = top;
        panel.OffsetRight = right;
        panel.OffsetBottom = bottom;
        _canvas.AddChild(panel);
        return panel;
    }

    private void UpdateTopBar()
    {
        if (_dateLabel == null)
            return;

        WorldData world = DataManager.Instance.World;
        int playerId = PlayerCountryId();
        CountryData country = world.Countries.Length > 0 ? world.GetCountry(playerId) : null;
        CountryEconomy eco = EconomyManager.Instance.Economy.Countries.Length > playerId
            ? EconomyManager.Instance.Economy.Countries[playerId] : null;

        _dateLabel.Text = TimeManager.Instance.CurrentDateString;
        _treasuryLabel.Text = country != null ? $"💰 {country.Treasury:N0}" : "";
        _incomeLabel.Text = eco != null ? $"{(eco.BudgetRevenue - eco.BudgetExpenses) >= 0 ? "+" : ""}{eco.BudgetRevenue - eco.BudgetExpenses:N0}/d" : "";
        _gdpLabel.Text = country != null ? $"GDP {country.Gdp:N0}" : "";
        _popLabel.Text = country != null ? $"👥 {country.Population:N0}" : "";
        _stabilityLabel.Text = country != null ? $"⚖ {country.Stability:0}%" : "";
    }

    private void OnProvinceSelected(int provinceId)
    {
        if (_provinceText == null || !DataManager.Instance.IsLoaded)
            return;
        ProvinceData p = DataManager.Instance.World.GetProvince(provinceId);
        _provinceText.Text =
            $"Province #{p.Id}\n" +
            $"Pop: {p.TotalPopulation:N0}\n" +
            $"  Adults: {p.MaleAdults + p.FemaleAdults:N0} (M {p.MaleAdults:N0} / F {p.FemaleAdults:N0})\n" +
            $"  Children: {p.MaleChildren + p.FemaleChildren:N0}\n" +
            $"  Seniors: {p.MaleSeniors + p.FemaleSeniors:N0}\n" +
            $"Terrain: {p.Terrain}  Climate: {p.Climate}\n" +
            $"Infra: {p.Infrastructure:P0}  Dev: {p.Development:P0}\n" +
            $"Neighbors: {p.NeighborIds.Length}";
    }

    private void OnCountrySelected(int countryId)
    {
        if (_countryText == null || !DataManager.Instance.IsLoaded)
            return;
        CountryData c = DataManager.Instance.World.GetCountry(countryId);
        if (c == null)
            return;
        CountryEconomy eco = EconomyManager.Instance.Economy.Countries[countryId];
        _countryText.Text =
            $"{c.NameKey} ({c.Code})\n" +
            $"Gov: {c.GovernmentType}  Ideology: {c.Ideology}\n" +
            $"GDP: {c.Gdp:N0}  Pop: {c.Population:N0}\n" +
            $"Treasury: {c.Treasury:N0}  Debt: {c.Debt:N0}\n" +
            $"Inflation: {(eco != null ? eco.Inflation : 0) * 100:0.0}%\n" +
            $"Stability: {c.Stability:0}  Legitimacy: {c.Legitimacy:0}\n" +
            $"WarExhaustion: {c.WarExhaustion:0}";
    }

    private void ShowToast(string text)
    {
        if (_toast == null)
            return;
        _toast.Text = text;
        _toast.Visible = true;
        GetTree().CreateTimer(4.0).Timeout += () => { if (_toast != null) _toast.Visible = false; };
    }

    private int PlayerCountryId() => GameManager.Instance.ActiveOptions?.PlayerCountryId ?? 0;
}
