using System;
using Godot;
using GrandStrategy.Core;
using GrandStrategy.Data;
using GrandStrategy.Systems.Decisions;
using GrandStrategy.Systems.Diplomacy;
using GrandStrategy.Systems.Economy;
using GrandStrategy.Systems.Governance;
using GrandStrategy.Systems.Military;
using GrandStrategy.Systems.Tech;

namespace GrandStrategy.UI;

/// <summary>
/// Менеджер UI (Autoload #17). Программно строит HUD и панели действий игрока:
/// верхняя панель (дата/казна/доход/ВВП/население/стабильность/скорость/конец хода),
/// панель провинции (инфо + действия: стройка/рекрут), панель игрока (налоги/правительство),
/// панель целевой страны (дипломатия/торговля), панели технологий и формируемых наций.
/// Обновление — по сигналам EventBus, без пофреймового опроса.
/// </summary>
public partial class UIManager : Node
{
    public static UIManager Instance { get; private set; } = null!;

    private CanvasLayer _canvas = null!;
    private Label _dateLabel = null!;
    private Label _treasuryLabel = null!;
    private Label _incomeLabel = null!;
    private Label _gdpLabel = null!;
    private Label _popLabel = null!;
    private Label _stabilityLabel = null!;

    private VBoxContainer _provinceBox = null!;
    private VBoxContainer _playerBox = null!;
    private VBoxContainer _targetBox = null!;
    private VBoxContainer _techBox = null!;

    private Label _toast = null!;

    private int _selectedProvince = -1;
    private int _targetCountry = -1;

    public override void _Ready()
    {
        Instance = this;
        EventBus.Instance.GameStarted += OnGameStarted;
        EventBus.Instance.TurnEnded += _ => UpdateTopBar();
        EventBus.Instance.EconomyUpdated += UpdateTopBar;
        EventBus.Instance.ProvinceSelected += OnProvinceSelected;
        EventBus.Instance.CountrySelected += _ => { };
        EventBus.Instance.UINotification += ShowToast;
        EventBus.Instance.DiplomacyUpdated += RebuildPanels;
        EventBus.Instance.TradeUpdated += RebuildPanels;
    }

    private void OnGameStarted()
    {
        EnsureHud();
        UpdateTopBar();
        RebuildPanels();
    }

    /// <summary>Строит HUD, если ещё не построен (вызывается из GameRoot после загрузки сцены).</summary>
    public void EnsureHud()
    {
        if (_canvas == null)
            BuildHud();
        _canvas.Visible = true;
        UpdateTopBar();
        RebuildPanels();
    }

    /// <summary>Скрывает HUD (при выходе из игровой сцены в меню).</summary>
    public void HideHud()
    {
        if (_canvas != null)
            _canvas.Visible = false;
    }

    private int PlayerId() => GameManager.Instance.ActiveOptions?.PlayerCountryId ?? 0;

    private static string L(string key) => LocalizationManager.Instance.Get(key);

    // --- Построение HUD ------------------------------------------------------

    private void BuildHud()
    {
        _canvas = new CanvasLayer();
        AddChild(_canvas);

        // Верхняя панель.
        var topBar = new PanelContainer();
        topBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _canvas.AddChild(topBar);

        var topHBox = new HBoxContainer();
        topHBox.AddThemeConstantOverride("separation", 12);
        topBar.AddChild(topHBox);

        _dateLabel = MakeLabel(topHBox);
        _treasuryLabel = MakeLabel(topHBox);
        _incomeLabel = MakeLabel(topHBox);
        _gdpLabel = MakeLabel(topHBox);
        _popLabel = MakeLabel(topHBox);
        _stabilityLabel = MakeLabel(topHBox);

        AddButton(topHBox, L("HUD_PAUSE"), () => GameManager.Instance.Pause());
        AddButton(topHBox, L("HUD_SPEED_DOWN"), () => TimeManager.Instance.SetSpeed(TimeManager.Instance.Speed - 1));
        AddButton(topHBox, L("HUD_SPEED_UP"), () => TimeManager.Instance.SetSpeed(TimeManager.Instance.Speed + 1));
        AddButton(topHBox, L("HUD_END_TURN"), () => GameManager.Instance.EndTurn());
        AddButton(topHBox, L("HUD_SAVE"), () => GameManager.Instance.SaveGame("manual"));
        AddButton(topHBox, L("HUD_MENU"), () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn"));

        // Панель провинции (слева сверху).
        _provinceBox = MakePanel(Control.LayoutPreset.TopLeft, new Vector2(8, 64), new Vector2(360, 260), L("PANEL_PROVINCE"));
        // Панель игрока (слева снизу).
        _playerBox = MakePanel(Control.LayoutPreset.TopLeft, new Vector2(8, 340), new Vector2(360, 640), L("PANEL_COUNTRY"));
        // Панель целевой страны (справа).
        _targetBox = MakePanel(Control.LayoutPreset.TopRight, new Vector2(-380, 64), new Vector2(-8, 420), L("PANEL_TARGET"));
        // Панель технологий (справа снизу).
        _techBox = MakePanel(Control.LayoutPreset.TopRight, new Vector2(-380, 430), new Vector2(-8, 700), L("PANEL_TECHNOLOGY"));

        // Тост.
        _toast = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _toast.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _toast.OffsetTop = -40;
        _canvas.AddChild(_toast);
    }

    private static Label MakeLabel(HBoxContainer parent)
    {
        var label = new Label();
        parent.AddChild(label);
        return label;
    }

    private static void AddButton(Container parent, string text, Action onPressed)
    {
        var btn = new Button { Text = text };
        btn.Pressed += () =>
        {
            AudioManager.Instance.PlayClick();
            onPressed();
        };
        parent.AddChild(btn);
    }

    private VBoxContainer MakePanel(Control.LayoutPreset preset, Vector2 offsetMin, Vector2 offsetMax, string title)
    {
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(preset);
        panel.OffsetLeft = offsetMin.X;
        panel.OffsetTop = offsetMin.Y;
        panel.OffsetRight = offsetMax.X;
        panel.OffsetBottom = offsetMax.Y;
        _canvas.AddChild(panel);

        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(vbox);

        var titleLabel = new Label { Text = title };
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(titleLabel);

        return vbox;
    }

    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
            child.QueueFree();
    }

    // --- Верхняя панель ------------------------------------------------------

    private void UpdateTopBar()
    {
        if (_dateLabel == null || !DataManager.Instance.IsLoaded)
            return;

        WorldData world = DataManager.Instance.World;
        int playerId = PlayerId();
        CountryData country = world.GetCountry(playerId);
        CountryEconomy eco = EconomyManager.Instance.Economy.Countries.Length > playerId
            ? EconomyManager.Instance.Economy.Countries[playerId] : null;

        _dateLabel.Text = TimeManager.Instance.CurrentDateString;
        _treasuryLabel.Text = country != null ? $"$ {country.Treasury:N0}" : "";
        _incomeLabel.Text = eco != null
            ? $"{Sign(eco.BudgetRevenue - eco.BudgetExpenses)}{eco.BudgetRevenue - eco.BudgetExpenses:N0}/d" : "";
        _gdpLabel.Text = country != null ? $"GDP {country.Gdp:N0}" : "";
        _popLabel.Text = country != null ? $"pop {country.Population:N0}" : "";
        _stabilityLabel.Text = country != null ? $"stab {country.Stability:0}%" : "";
    }

    private static string Sign(double v) => v >= 0 ? "+" : "";

    // --- События выбора ------------------------------------------------------

    private void OnProvinceSelected(int provinceId)
    {
        _selectedProvince = provinceId;
        WorldData world = DataManager.Instance.World;
        ProvinceData p = world.GetProvince(provinceId);
        int playerId = PlayerId();

        if (p.OwnerId >= 0 && p.OwnerId != playerId)
            _targetCountry = p.OwnerId;
        else
            _targetCountry = -1;

        RebuildPanels();
    }

    private void RebuildPanels()
    {
        if (_provinceBox == null)
            return;
        RebuildProvincePanel();
        RebuildPlayerPanel();
        RebuildTargetPanel();
        RebuildTechPanel();
    }

    // --- Панель провинции ----------------------------------------------------

    private void RebuildProvincePanel()
    {
        ClearChildren(_provinceBox);
        if (_selectedProvince < 0 || !DataManager.Instance.IsLoaded)
            return;

        WorldData world = DataManager.Instance.World;
        ProvinceData p = world.GetProvince(_selectedProvince);
        int playerId = PlayerId();
        string ownerName = world.TryGetCountry(p.OwnerId, out CountryData owner)
            ? LocalizationManager.Instance.Get(owner.NameKey)
            : LocalizationManager.Instance.Get("unclaimed");

        var info = new Label
        {
            Text =
                $"Province #{p.Id}  ({ownerName})\n" +
                $"Pop: {p.TotalPopulation:N0}\n" +
                $"  Adults: {p.MaleAdults + p.FemaleAdults:N0} (M {p.MaleAdults:N0} / F {p.FemaleAdults:N0})\n" +
                $"  Children: {p.MaleChildren + p.FemaleChildren:N0}  Seniors: {p.MaleSeniors + p.FemaleSeniors:N0}\n" +
                $"Terrain: {p.Terrain}  Climate: {p.Climate}  Coastal: {p.IsCoastal}\n" +
                $"Infra: {p.Infrastructure:P0}  Dev: {p.Development:P0}  Unrest: {p.Unrest:P0}\n" +
                $"Fort: {p.FortLevel}  Neighbors: {p.NeighborIds.Length}",
        };
        _provinceBox.AddChild(info);

        if (p.OwnerId == playerId)
        {
            AddButton(_provinceBox, L("ACT_BUILD_INFRA"), () =>
            {
                CountryData c = world.GetCountry(playerId);
                if (c.Treasury >= 500)
                {
                    ProvinceData cur = world.GetProvince(_selectedProvince);
                    cur.Infrastructure = Mathf.Clamp(cur.Infrastructure + 0.05f, 0f, 1f);
                    world.SetProvince(_selectedProvince, in cur);
                    c.Treasury -= 500;
                    UpdateTopBar();
                    RebuildProvincePanel();
                }
            });

            AddButton(_provinceBox, L("ACT_RECRUIT"), () =>
            {
                MilitaryManager.Instance.RecruitArmy(playerId, _selectedProvince,
                    new System.Collections.Generic.Dictionary<int, int> { { 0, 5 } });
                EventBus.Instance.EmitUINotification(L("MSG_ARMY_RECRUITED"));
            });

            AddButton(_provinceBox, L("ACT_BUILD_FORT"), () =>
            {
                if (MilitaryManager.Instance.BuildFort(playerId, _selectedProvince))
                    RebuildProvincePanel();
                else
                    EventBus.Instance.EmitUINotification(L("MSG_FORT_FAIL"));
            });
        }
        else if (p.OwnerId < 0 && !world.TryGetCountry(p.OwnerId, out _))
        {
            // Пустая провинция, граничащая с владениями игрока — колонизация.
            AddButton(_provinceBox, L("ACT_COLONIZE"), () =>
            {
                if (MilitaryManager.Instance.Colonize(playerId, _selectedProvince))
                    RebuildProvincePanel();
                else
                    EventBus.Instance.EmitUINotification(L("MSG_COLONIZE_FAIL"));
            });
        }
    }

    // --- Панель игрока -------------------------------------------------------

    private void RebuildPlayerPanel()
    {
        ClearChildren(_playerBox);
        if (!DataManager.Instance.IsLoaded)
            return;

        WorldData world = DataManager.Instance.World;
        int playerId = PlayerId();
        CountryData c = world.GetCountry(playerId);
        CountryEconomy eco = EconomyManager.Instance.Economy.Countries[playerId];

        var info = new Label
        {
            Text =
                $"{c.NameKey} ({c.Code})\n" +
                $"Gov: {c.GovernmentType}  Ideology: {c.Ideology}\n" +
                $"GDP: {c.Gdp:N0}  Pop: {c.Population:N0}\n" +
                $"Debt: {c.Debt:N0}  Inflation: {eco.Inflation * 100:0.0}%\n" +
                $"Stability: {c.Stability:0}  Legitimacy: {c.Legitimacy:0}  WarExh: {c.WarExhaustion:0}\n" +
                $"Income tax: {eco.Taxes.Income:P0}",
        };
        _playerBox.AddChild(info);

        AddButton(_playerBox, L("ACT_TAX_PLUS"), () =>
        {
            eco.Taxes.Income = Math.Min(eco.Taxes.Income + 0.05, 0.5);
            UpdateTopBar(); RebuildPlayerPanel();
        });
        AddButton(_playerBox, L("ACT_TAX_MINUS"), () =>
        {
            eco.Taxes.Income = Math.Max(eco.Taxes.Income - 0.05, 0.0);
            UpdateTopBar(); RebuildPlayerPanel();
        });
        AddButton(_playerBox, L("ACT_CHANGE_GOV"), () =>
        {
            GovernanceSystem.ChangeGovernment(world, playerId);
            RebuildPlayerPanel();
        });
        AddButton(_playerBox, L("ACT_CHANGE_IDEOLOGY"), () =>
        {
            GovernanceSystem.ChangeIdeology(world, playerId);
            RebuildPlayerPanel();
        });
    }

    // --- Панель целевой страны ------------------------------------------------

    private void RebuildTargetPanel()
    {
        ClearChildren(_targetBox);
        if (_targetCountry < 0 || !DataManager.Instance.IsLoaded)
            return;

        WorldData world = DataManager.Instance.World;
        int playerId = PlayerId();
        CountryData t = world.GetCountry(_targetCountry);

        var info = new Label
        {
            Text =
                $"{t.NameKey} ({t.Code})\n" +
                $"Relation: {t.RelationWith(playerId):0}\n" +
                $"Status: {DiplomacyManager.Instance.GetStatus(playerId, _targetCountry)}\n" +
                $"Trade: {DiplomacyManager.Instance.GetTradeAgreement(playerId, _targetCountry)}\n" +
                $"GDP: {t.Gdp:N0}  Army: {t.OwnedProvinceIds.Count} prov",
        };
        _targetBox.AddChild(info);

        AddButton(_targetBox, L("ACT_IMPROVE_REL"), () =>
        {
            DiplomacyManager.Instance.ImproveRelations(playerId, _targetCountry, 10f);
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_ALLIANCE"), () =>
        {
            DiplomacyManager.Instance.FormAlliance(playerId, _targetCountry);
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_DECLARE_WAR"), () =>
        {
            DiplomacyManager.Instance.DeclareWar(playerId, _targetCountry, "conquest");
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_WHITE_PEACE"), () =>
        {
            DiplomacyManager.Instance.MakePeace(playerId, _targetCountry, new PeaceTerms());
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_PEACE_CEDE"), () =>
        {
            DiplomacyManager.Instance.MakePeace(playerId, _targetCountry,
                new PeaceTerms { CedeOccupied = true });
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_PEACE_PUPPET"), () =>
        {
            DiplomacyManager.Instance.MakePeace(playerId, _targetCountry,
                new PeaceTerms { Puppet = true });
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_PEACE_ANNEX"), () =>
        {
            DiplomacyManager.Instance.MakePeace(playerId, _targetCountry,
                new PeaceTerms { Annex = true });
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_EMBARGO"), () =>
        {
            DiplomacyManager.Instance.ToggleEmbargo(playerId, _targetCountry);
            RebuildTargetPanel();
        });
        AddButton(_targetBox, L("ACT_TRADE_AGREEMENT"), () =>
        {
            DiplomacyManager.Instance.CycleTradeAgreement(playerId, _targetCountry);
            RebuildTargetPanel();
        });
    }

    // --- Панель технологий и решений ------------------------------------------

    private void RebuildTechPanel()
    {
        ClearChildren(_techBox);
        if (!DataManager.Instance.IsLoaded)
            return;

        int playerId = PlayerId();
        TechManager tech = TechManager.Instance;

        var header = new Label
        {
            Text = $"{L("PANEL_RESEARCH")}: {tech.Progress(playerId):N0}",
        };
        _techBox.AddChild(header);

        foreach (TechData t in tech.Techs)
        {
            bool done = tech.IsResearched(playerId, t.Id);
            _techBox.AddChild(new Label
            {
                Text = $"{(done ? "[x]" : "[ ]")} {LocalizationManager.Instance.Get(t.NameKey)}",
            });
        }

        // Формируемые нации.
        FormableManager formable = FormableManager.Instance;
        var avail = formable.AvailableFor(playerId);
        if (avail.Count > 0)
        {
            _techBox.AddChild(new Label { Text = L("PANEL_DECISIONS") });
            foreach (FormableData f in avail)
            {
                AddButton(_techBox, $"{L("ACT_FORM")}: {LocalizationManager.Instance.Get(f.NameKey)}", () =>
                {
                    formable.Form(playerId, f);
                    RebuildPanels();
                });
            }
        }
    }

    // --- Уведомления ---------------------------------------------------------

    private void ShowToast(string text)
    {
        if (_toast == null)
            return;
        _toast.Text = text;
        _toast.Visible = true;
        GetTree().CreateTimer(4.0).Timeout += () => { if (_toast != null) _toast.Visible = false; };
    }
}
