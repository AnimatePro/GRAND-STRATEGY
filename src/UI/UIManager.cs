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
    private TextureRect _flagTex = null!;
    private Label _countryNameLabel = null!;
    private Label _dateLabel = null!;
    private Label _treasuryLabel = null!;
    private Label _incomeLabel = null!;
    private Label _manpowerLabel = null!;
    private Label _gdpLabel = null!;
    private Label _popLabel = null!;
    private Label _stabilityLabel = null!;

    private VBoxContainer _provinceBox = null!;
    private VBoxContainer _playerBox = null!;    // вкладка «Страна»
    private VBoxContainer _militaryBox = null!;  // вкладка «Армия»
    private VBoxContainer _diplomacyBox = null!; // вкладка «Дипломатия»
    private VBoxContainer _targetBox = null!;
    private VBoxContainer _techBox = null!;      // вкладка «Технологии»

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
        EventBus.Instance.SettingsChanged += ApplyUiScale;
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
        _canvas!.Visible = true;
        ApplyUiScale();
        UpdateTopBar();
        RebuildPanels();
    }

    /// <summary>Применяет масштаб интерфейса из настроек к HUD-слою.</summary>
    private void ApplyUiScale()
    {
        if (_canvas == null)
            return;
        float scale = SettingsManager.Instance.Current.Display.UiScale;
        _canvas.Scale = new Vector2(scale, scale);
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

        // Верхняя панель (полоса управления).
        var topBar = new PanelContainer();
        topBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        topBar.AddThemeStyleboxOverride("panel", GameTheme.Style(
            new Color(0.11f, 0.14f, 0.20f, 0.98f), new Color(0.22f, 0.28f, 0.40f)));
        _canvas.AddChild(topBar);

        var topHBox = new HBoxContainer();
        topHBox.AddThemeConstantOverride("separation", 14);
        topBar.AddChild(topHBox);

        // Флаг + название страны.
        _flagTex = new TextureRect
        {
            CustomMinimumSize = new Vector2(30, 20),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        topHBox.AddChild(_flagTex);
        _countryNameLabel = MakeLabel(topHBox);

        _dateLabel = MakeLabel(topHBox);
        _treasuryLabel = MakeLabel(topHBox);
        _incomeLabel = MakeLabel(topHBox);
        _manpowerLabel = MakeLabel(topHBox);
        _gdpLabel = MakeLabel(topHBox);
        _popLabel = MakeLabel(topHBox);
        _stabilityLabel = MakeLabel(topHBox);

        AddButton(topHBox, L("HUD_PAUSE"), () => GameManager.Instance.Pause());
        AddButton(topHBox, L("HUD_SPEED_DOWN"), () => TimeManager.Instance.SetSpeed(TimeManager.Instance.Speed - 1));
        AddButton(topHBox, L("HUD_SPEED_UP"), () => TimeManager.Instance.SetSpeed(TimeManager.Instance.Speed + 1));
        AddButton(topHBox, L("HUD_END_TURN"), () => GameManager.Instance.EndTurn());
        AddButton(topHBox, L("HUD_SAVE"), () => GameManager.Instance.SaveGame("manual"));
        AddButton(topHBox, L("HUD_MENU"), () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn"));

        // Панель провинции (слева сверху, контекстная).
        _provinceBox = MakePanel(Control.LayoutPreset.TopLeft, new Vector2(8, 64), new Vector2(360, 300), L("PANEL_PROVINCE"));

        // Левая боковая панель с вкладками (Страна / Армия / Технологии).
        var sidebar = new TabContainer();
        sidebar.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        sidebar.OffsetLeft = 8;
        sidebar.OffsetTop = 310;
        sidebar.OffsetRight = 380;
        sidebar.OffsetBottom = -60;
        sidebar.TabAlignment = TabBar.AlignmentMode.Left;
        _canvas.AddChild(sidebar);

        _playerBox = MakeTab(sidebar, L("PANEL_COUNTRY"));
        _militaryBox = MakeTab(sidebar, L("PANEL_MILITARY"));
        _diplomacyBox = MakeTab(sidebar, L("PANEL_DIPLOMACY"));
        _techBox = MakeTab(sidebar, L("PANEL_TECHNOLOGY"));

        // Панель целевой страны (справа).
        _targetBox = MakePanel(Control.LayoutPreset.TopRight, new Vector2(-380, 64), new Vector2(-8, 520), L("PANEL_TARGET"));

        // Тост.
        _toast = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _toast.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _toast.OffsetTop = -40;
        _canvas.AddChild(_toast);
    }

    /// <summary>Создаёт вкладку TabContainer с прокруткой и возвращает её контент-VBox.</summary>
    private static VBoxContainer MakeTab(TabContainer tabs, string title)
    {
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(vbox);
        tabs.AddChild(scroll);
        scroll.Name = title;
        return vbox;
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
        CountryEconomy? eco = EconomyManager.Instance.Economy.Countries.Length > playerId
            ? EconomyManager.Instance.Economy.Countries[playerId] : null;

        _dateLabel.Text = TimeManager.Instance.CurrentDateString;
        _countryNameLabel.Text = country != null ? world.CountryName(country, LocalizationManager.Instance.Language) : "";
        if (country != null)
            _flagTex.Texture = LoadFlag(country.FlagId);

        double balance = eco != null ? eco.BudgetRevenue - eco.BudgetExpenses : 0.0;
        _treasuryLabel.Text = country != null ? $"{L("HUD_GOLD")} {country.Treasury:N0}" : "";
        _incomeLabel.Text = eco != null ? $"{Sign(balance)}{balance:N0}{L("HUD_PER_DAY")}" : "";
        _manpowerLabel.Text = country != null ? $"{L("HUD_MANPOWER")} {ManpowerOf(world, playerId):N0}" : "";
        _gdpLabel.Text = country != null ? $"{L("HUD_GDP")} {country.Gdp:N0}" : "";
        _popLabel.Text = country != null ? $"{L("HUD_POP")} {country.Population:N0}" : "";
        _stabilityLabel.Text = country != null ? $"{L("HUD_STAB")} {country.Stability:0}%" : "";
    }

    /// <summary>Людской запас страны: сумма взрослых мужчин по владениям.</summary>
    private static long ManpowerOf(WorldData world, int countryId)
    {
        if (countryId < 0 || countryId >= world.Countries.Length)
            return 0;
        CountryData c = world.Countries[countryId];
        if (c == null)
            return 0;
        long mp = 0;
        foreach (int pid in c.OwnedProvinceIds)
            mp += world.GetProvince(pid).MaleAdults;
        return mp;
    }

    private static string Sign(double v) => v >= 0 ? "+" : "";

    /// <summary>Загрузка текстуры флага (SVG импортируется Godot'ом в текстуру); null при отсутствии.</summary>
    private static Texture2D? LoadFlag(string flagId)
    {
        string path = $"res://assets/flags/{flagId}.svg";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

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
        RebuildMilitaryPanel();
        RebuildDiplomacyPanel();
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
        string lang = LocalizationManager.Instance.Language;
        string ownerName = world.TryGetCountry(p.OwnerId, out CountryData owner)
            ? world.CountryName(owner, lang)
            : LocalizationManager.Instance.Get("unclaimed");
        string provName = world.ProvinceName(p.Id, lang);

        var info = new Label
        {
            Text =
                $"{provName} ({ownerName})\n" +
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

            // Сетка зданий: кнопка постройки на каждый тип.
            _provinceBox.AddChild(new Label { Text = L("PANEL_BUILDINGS") });
            foreach (BuildingData b in world.Buildings)
            {
                AddButton(_provinceBox, $"{LocalizationManager.Instance.Get(b.NameKey)} ({b.BuildCost:N0})", () =>
                {
                    if (MilitaryManager.Instance.BuildBuilding(playerId, _selectedProvince, b.Id))
                        RebuildProvincePanel();
                    else
                        EventBus.Instance.EmitUINotification(L("MSG_BUILD_FAIL"));
                });
            }

            // Построенные здания.
            if (p.BuildingIds.Length > 0)
            {
                var built = new System.Text.StringBuilder();
                foreach (int bid in p.BuildingIds)
                    if (bid >= 0 && bid < world.Buildings.Length)
                        built.Append(LocalizationManager.Instance.Get(world.Buildings[bid].NameKey)).Append(", ");
                _provinceBox.AddChild(new Label { Text = $"{L("PANEL_BUILT")}: {built.ToString().TrimEnd(',', ' ')}" });
            }
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
                $"{world.CountryName(c, LocalizationManager.Instance.Language)} ({c.Code})\n" +
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

    // --- Панель армии ----------------------------------------------------------

    private void RebuildMilitaryPanel()
    {
        ClearChildren(_militaryBox);
        if (!DataManager.Instance.IsLoaded)
            return;

        int playerId = PlayerId();
        MilitaryManager mil = MilitaryManager.Instance;

        var header = new Label
        {
            Text = $"{L("PANEL_MILITARY")}: {mil.Armies.Count(a => a.OwnerId == playerId)} {L("MIL_ARMIES")}",
        };
        _militaryBox.AddChild(header);

        if (mil.SelectedArmyId >= 0)
        {
            _militaryBox.AddChild(new Label { Text = L("MIL_SELECT_HINT") });
        }

        // Армии игрока (кнопка Select + клик по карте = приказ движения).
        foreach (ArmyData army in mil.Armies)
        {
            if (army.OwnerId != playerId)
                continue;
            CommanderData? cmd = mil.Commanders.Find(c => c.Id == army.CommanderId);
            string label = $"{(army.Id == mil.SelectedArmyId ? "▶ " : "")}{army.TotalUnits} {L("MIL_UNITS")} @ {DataManager.Instance.World.ProvinceName(army.ProvinceId, LocalizationManager.Instance.Language)} ({cmd?.Name ?? "-"})";
            AddButton(_militaryBox, label, () =>
            {
                mil.SelectedArmyId = army.Id;
                RebuildMilitaryPanel();
            });
        }

        AddButton(_militaryBox, L("ACT_RECRUIT_COMMANDER"), () =>
        {
            int cap = DataManager.Instance.World.GetCountry(playerId).CapitalProvinceId;
            if (mil.RecruitCommander(playerId, cap))
                EventBus.Instance.EmitUINotification(L("MSG_COMMANDER_RECRUITED"));
            else
                EventBus.Instance.EmitUINotification(L("MSG_COMMANDER_FAIL"));
            RebuildMilitaryPanel();
        });
    }

    // --- Вкладка дипломатии ---------------------------------------------------

    private void RebuildDiplomacyPanel()
    {
        ClearChildren(_diplomacyBox);
        if (!DataManager.Instance.IsLoaded)
            return;

        WorldData world = DataManager.Instance.World;
        int playerId = PlayerId();
        string lang = LocalizationManager.Instance.Language;

        var countries = new System.Collections.Generic.List<CountryData>();
        foreach (CountryData c in world.Countries)
            if (c != null && c.IsAlive && c.Id != playerId)
                countries.Add(c);
        countries.Sort((a, b) => world.CountryName(a, lang).CompareTo(world.CountryName(b, lang)));

        foreach (CountryData c in countries)
        {
            float rel = c.RelationWith(playerId);
            string status = DiplomacyManager.Instance.GetStatus(playerId, c.Id).ToString();
            AddButton(_diplomacyBox, $"{world.CountryName(c, lang)}  (rel {rel:0}, {status})", () =>
            {
                _targetCountry = c.Id;
                RebuildTargetPanel();
                EventBus.Instance.EmitUINotification(world.CountryName(c, lang));
            });
        }
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
                $"{world.CountryName(t, LocalizationManager.Instance.Language)} ({t.Code})\n" +
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
