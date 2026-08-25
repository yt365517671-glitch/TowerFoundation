using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using TowerFoundation.Application;
using TowerFoundation.Domain;
using TowerFoundation.Optimization;

namespace TowerFoundation.Desktop;

public sealed record CatalogNumericFilterOption(string Display, double? Value);

public sealed class MonitoringManualCompletionItem
{
    public required string FieldName { get; init; }

    public required string DisplayName { get; init; }

    public required string Unit { get; init; }

    public required string Explanation { get; init; }

    public required string RecognitionStatus { get; init; }

    public string InputText { get; set; } = string.Empty;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private sealed record MonitoringInputDefinition(
        string FieldName,
        string DisplayName,
        string Unit,
        string Explanation,
        bool AllowZero = false);

    private static readonly MonitoringInputDefinition[] MonitoringInputDefinitions =
    [
        new(MonitoringDrawingFieldNames.PoleHeight, "立杆高度", "m", "从基础顶面或底法兰安装基准面到立杆顶端的竖向高度，不包含基础埋深。"),
        new(MonitoringDrawingFieldNames.PoleBottomDimension, "立杆下端尺寸", "mm", "立杆根部钢管的外轮廓尺寸；正八边形填写对角尺寸，圆管填写外径，不是法兰或螺栓圆尺寸。"),
        new(MonitoringDrawingFieldNames.PoleTopDimension, "立杆上端尺寸", "mm", "立杆顶部钢管的外轮廓尺寸；正八边形填写对角尺寸，且不得大于下端尺寸。"),
        new(MonitoringDrawingFieldNames.PoleWallThickness, "立杆壁厚", "mm", "立杆钢管母材实际壁厚，不是法兰、加劲板或镀锌层厚度。"),
        new(MonitoringDrawingFieldNames.ArmMountingHeight, "横杆安装高度", "m", "从基础顶面或安装基准面到横杆中心线的竖向距离，是横杆及设备风荷载的竖向力臂。"),
        new(MonitoringDrawingFieldNames.ArmLength, "横杆长度", "m", "从立杆连接处到横杆远端的水平总长度；局部尺寸链不得重复相加。"),
        new(MonitoringDrawingFieldNames.ArmNearDimension, "横杆近端尺寸", "mm", "横杆靠近立杆一端的外轮廓尺寸；正八边形填写对角尺寸。"),
        new(MonitoringDrawingFieldNames.ArmFarDimension, "横杆远端尺寸", "mm", "横杆最远端的外轮廓尺寸；不得大于近端尺寸。"),
        new(MonitoringDrawingFieldNames.ArmWallThickness, "横杆壁厚", "mm", "横杆钢管母材壁厚；分段横杆按各段分别填写，不能用平均壁厚代替。"),
        new(MonitoringDrawingFieldNames.ArmCount, "横杆数量", "个", "同一立杆上参与受力计算的横杆总数；单臂为1，双向横臂按实际数量填写。"),
        new(MonitoringDrawingFieldNames.AttachmentProjectedArea, "设备迎风面积", "m²", "摄像机、补光灯、机箱等全部设备垂直于来风方向的有效投影面积合计，不包含杆件自身迎风面积。", true),
        new(MonitoringDrawingFieldNames.AttachmentWeight, "设备重量", "kN", "横杆上全部设备及其支架的重力标准值合计；若资料给出kg，应先按重力换算为kN。", true)
    ];

    public bool SuppressErrorDialogsForAutomation { get; set; }

    private readonly DesignWorkflowService _workflow;
    private readonly IProjectRepository _repository;
    private readonly IProjectCatalogService _projectCatalogService;
    private readonly IProjectOutputService _outputService;
    private readonly IApplicationSettingsService _settingsService;
    private readonly IDeepSeekService _deepSeekService;
    private readonly IVisualGeotechnicalAiService _visualGeotechnicalAiService;
    private readonly IMonitoringDrawingVisionAiService _monitoringDrawingVisionAiService;
    private readonly IMonitoringDrawingRecognitionHistoryService
        _monitoringDrawingRecognitionHistoryService;
    private readonly GeotechnicalDocumentImportService _documentImportService;
    private readonly IGeotechnicalAnalysisHistoryService _geotechnicalHistoryService;
    private readonly IRegionWindCatalog _regionWindCatalog;
    private readonly EnterpriseTowerLoadService _enterpriseTowerLoadService;
    private readonly SpecialtyAutoFillService _specialtyAutoFillService = new();
    private readonly LocationSeismicReferenceService _locationSeismicReferenceService = new();
    private readonly ProjectReadinessService _projectReadinessService = new();
    private ProjectModel _project = CreateNewProject();
    private FoundationScheme? _selectedCandidate;
    private FoundationScheme? _customScheme;
    private FoundationLoad? _loadPreview;
    private string? _currentFilePath;
    private string? _lastOutputDirectory;
    private string _statusMessage = "请先选择工程类型。";
    private string _customResultSummary = "输入尺寸后点击“复算自定义尺寸”。";
    private string _aiStatusText = "AI 状态检查中";
    private string _aiStatusDetail = "正在读取本机设置。";
    private string _aiImportSummary = "尚未进行 AI 地勘识别。";
    private string _geotechnicalHistorySummary = "正在读取本机地勘分析记录。";
    private string _aiProgressMessage = string.Empty;
    private int _selectedStep;
    private bool _isBusy;
    private bool _isAiProgressVisible;
    private bool _isAiProgressIndeterminate;
    private bool _shouldExpandAdvancedDesignParameters;
    private bool _isFormalUseAuthorized;
    private double _aiProgressValue;
    private double _customBaseLengthM = 2.0;
    private double _customBaseWidthM = 2.0;
    private double _customBaseThicknessM = 0.8;
    private double _customPileDiameterM = 0.8;
    private double _customPileLengthM = 12;
    private RegionOption? _selectedProvince;
    private RegionOption? _selectedCity;
    private RegionOption? _selectedCounty;
    private WindPressureStation? _selectedManualWindStation;
    private string _windPressureSummary = "请先选择省、市、县区。";
    private string _seismicLocationSummary =
        "选到县/区后，软件会尝试带出设防烈度、基本地震加速度和设计地震分组。";
    private bool _isApplyingWindLookup;
    private const string AllCurrentCatalogsLabel = "全部现行图集";
    private string _selectedTowerCatalogSource = AllCurrentCatalogsLabel;
    private string _selectedTowerCatalogType = "全部塔型";
    private string _towerCatalogSearchText = string.Empty;
    private CatalogNumericFilterOption _selectedTowerCatalogHeight =
        new("全部塔高", null);
    private CatalogNumericFilterOption _selectedTowerCatalogWindPressure =
        new("全部风压", null);
    private TowerLoadCatalogRecord? _selectedTowerCatalogRecord;
    private GeotechnicalAnalysisRecord? _selectedGeotechnicalHistoryRecord;
    private MonitoringDrawingCandidate? _selectedMonitoringDrawingCandidate;
    private Guid? _manualCompletionCandidateId;
    private string _towerCatalogStatus = "正在载入现行V2.0企业标准塔型荷载库。";

    public MainViewModel(
        DesignWorkflowService workflow,
        IProjectRepository repository,
        IProjectCatalogService projectCatalogService,
        IProjectOutputService outputService,
        IApplicationSettingsService settingsService,
        IDeepSeekService deepSeekService,
        IVisualGeotechnicalAiService visualGeotechnicalAiService,
        IMonitoringDrawingVisionAiService monitoringDrawingVisionAiService,
        IMonitoringDrawingRecognitionHistoryService monitoringDrawingRecognitionHistoryService,
        IWordTextExtractor wordTextExtractor,
        ILocalPdfOcrService localPdfOcrService,
        IGeotechnicalAnalysisHistoryService geotechnicalHistoryService,
        IRegionWindCatalog regionWindCatalog,
        ITowerLoadCatalog towerLoadCatalog,
        bool isFormalUseAuthorized = true)
    {
        _workflow = workflow;
        _repository = repository;
        _projectCatalogService = projectCatalogService;
        _outputService = outputService;
        _settingsService = settingsService;
        _deepSeekService = deepSeekService;
        _visualGeotechnicalAiService = visualGeotechnicalAiService;
        _monitoringDrawingVisionAiService = monitoringDrawingVisionAiService;
        _monitoringDrawingRecognitionHistoryService =
            monitoringDrawingRecognitionHistoryService;
        _documentImportService = new GeotechnicalDocumentImportService(
            settingsService,
            deepSeekService,
            wordTextExtractor,
            localPdfOcrService);
        _geotechnicalHistoryService = geotechnicalHistoryService;
        _regionWindCatalog = regionWindCatalog;
        _enterpriseTowerLoadService = new EnterpriseTowerLoadService(towerLoadCatalog);
        _towerCatalogStatus = _enterpriseTowerLoadService.Status.UserDisplay;
        _isFormalUseAuthorized = isFormalUseAuthorized;

        Provinces = new ObservableCollection<RegionOption>(_regionWindCatalog.Provinces);
        TowerCatalogSources.Add(AllCurrentCatalogsLabel);
        foreach (var source in _enterpriseTowerLoadService.GetSourceTitles())
        {
            TowerCatalogSources.Add(source);
        }

        ApplyTowerCatalogLoadCommand = new RelayCommand(
            ApplySelectedTowerCatalogLoad,
            () => !IsBusy && IsFormalUseAuthorized &&
                  CanOperateWorkflowStep(3) &&
                  IsTowerProject &&
                  IsTowerCatalogLoad &&
                  CanApplySelectedTowerCatalogRecord());
        RefreshTowerCatalogTypesAndRecords();
        SyncRegionSelectionFromProject();

        ReturnToCurrentWorkflowCommand = new RelayCommand(
            ReturnToCurrentWorkflow,
            () => !IsBusy && IsBrowsingWorkflowStep);
        ReviseViewedWorkflowStepCommand = new RelayCommand(
            ReviseViewedWorkflowStep,
            () => !IsBusy && CanReviseViewedWorkflowStep);
        NewProjectCommand = new RelayCommand(NewProject, () => !IsBusy);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync, () => !IsBusy);
        SaveProjectCommand = new AsyncRelayCommand(() => SaveProjectAsync(false), () => !IsBusy && IsFormalUseAuthorized);
        SaveProjectAsCommand = new AsyncRelayCommand(() => SaveProjectAsync(true), () => !IsBusy && IsFormalUseAuthorized);
        SelectMonitoringProjectCommand = new RelayCommand(
            () => SelectProjectType(ProjectType.MonitoringPole),
            () => !IsBusy && CanOperateWorkflowStep(0));
        SelectTowerProjectCommand = new RelayCommand(
            () => SelectProjectType(ProjectType.CommunicationTower),
            () => !IsBusy && CanOperateWorkflowStep(0));
        GenerateSchemesCommand = new RelayCommand(
            GenerateSchemes,
            () => !IsBusy && IsFormalUseAuthorized && HasProjectType && CanOperateWorkflowStep(4));
        SelectSchemeCommand = new RelayCommand(
            SelectScheme,
            () => !IsBusy && IsFormalUseAuthorized &&
                  CanOperateWorkflowStep(4) &&
                  SelectedCandidate is not null);
        UseCandidateAsCustomCommand = new RelayCommand(
            UseCandidateAsCustom,
            () => !IsBusy && IsFormalUseAuthorized &&
                  CanOperateWorkflowStep(4) &&
                  SelectedCandidate is not null);
        EvaluateCustomSchemeCommand = new RelayCommand(
            EvaluateCustomScheme,
            () => !IsBusy && IsFormalUseAuthorized && HasProjectType && CanOperateWorkflowStep(4));
        AdoptCustomSchemeCommand = new RelayCommand(
            AdoptCustomScheme,
            () => !IsBusy && IsFormalUseAuthorized &&
                  CanOperateWorkflowStep(4) &&
                  CustomScheme?.IsFeasible == true);
        ExportPrototypePackageCommand = new AsyncRelayCommand(
            () => ExportDesignPackageAsync(chooseDirectory: false),
            () => !IsBusy && IsFormalUseAuthorized &&
                  CanOperateWorkflowStep(5) &&
                  SelectedScheme is not null);
        ExportPackageAsCommand = new AsyncRelayCommand(
            () => ExportDesignPackageAsync(chooseDirectory: true),
            () => !IsBusy && IsFormalUseAuthorized &&
                  CanOperateWorkflowStep(5) &&
                  SelectedScheme is not null);
        ImportGeotechnicalWordCommand = new AsyncRelayCommand(
            ImportGeotechnicalWordAsync,
            () => !IsBusy && IsFormalUseAuthorized && CanOperateWorkflowStep(2));
        ImportGeotechnicalPdfCommand = new AsyncRelayCommand(
            ImportGeotechnicalPdfAsync,
            () => !IsBusy && IsFormalUseAuthorized && CanOperateWorkflowStep(2));
        ImportGeotechnicalVisionPdfCommand = new AsyncRelayCommand(
            ImportGeotechnicalVisionPdfAsync,
            () => !IsBusy && IsFormalUseAuthorized && CanOperateWorkflowStep(2));
        RecognizeMonitoringDrawingsCommand = new AsyncRelayCommand(
            RecognizeMonitoringDrawingsAsync,
            () => !IsBusy && IsFormalUseAuthorized && IsMonitoringProject && CanOperateWorkflowStep(3));
        ApplyMonitoringDrawingCandidateCommand = new RelayCommand(
            ApplySelectedMonitoringDrawingCandidate,
            () => !IsBusy && IsFormalUseAuthorized &&
                  IsMonitoringProject &&
                  CanOperateWorkflowStep(3) &&
                  SelectedMonitoringDrawingCandidate is not null);
        ApplyMonitoringMissingInputsCommand = new RelayCommand(
            ApplyMonitoringMissingInputs,
            () => !IsBusy && IsFormalUseAuthorized &&
                  IsMonitoringProject &&
                  CanOperateWorkflowStep(3) &&
                  MonitoringMissingInputs.Count > 0);
        ReuseGeotechnicalHistoryCommand = new RelayCommand(
            ReuseSelectedGeotechnicalHistory,
            () => IsFormalUseAuthorized && CanReuseSelectedGeotechnicalHistory());
        ReanalyzeGeotechnicalHistoryCommand = new AsyncRelayCommand(
            ReanalyzeSelectedGeotechnicalHistoryAsync,
            () => !IsBusy && IsFormalUseAuthorized &&
                  CanOperateWorkflowStep(2) &&
                  SelectedGeotechnicalHistoryRecord is not null);
        DeleteGeotechnicalHistoryCommand = new RelayCommand(
            DeleteSelectedGeotechnicalHistory,
            () => !IsBusy && IsFormalUseAuthorized && SelectedGeotechnicalHistoryRecord is not null);
        RefreshGeotechnicalHistory();
        RefreshAiStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsFormalUseAuthorized
    {
        get => _isFormalUseAuthorized;
        private set
        {
            if (_isFormalUseAuthorized == value)
            {
                return;
            }
            _isFormalUseAuthorized = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public void SetFormalUseAuthorization(bool authorized)
    {
        IsFormalUseAuthorized = authorized;
        if (!authorized)
        {
            StatusMessage = "当前为未授权预览模式；可以查看界面，但计算、AI、保存和导出已停用。";
        }
    }

    public ProjectModel Project
    {
        get => _project;
        private set
        {
            _project = value;
            OnPropertyChanged();
            NotifyProjectTypeChanged();
            OnPropertyChanged(nameof(PoleBottomDiameterMm));
            OnPropertyChanged(nameof(PoleTopDiameterMm));
            OnPropertyChanged(nameof(PoleWallThicknessMm));
            OnPropertyChanged(nameof(ArmNearDiameterMm));
            OnPropertyChanged(nameof(ArmFarDiameterMm));
            OnPropertyChanged(nameof(ArmWallThicknessMm));
            OnPropertyChanged(nameof(PoleHeightInput));
            OnPropertyChanged(nameof(ArmMountingHeightInput));
            OnPropertyChanged(nameof(ArmLengthInput));
            OnPropertyChanged(nameof(ArmCountInput));
            OnPropertyChanged(nameof(AttachmentProjectedAreaInput));
            OnPropertyChanged(nameof(AttachmentWeightInput));
            RefreshMonitoringDrawingCandidates();
            RefreshMonitoringMissingInputs();
            OnPropertyChanged(nameof(PoleSectionTypeDisplay));
            OnPropertyChanged(nameof(ArmSectionTypeDisplay));
            OnPropertyChanged(nameof(ArmSegmentSummary));
            OnPropertyChanged(nameof(BasicWindPressureKpa));
            OnPropertyChanged(nameof(SelectedScheme));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(SpecialtyReadinessSummary));
            OnPropertyChanged(nameof(SelectedAnchorConnectionType));
            OnPropertyChanged(nameof(SeismicLocationSummary));
            NotifyFoundationTypeChanged();
            NotifyWorkflowPositionChanged();
            SyncRegionSelectionFromProject();
            SyncTowerCatalogSelectionFromProject();
        }
    }

    public IReadOnlyList<TowerStructureType> TowerStructureTypes { get; } =
        Enum.GetValues<TowerStructureType>();

    public IReadOnlyList<TubeSectionType> TubeSectionTypes { get; } =
        Enum.GetValues<TubeSectionType>();

    public TubeSectionType SelectedPoleSectionType
    {
        get => Project.MonitoringPole.PoleSectionType;
        set
        {
            if (Project.MonitoringPole.PoleSectionType == value)
            {
                return;
            }
            Project.MonitoringPole.PoleSectionType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PoleSectionTypeDisplay));
        }
    }

    public TubeSectionType SelectedArmSectionType
    {
        get => Project.MonitoringPole.ArmSectionType;
        set
        {
            if (Project.MonitoringPole.ArmSectionType == value)
            {
                return;
            }
            Project.MonitoringPole.ArmSectionType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArmSectionTypeDisplay));
        }
    }

    public string PoleSectionTypeDisplay =>
        Project.MonitoringPole.PoleSectionType == TubeSectionType.RegularOctagonDiagonalTube
            ? "正八边形（按对角尺寸）"
            : "圆形管";

    public string ArmSectionTypeDisplay =>
        Project.MonitoringPole.ArmSectionType == TubeSectionType.RegularOctagonDiagonalTube
            ? "正八边形（按对角尺寸）"
            : "圆形管";

    public IReadOnlyList<AnchorConnectionChoice> QuickAnchorConnectionChoices { get; } =
    [
        new("请选择", AnchorConnectionType.NotDetermined),
        new("地脚锚栓连接", AnchorConnectionType.AnchorBoltCage),
        new("塔身直接埋入", AnchorConnectionType.DirectEmbedded),
        new("其他连接（转专项）", AnchorConnectionType.Other)
    ];

    public AnchorConnectionType SelectedAnchorConnectionType
    {
        get => Project.FoundationSettings.SpecialtyDesign.AnchorBolts.ConnectionType;
        set
        {
            if (Project.FoundationSettings.SpecialtyDesign.AnchorBolts.ConnectionType == value)
            {
                return;
            }

            Project.FoundationSettings.SpecialtyDesign.AnchorBolts.ConnectionType = value;
            Project.FoundationSettings.SpecialtyDesign.AnchorBolts.TemplateName =
                QuickAnchorConnectionChoices.First(choice => choice.Value == value).Name;
            Project.FoundationSettings.SpecialtyDesign.AnchorBolts.Source.IsConfirmed = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpecialtyReadinessSummary));
        }
    }

    public TowerStructureType SelectedTowerStructureType
    {
        get => Project.TowerMast.StructureType;
        set
        {
            if (Project.TowerMast.StructureType == value)
            {
                return;
            }

            Project.TowerMast.StructureType = value;
            Project.TowerMast.FoundationLegCount = 0;
            Project.TowerMast.UsesIndividualPileReactions =
                PileLayoutRules.RequiresSingleLegReactions(
                    Project.TowerMast,
                    Project.FoundationSettings.FoundationType);
            Project.TowerMast.IsConfirmed = false;
            PileLayoutRules.Synchronize(Project);
            Project.Schemes.Clear();
            Project.SelectedSchemeId = null;
            Schemes.Clear();
            SelectedCandidate = null;
            CustomScheme = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMultiLegPileFoundation));
            OnPropertyChanged(nameof(IsMultiLegFoundation));
            OnPropertyChanged(nameof(PileLayoutSummary));
            OnPropertyChanged(nameof(PileCountDisplay));
            OnPropertyChanged(nameof(SelectedTowerFoundationLegCount));
            ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
        }
    }

    public int SelectedTowerFoundationLegCount
    {
        get => PileLayoutRules.GetPileCount(Project.TowerMast);
        set
        {
            if (value is not 1 and not 3 and not 4 ||
                PileLayoutRules.GetPileCount(Project.TowerMast) == value &&
                Project.TowerMast.FoundationLegCount == value)
            {
                return;
            }

            Project.TowerMast.FoundationLegCount = value;
            Project.TowerMast.UsesIndividualPileReactions =
                PileLayoutRules.RequiresSingleLegReactions(
                    Project.TowerMast,
                    Project.FoundationSettings.FoundationType);
            Project.TowerMast.IsConfirmed = false;
            PileLayoutRules.Synchronize(Project);
            Project.Schemes.Clear();
            Project.SelectedSchemeId = null;
            Schemes.Clear();
            SelectedCandidate = null;
            CustomScheme = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMultiLegPileFoundation));
            OnPropertyChanged(nameof(IsMultiLegFoundation));
            OnPropertyChanged(nameof(PileLayoutSummary));
            OnPropertyChanged(nameof(PileCountDisplay));
            ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<FoundationScheme> Schemes { get; } = [];

    public ObservableCollection<FoundationAdjustmentAdvice> AdjustmentAdvices { get; } = [];

    public ObservableCollection<RegionOption> Provinces { get; }

    public ObservableCollection<RegionOption> Cities { get; } = [];

    public ObservableCollection<RegionOption> Counties { get; } = [];

    public ObservableCollection<WindPressureStation> ManualWindStations { get; } = [];

    public ObservableCollection<string> TowerCatalogSources { get; } = [];

    public ObservableCollection<string> TowerCatalogTypes { get; } = [];

    public ObservableCollection<CatalogNumericFilterOption> TowerCatalogHeights { get; } = [];

    public ObservableCollection<CatalogNumericFilterOption> TowerCatalogWindPressures { get; } = [];

    public ObservableCollection<TowerLoadCatalogRecord> FilteredTowerCatalogRecords { get; } = [];

    public string TowerCatalogAvailabilitySummary =>
        _enterpriseTowerLoadService.Status.UserDisplay;

    public bool HasCurrentTowerCatalogRecords =>
        _enterpriseTowerLoadService.Status.HasCurrentRecords;

    public RegionOption? SelectedProvince
    {
        get => _selectedProvince;
        set
        {
            if (!SetField(ref _selectedProvince, value))
            {
                return;
            }

            Cities.Clear();
            Counties.Clear();
            _selectedCity = null;
            _selectedCounty = null;
            OnPropertyChanged(nameof(SelectedCity));
            OnPropertyChanged(nameof(SelectedCounty));
            Project.Province = value?.Name ?? string.Empty;
            Project.City = string.Empty;
            Project.County = string.Empty;
            _selectedManualWindStation = null;
            OnPropertyChanged(nameof(SelectedManualWindStation));
            RefreshManualWindStations();
            if (value is not null)
            {
                foreach (var city in _regionWindCatalog.GetCities(value.Code))
                {
                    Cities.Add(city);
                }
            }

            RefreshWindPressureFromAddress();
            RefreshLocationSeismicReference();
        }
    }

    public RegionOption? SelectedCity
    {
        get => _selectedCity;
        set
        {
            if (!SetField(ref _selectedCity, value))
            {
                return;
            }

            Counties.Clear();
            _selectedCounty = null;
            OnPropertyChanged(nameof(SelectedCounty));
            Project.City = value?.Name ?? string.Empty;
            Project.County = string.Empty;
            if (value is not null)
            {
                foreach (var county in _regionWindCatalog.GetCounties(value.Code))
                {
                    Counties.Add(county);
                }
            }

            RefreshWindPressureFromAddress();
            RefreshLocationSeismicReference();
        }
    }

    public RegionOption? SelectedCounty
    {
        get => _selectedCounty;
        set
        {
            if (!SetField(ref _selectedCounty, value))
            {
                return;
            }

            Project.County = value?.Name ?? string.Empty;
            RefreshWindPressureFromAddress();
            RefreshLocationSeismicReference();
        }
    }

    public WindPressureStation? SelectedManualWindStation
    {
        get => _selectedManualWindStation;
        set
        {
            if (!SetField(ref _selectedManualWindStation, value) || value is null)
            {
                return;
            }

            _isApplyingWindLookup = true;
            try
            {
                var sourceWindPressure = value.FiftyYearKpa;
                var adoptedWindPressure = Math.Max(
                    MonitoringPoleInput.MinimumBasicWindPressureKpa,
                    sourceWindPressure);
                Project.MonitoringPole.SourceBasicWindPressureKpa = sourceWindPressure;
                Project.MonitoringPole.BasicWindPressureKpa = adoptedWindPressure;
                Project.MonitoringPole.IsMinimumBasicWindPressureApplied =
                    sourceWindPressure < MonitoringPoleInput.MinimumBasicWindPressureKpa;
                Project.MonitoringPole.BasicWindPressureSourceType =
                    BasicWindPressureSourceType.NearestStationManualReference;
                Project.MonitoringPole.BasicWindPressureSourceStation = value.City;
                var lowerBoundNote = sourceWindPressure < MonitoringPoleInput.MinimumBasicWindPressureKpa
                    ? "；查得值低于0.35 kPa，计算按高耸结构最低0.35 kPa采用"
                    : string.Empty;
                Project.MonitoringPole.BasicWindPressureSourceNote =
                    $"用户人工选择同省参考台站“{value.City}”，50年基本风压{sourceWindPressure:F2} kPa，来源{value.SourcePage}" +
                    lowerBoundNote +
                    "。软件未按坐标计算几何距离，台站代表性须由设计人员确认。";
                WindPressureSummary = Project.MonitoringPole.BasicWindPressureSourceNote;
                OnPropertyChanged(nameof(BasicWindPressureKpa));
                OnPropertyChanged(nameof(WindPressureSourceBadge));
            }
            finally
            {
                _isApplyingWindLookup = false;
            }
        }
    }

    public string SelectedTowerCatalogSource
    {
        get => _selectedTowerCatalogSource;
        set
        {
            if (SetField(ref _selectedTowerCatalogSource, value ?? AllCurrentCatalogsLabel))
            {
                RefreshTowerCatalogTypesAndRecords();
            }
        }
    }

    public string SelectedTowerCatalogType
    {
        get => _selectedTowerCatalogType;
        set
        {
            if (SetField(ref _selectedTowerCatalogType, value ?? "全部塔型"))
            {
                RefreshTowerCatalogDimensionsAndRecords();
            }
        }
    }

    public string TowerCatalogSearchText
    {
        get => _towerCatalogSearchText;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (SetField(ref _towerCatalogSearchText, normalizedValue))
            {
                RefreshTowerCatalogRecords(clearSelectionIfExcluded: false);
            }
        }
    }

    public CatalogNumericFilterOption SelectedTowerCatalogHeight
    {
        get => _selectedTowerCatalogHeight;
        set
        {
            if (SetField(
                    ref _selectedTowerCatalogHeight,
                    value ?? new CatalogNumericFilterOption("全部塔高", null)))
            {
                RefreshTowerCatalogRecords(clearSelectionIfExcluded: true);
            }
        }
    }

    public CatalogNumericFilterOption SelectedTowerCatalogWindPressure
    {
        get => _selectedTowerCatalogWindPressure;
        set
        {
            if (SetField(
                    ref _selectedTowerCatalogWindPressure,
                    value ?? new CatalogNumericFilterOption("全部风压", null)))
            {
                RefreshTowerCatalogRecords(clearSelectionIfExcluded: true);
            }
        }
    }

    public TowerLoadCatalogRecord? SelectedTowerCatalogRecord
    {
        get => _selectedTowerCatalogRecord;
        set
        {
            if (!SetField(ref _selectedTowerCatalogRecord, value))
            {
                return;
            }

            TowerCatalogStatus = value is null
                ? BuildTowerCatalogEmptyOrCountStatus()
                : $"已选择 {value.TowerCode} · {value.TowerType} · {value.SourceDisplay} · PDF第{value.SourcePdfPage}页/表第{value.SourceTableRow}行 · {value.AvailabilityDisplay}。";
            OnPropertyChanged(nameof(SelectedTowerCatalogDisplay));
            OnPropertyChanged(nameof(SelectedTowerCatalogStandardSummary));
            OnPropertyChanged(nameof(SelectedTowerCatalogBasicSummary));
            ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedTowerCatalogDisplay =>
        SelectedTowerCatalogRecord is null
            ? "尚未选择具体塔型"
            : $"{SelectedTowerCatalogRecord.TowerCode}　{SelectedTowerCatalogRecord.TowerType}";

    public string TowerCatalogMatchSummary =>
        $"当前筛选得到 {FilteredTowerCatalogRecords.Count} 条记录";

    public void SelectTowerCatalogRecord(TowerLoadCatalogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        SelectedTowerCatalogRecord = record;
        if (!string.IsNullOrWhiteSpace(_towerCatalogSearchText))
        {
            _towerCatalogSearchText = string.Empty;
            OnPropertyChanged(nameof(TowerCatalogSearchText));
            RefreshTowerCatalogRecords(clearSelectionIfExcluded: false);
        }
    }

    public string TowerCatalogStatus
    {
        get => _towerCatalogStatus;
        private set => SetField(ref _towerCatalogStatus, value);
    }

    public string SelectedTowerCatalogStandardSummary =>
        ShouldUseSelectedSingleLegLoad() &&
        SelectedTowerCatalogRecord?.SingleLegReaction?.Standard is { } leg
            ? $"单塔腿标准组合：最大压力={leg.CompressionControl?.CompressionKn:F2} kN，最大拔力={leg.TensionControl?.TensionKn:F2} kN，水平力取两工况较大值"
        : SelectedTowerCatalogRecord?.OverallBaseReaction?.Standard is { } load
            ? $"标准组合：N={load.AxialKn:F2} kN，V={load.ShearKn:F2} kN，M={load.MomentKnM:F2} kN·m"
            : SelectedTowerCatalogRecord is null
                ? "标准组合：请选择一个具体塔型型号"
                : "标准组合：当前记录未提供所选基础形式需要的可用反力";

    public string SelectedTowerCatalogBasicSummary =>
        ShouldUseSelectedSingleLegLoad() &&
        SelectedTowerCatalogRecord?.SingleLegReaction?.Basic is { } leg
            ? $"一个塔脚基本组合（用于单个基础结构验算）：压力={leg.CompressionControl?.CompressionKn:F2} kN，拔力={leg.TensionControl?.TensionKn:F2} kN"
        : SelectedTowerCatalogRecord?.OverallBaseReaction?.Basic is { } load
            ? $"基本组合（用于冲切、受剪、受弯和配筋）：N={load.AxialKn:F2} kN，V={load.ShearKn:F2} kN，M={load.MomentKnM:F2} kN·m"
            : SelectedTowerCatalogRecord is null
                ? "基本组合：选择型号后显示"
                : "基本组合：原图集未提供";

    public FoundationScheme? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetField(ref _selectedCandidate, value))
            {
                SelectSchemeCommand.RaiseCanExecuteChanged();
                UseCandidateAsCustomCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasGeneratedSchemes));
            }
        }
    }

    public bool HasGeneratedSchemes => SelectedCandidate is not null && Schemes.Count > 0;

    public bool ShouldExpandAdvancedDesignParameters
    {
        get => _shouldExpandAdvancedDesignParameters;
        set => SetField(ref _shouldExpandAdvancedDesignParameters, value);
    }

    public FoundationScheme? CustomScheme
    {
        get => _customScheme;
        private set
        {
            if (SetField(ref _customScheme, value))
            {
                AdoptCustomSchemeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public FoundationScheme? SelectedScheme =>
        Project.SelectedSchemeId is { } id
            ? Project.Schemes.FirstOrDefault(item => item.Id == id)
            : null;

    public FoundationLoad? LoadPreview
    {
        get => _loadPreview;
        private set => SetField(ref _loadPreview, value);
    }

    public bool HasProjectType => Project.ProjectType != ProjectType.NotSelected;

    public bool IsMonitoringProject
    {
        get => Project.ProjectType == ProjectType.MonitoringPole;
        set
        {
            if (value)
            {
                SelectProjectType(ProjectType.MonitoringPole);
            }
        }
    }

    public bool IsTowerProject
    {
        get => Project.ProjectType == ProjectType.CommunicationTower;
        set
        {
            if (value)
            {
                SelectProjectType(ProjectType.CommunicationTower);
            }
        }
    }

    public bool IsTowerManualLoad
    {
        get => Project.TowerMast.LoadSourceType == TowerLoadSourceType.Manual;
        set
        {
            if (value)
            {
                Project.TowerMast.LoadSourceType = TowerLoadSourceType.Manual;
                ClearTowerCatalogProvenance();
                Project.TowerMast.IsConfirmed = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTowerCatalogLoad));
                OnPropertyChanged(nameof(Project));
                ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsTowerCatalogLoad
    {
        get => Project.TowerMast.LoadSourceType == TowerLoadSourceType.EnterpriseCatalog;
        set
        {
            if (value)
            {
                Project.TowerMast.LoadSourceType = TowerLoadSourceType.EnterpriseCatalog;
                Project.TowerMast.IsConfirmed = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTowerManualLoad));
                OnPropertyChanged(nameof(Project));
                RefreshTowerCatalogRecords();
                if (!HasCurrentTowerCatalogRecords)
                {
                    TowerCatalogStatus = TowerCatalogAvailabilitySummary;
                }
                ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRectangularFoundation
    {
        get => Project.FoundationSettings.FoundationType ==
               FoundationType.RectangularShortColumn;
        set
        {
            if (value)
            {
                SelectFoundationType(FoundationType.RectangularShortColumn);
            }
        }
    }

    public bool IsCircularFoundation
    {
        get => Project.FoundationSettings.FoundationType ==
               FoundationType.CircularShortColumn;
        set
        {
            if (value)
            {
                SelectFoundationType(FoundationType.CircularShortColumn);
            }
        }
    }

    public bool IsRaftFoundation
    {
        get => Project.FoundationSettings.FoundationType == FoundationType.Raft;
        set
        {
            if (value)
            {
                SelectFoundationType(FoundationType.Raft);
            }
        }
    }

    public bool IsPileFoundation
    {
        get => Project.FoundationSettings.FoundationType == FoundationType.Pile;
        set
        {
            if (value)
            {
                SelectFoundationType(FoundationType.Pile);
            }
        }
    }

    public bool IsRigidShortPileFoundation
    {
        get => Project.FoundationSettings.FoundationType is
               FoundationType.RigidShortPile or
               FoundationType.RigidRectangularShortPile;
        set
        {
            if (value)
            {
                SelectFoundationType(FoundationType.RigidShortPile);
            }
        }
    }

    public bool IsRigidCircularShortPileFoundation
    {
        get => Project.FoundationSettings.FoundationType ==
               FoundationType.RigidShortPile;
        set
        {
            if (value)
            {
                SelectFoundationType(FoundationType.RigidShortPile);
            }
        }
    }

    public bool IsRigidRectangularShortPileFoundation
    {
        get => Project.FoundationSettings.FoundationType ==
               FoundationType.RigidRectangularShortPile;
        set
        {
            if (value)
            {
                SelectFoundationType(FoundationType.RigidRectangularShortPile);
            }
        }
    }

    public bool IsShallowFoundation =>
        Project.FoundationSettings.FoundationType is
            FoundationType.RectangularShortColumn or
            FoundationType.CircularShortColumn or
            FoundationType.Raft;

    public bool IsPileLikeFoundation =>
        Project.FoundationSettings.FoundationType is
            FoundationType.Pile or
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile;

    public bool IsMultiLegPileFoundation =>
        IsPileFoundation &&
        PileLayoutRules.GetPileCount(Project.TowerMast) > 1;

    public bool IsMultiLegFoundation =>
        PileLayoutRules.RequiresSingleLegReactions(
            Project.TowerMast,
            Project.FoundationSettings.FoundationType);

    public string PileLayoutSummary =>
        PileLayoutRules.DescribeFoundationLayout(
            Project.TowerMast,
            Project.FoundationSettings.FoundationType);

    public int PileCountDisplay =>
        PileLayoutRules.GetFoundationUnitCount(
            Project.TowerMast,
            Project.FoundationSettings.FoundationType);

    public bool IsBaseThicknessRelevant => IsShallowFoundation;

    public bool IsBasePlanRelevant =>
        IsShallowFoundation || IsRigidRectangularShortPileFoundation;

    public bool IsCircularPileDiameterRelevant =>
        IsPileFoundation || IsRigidCircularShortPileFoundation;

    public string FoundationTypeDisplay =>
        Project.FoundationSettings.FoundationType switch
        {
            FoundationType.RectangularShortColumn => "独立基础－矩形柱",
            FoundationType.CircularShortColumn => "独立基础－圆形柱",
            FoundationType.Raft => "中央塔柱筏板基础",
            FoundationType.RigidShortPile => "刚性短柱桩基础－圆形",
            FoundationType.RigidRectangularShortPile => "刚性短柱桩基础－矩形",
            FoundationType.Pile => "独立灌注桩及连梁基础",
            _ => "独立基础－矩形柱"
        };

    public double PedestalDiameterM
    {
        get => Project.FoundationSettings.PedestalDiameterM;
        set
        {
            Project.FoundationSettings.PedestalDiameterM = value;
            if (Project.FoundationSettings.FoundationType ==
                FoundationType.CircularShortColumn)
            {
                Project.FoundationSettings.PedestalLengthM = value;
                Project.FoundationSettings.PedestalWidthM = value;
            }

            OnPropertyChanged();
        }
    }

    public string ProjectTypeDisplay => Project.ProjectType switch
    {
        ProjectType.MonitoringPole => "监控杆基础",
        ProjectType.CommunicationTower => "通信塔桅基础",
        _ => "尚未选择"
    };

    public string LoadStepDescription => Project.ProjectType switch
    {
        ProjectType.MonitoringPole => "根据立杆、横杆和设备几何参数自动计算基础端荷载。",
        ProjectType.CommunicationTower => "可从企业标准塔型库选择，也可手工录入厂家提供的基础端控制荷载。",
        _ => "请先选择工程类型。"
    };

    public string ProgressText => Project.Stage switch
    {
        ProjectStage.Created when !HasProjectType => "0 / 6",
        ProjectStage.Created => "1 / 6",
        ProjectStage.SiteReady => "2 / 6",
        ProjectStage.GeotechnicalReady => "3 / 6",
        ProjectStage.LoadReady => "4 / 6",
        ProjectStage.CandidateReady => "5 / 6",
        ProjectStage.SchemeSelected or ProjectStage.Verified or ProjectStage.OutputReady => "6 / 6",
        _ => "1 / 6"
    };

    public int CurrentWorkflowStep => Project.Stage switch
    {
        ProjectStage.Created when !HasProjectType => 0,
        ProjectStage.Created => 1,
        ProjectStage.SiteReady => 2,
        ProjectStage.GeotechnicalReady => 3,
        ProjectStage.LoadReady or ProjectStage.CandidateReady => 4,
        ProjectStage.SchemeSelected or ProjectStage.Verified or ProjectStage.OutputReady => 5,
        _ => 0
    };

    public bool IsViewingCurrentWorkflowStep => SelectedStep == CurrentWorkflowStep;

    public bool IsBrowsingWorkflowStep => !IsViewingCurrentWorkflowStep;

    public bool IsBrowsingPastWorkflowStep => SelectedStep < CurrentWorkflowStep;

    public bool IsBrowsingFutureWorkflowStep => SelectedStep > CurrentWorkflowStep;

    public bool CanReviseViewedWorkflowStep => IsBrowsingPastWorkflowStep;

    public string WorkflowBrowseSummary => IsBrowsingWorkflowStep
        ? IsBrowsingPastWorkflowStep
            ? $"正在回看“{GetWorkflowStepName(SelectedStep)}”；可退回此步骤修改，后续方案和成果将自动作废。"
            : $"正在预览“{GetWorkflowStepName(SelectedStep)}”；当前应执行“{GetWorkflowStepName(CurrentWorkflowStep)}”，不能越级填写。"
        : $"当前执行：{GetWorkflowStepName(CurrentWorkflowStep)}";

    public string CurrentFileDisplay => CurrentFilePath ?? "尚未保存";

    public string LastOutputDisplay => LastOutputDirectory ?? "尚未导出成果包";

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (SetField(ref _currentFilePath, value))
            {
                OnPropertyChanged(nameof(CurrentFileDisplay));
            }
        }
    }

    public string? LastOutputDirectory
    {
        get => _lastOutputDirectory;
        private set
        {
            if (SetField(ref _lastOutputDirectory, value))
            {
                OnPropertyChanged(nameof(LastOutputDisplay));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string CustomResultSummary
    {
        get => _customResultSummary;
        private set => SetField(ref _customResultSummary, value);
    }

    public string SpecialtyReadinessSummary
    {
        get
        {
            var specialty = Project.FoundationSettings.SpecialtyDesign;
            var applicability = _specialtyAutoFillService.DetermineApplicability(Project);
            var simpleDecisions = new List<string>();
            var professionalReview = new List<string>();
            var confirmed = 0;
            if (applicability.NeedsDeformationLimits)
            {
                if (specialty.Deformation.Source.IsConfirmed)
                {
                    confirmed++;
                }
                else
                {
                    professionalReview.Add("塔型允许变形");
                }
            }
            if (applicability.NeedsSettlementParameters)
            {
                var isMindlinReview = Project.FoundationSettings.FoundationType == FoundationType.Pile &&
                    Project.FoundationSettings.Pile.SettlementMethod == PileSettlementMethod.MindlinReviewEstimate;
                if (specialty.Settlement.Source.IsConfirmed || isMindlinReview)
                {
                    confirmed++;
                    if (isMindlinReview)
                    {
                        professionalReview.Add("桩基正式沉降/试桩");
                    }
                }
                else
                {
                    professionalReview.Add("沉降分层或试桩资料");
                }
            }
            if (applicability.NeedsCrackParameters)
            {
                if (specialty.Crack.Source.IsConfirmed)
                {
                    confirmed++;
                }
                else
                {
                    simpleDecisions.Add("使用环境");
                }
            }
            if (applicability.NeedsPedestalStructuralParameters)
            {
                if (specialty.PedestalStructure.Source.IsConfirmed)
                {
                    confirmed++;
                }
                else
                {
                    simpleDecisions.Add("短柱材料候选");
                }
            }
            if (applicability.NeedsPileStructuralParameters)
            {
                if (Project.FoundationSettings.Pile.IsConfirmed)
                {
                    confirmed++;
                }
                else
                {
                    simpleDecisions.Add("桩身材料候选");
                }
            }
            if (applicability.NeedsHighWaterParameters)
            {
                if (specialty.Hydrogeology.Source.IsConfirmed)
                {
                    confirmed++;
                }
                else
                {
                    professionalReview.Add("设计最高水位");
                }
            }
            if (specialty.AnchorBolts.Source.IsConfirmed)
            {
                confirmed++;
            }
            else
            {
                if (applicability.NeedsAnchorDecision)
                {
                    simpleDecisions.Add("塔脚连接形式");
                }
                else if (applicability.NeedsAnchorParameters)
                {
                    professionalReview.Add("厂家锚栓详图");
                }
                else
                {
                    simpleDecisions.Add("连接形式确认");
                }
            }

            var progress = confirmed == 0
                ? "软件会在生成方案前自动采用规范默认和当前资料候选"
                : $"已自动处理 {confirmed} 类参数";
            var decisionText = simpleDecisions.Count == 0
                ? "当前没有必须立即填写的复杂表格"
                : $"若要形成完整结论，只需确认：{string.Join("、", simpleDecisions.Distinct())}";
            var reviewText = professionalReview.Count == 0
                ? string.Empty
                : $"；{string.Join("、", professionalReview.Distinct())}已转入交付前专业核对";
            return $"{progress}；{decisionText}{reviewText}，不用现在逐项填写。基础方案可以继续计算。";
        }
    }

    public string AiStatusText
    {
        get => _aiStatusText;
        private set => SetField(ref _aiStatusText, value);
    }

    public string AiStatusDetail
    {
        get => _aiStatusDetail;
        private set => SetField(ref _aiStatusDetail, value);
    }

    public string AiImportSummary
    {
        get => _aiImportSummary;
        private set => SetField(ref _aiImportSummary, value);
    }

    public ObservableCollection<GeotechnicalAnalysisRecord> GeotechnicalHistoryRecords { get; } = [];

    public ObservableCollection<MonitoringDrawingCandidate> MonitoringDrawingCandidates { get; } = [];

    public ObservableCollection<MonitoringManualCompletionItem> MonitoringMissingInputs { get; } = [];

    public MonitoringDrawingCandidate? SelectedMonitoringDrawingCandidate
    {
        get => _selectedMonitoringDrawingCandidate;
        set
        {
            if (!SetField(ref _selectedMonitoringDrawingCandidate, value))
            {
                return;
            }
            OnPropertyChanged(nameof(MonitoringDrawingCandidateSummary));
            RefreshMonitoringMissingInputs();
            ApplyMonitoringDrawingCandidateCommand.RaiseCanExecuteChanged();
        }
    }

    public string MonitoringDrawingCandidateSummary =>
        SelectedMonitoringDrawingCandidate is null
            ? "尚未识别监控杆图纸；也可继续完整手工录入。"
            : $"来源：{SelectedMonitoringDrawingCandidate.SourceFileName} 第{SelectedMonitoringDrawingCandidate.PageNumber}页；" +
              $"模型：{SelectedMonitoringDrawingCandidate.VisionModel}；{SelectedMonitoringDrawingCandidate.WarningSummary}";

    public bool HasMonitoringMissingInputs => MonitoringMissingInputs.Count > 0;

    public string MonitoringMissingInputSummary => MonitoringMissingInputs.Count == 0
        ? "AI候选与人工输入已经覆盖本次计算所需的图纸参数。"
        : $"还有{MonitoringMissingInputs.Count}项未形成可靠输入。请对照原图重新填写，输入框故意保持空白，不采用软件样例值。";

    public string ArmSegmentSummary
    {
        get
        {
            var segments = Project.MonitoringPole.ArmSegments;
            if (segments.Count == 0)
            {
                return "横杆未分段，按总参数计算。";
            }

            return string.Join("；", segments.Select((segment, index) =>
            {
                var position = segments.Count == 2
                    ? index == 0 ? "近端" : "远端"
                    : $"第{index + 1}段";
                return $"{position}{segment.LengthM:G4}m厚{segment.WallThicknessM * 1000:G4}mm" +
                       $"（{segment.NearDimensionM * 1000:G4}→{segment.FarDimensionM * 1000:G4}mm）";
            }));
        }
    }

    public GeotechnicalAnalysisRecord? SelectedGeotechnicalHistoryRecord
    {
        get => _selectedGeotechnicalHistoryRecord;
        set
        {
            if (!SetField(ref _selectedGeotechnicalHistoryRecord, value))
            {
                return;
            }

            UpdateGeotechnicalHistorySummary();
            ReuseGeotechnicalHistoryCommand.RaiseCanExecuteChanged();
            ReanalyzeGeotechnicalHistoryCommand.RaiseCanExecuteChanged();
            DeleteGeotechnicalHistoryCommand.RaiseCanExecuteChanged();
        }
    }

    public string GeotechnicalHistorySummary
    {
        get => _geotechnicalHistorySummary;
        private set => SetField(ref _geotechnicalHistorySummary, value);
    }

    public string AiProgressMessage
    {
        get => _aiProgressMessage;
        private set => SetField(ref _aiProgressMessage, value);
    }

    public bool IsAiProgressVisible
    {
        get => _isAiProgressVisible;
        private set => SetField(ref _isAiProgressVisible, value);
    }

    public bool IsAiProgressIndeterminate
    {
        get => _isAiProgressIndeterminate;
        private set => SetField(ref _isAiProgressIndeterminate, value);
    }

    public double AiProgressValue
    {
        get => _aiProgressValue;
        private set => SetField(ref _aiProgressValue, Math.Clamp(value, 0, 100));
    }

    public string WindPressureSummary
    {
        get => _windPressureSummary;
        private set => SetField(ref _windPressureSummary, value);
    }

    public string SeismicLocationSummary
    {
        get => _seismicLocationSummary;
        private set => SetField(ref _seismicLocationSummary, value);
    }

    public string WindPressureSourceBadge => Project.MonitoringPole.BasicWindPressureSourceType switch
    {
        _ when Project.MonitoringPole.IsMinimumBasicWindPressureApplied => "规范下限 0.35",
        BasicWindPressureSourceType.DirectNormativeStation => "规范直接值",
        BasicWindPressureSourceType.ParentCityReference => "城市参考值",
        BasicWindPressureSourceType.NearestStationManualReference => "人工参考台站",
        _ => "人工确认值"
    };

    public double BasicWindPressureKpa
    {
        get => Project.MonitoringPole.BasicWindPressureKpa;
        set
        {
            var adopted = Math.Max(
                MonitoringPoleInput.MinimumBasicWindPressureKpa,
                value);
            Project.MonitoringPole.SourceBasicWindPressureKpa = value;
            Project.MonitoringPole.BasicWindPressureKpa = adopted;
            Project.MonitoringPole.IsMinimumBasicWindPressureApplied =
                value < MonitoringPoleInput.MinimumBasicWindPressureKpa;
            if (!_isApplyingWindLookup)
            {
                Project.MonitoringPole.BasicWindPressureSourceType =
                    BasicWindPressureSourceType.Manual;
                Project.MonitoringPole.BasicWindPressureSourceStation = string.Empty;
                Project.MonitoringPole.BasicWindPressureSourceNote =
                    value < MonitoringPoleInput.MinimumBasicWindPressureKpa
                        ? $"手工输入基本风压{value:F2} kPa低于GB 50135-2019第4.2.1条下限，计算已按0.35 kPa采用。"
                        : "用户手工修改的基本风压，必须依据规范、当地气象资料或审图要求确认。";
                WindPressureSummary = Project.MonitoringPole.BasicWindPressureSourceNote;
                OnPropertyChanged(nameof(WindPressureSourceBadge));
            }

            OnPropertyChanged();
        }
    }

    public int SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (SetField(ref _selectedStep, Math.Clamp(value, 0, 5)))
            {
                NotifyWorkflowViewChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public double CustomBaseLengthM
    {
        get => _customBaseLengthM;
        set => SetField(ref _customBaseLengthM, value);
    }

    public double CustomBaseWidthM
    {
        get => _customBaseWidthM;
        set => SetField(ref _customBaseWidthM, value);
    }

    public double CustomBaseThicknessM
    {
        get => _customBaseThicknessM;
        set => SetField(ref _customBaseThicknessM, value);
    }

    public double CustomPileDiameterM
    {
        get => _customPileDiameterM;
        set => SetField(ref _customPileDiameterM, value);
    }

    public double CustomPileLengthM
    {
        get => _customPileLengthM;
        set => SetField(ref _customPileLengthM, value);
    }

    public double? PoleHeightInput
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleHeight, Project.MonitoringPole.PoleHeightM);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleHeight, value, v => Project.MonitoringPole.PoleHeightM = v);
    }

    public double? PoleBottomDiameterMm
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleBottomDimension, Project.MonitoringPole.PoleBottomDiameterM * 1000);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleBottomDimension, value, v => Project.MonitoringPole.PoleBottomDiameterM = v / 1000);
    }

    public double? PoleTopDiameterMm
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleTopDimension, Project.MonitoringPole.PoleTopDiameterM * 1000);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleTopDimension, value, v => Project.MonitoringPole.PoleTopDiameterM = v / 1000);
    }

    public double? PoleWallThicknessMm
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleWallThickness, Project.MonitoringPole.PoleWallThicknessM * 1000);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.PoleWallThickness, value, v => Project.MonitoringPole.PoleWallThicknessM = v / 1000);
    }

    public double? ArmMountingHeightInput
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmMountingHeight, Project.MonitoringPole.ArmMountingHeightM);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmMountingHeight, value, v => Project.MonitoringPole.ArmMountingHeightM = v);
    }

    public double? ArmLengthInput
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmLength, Project.MonitoringPole.ArmLengthM);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmLength, value, v => Project.MonitoringPole.ArmLengthM = v);
    }

    public double? ArmNearDiameterMm
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmNearDimension, Project.MonitoringPole.ArmNearDiameterM * 1000);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmNearDimension, value, v => Project.MonitoringPole.ArmNearDiameterM = v / 1000);
    }

    public double? ArmFarDiameterMm
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmFarDimension, Project.MonitoringPole.ArmFarDiameterM * 1000);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmFarDimension, value, v => Project.MonitoringPole.ArmFarDiameterM = v / 1000);
    }

    public double? ArmWallThicknessMm
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmWallThickness, Project.MonitoringPole.ArmWallThicknessM * 1000);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmWallThickness, value, v => Project.MonitoringPole.ArmWallThicknessM = v / 1000);
    }

    public int? ArmCountInput
    {
        get => IsExplicitDrawingValue(MonitoringDrawingFieldNames.ArmCount) ? Project.MonitoringPole.ArmCount : null;
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.ArmCount, value, v => Project.MonitoringPole.ArmCount = v);
    }

    public double? AttachmentProjectedAreaInput
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.AttachmentProjectedArea, Project.MonitoringPole.AttachmentProjectedAreaM2);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.AttachmentProjectedArea, value, v => Project.MonitoringPole.AttachmentProjectedAreaM2 = v);
    }

    public double? AttachmentWeightInput
    {
        get => GetExplicitDrawingValue(MonitoringDrawingFieldNames.AttachmentWeight, Project.MonitoringPole.AttachmentWeightKn);
        set => SetExplicitDrawingValue(MonitoringDrawingFieldNames.AttachmentWeight, value, v => Project.MonitoringPole.AttachmentWeightKn = v);
    }

    public RelayCommand NewProjectCommand { get; }

    public RelayCommand ReturnToCurrentWorkflowCommand { get; }

    public RelayCommand ReviseViewedWorkflowStepCommand { get; }

    public AsyncRelayCommand OpenProjectCommand { get; }

    public AsyncRelayCommand SaveProjectCommand { get; }

    public AsyncRelayCommand SaveProjectAsCommand { get; }

    public RelayCommand SelectMonitoringProjectCommand { get; }

    public RelayCommand SelectTowerProjectCommand { get; }

    public RelayCommand GenerateSchemesCommand { get; }

    public RelayCommand SelectSchemeCommand { get; }

    public RelayCommand UseCandidateAsCustomCommand { get; }

    public RelayCommand EvaluateCustomSchemeCommand { get; }

    public RelayCommand AdoptCustomSchemeCommand { get; }

    public AsyncRelayCommand ExportPrototypePackageCommand { get; }

    public AsyncRelayCommand ExportPackageAsCommand { get; }

    public AsyncRelayCommand ImportGeotechnicalWordCommand { get; }

    public AsyncRelayCommand ImportGeotechnicalPdfCommand { get; }

    public AsyncRelayCommand ImportGeotechnicalVisionPdfCommand { get; }

    public AsyncRelayCommand RecognizeMonitoringDrawingsCommand { get; }

    public RelayCommand ApplyMonitoringDrawingCandidateCommand { get; }

    public RelayCommand ApplyMonitoringMissingInputsCommand { get; }

    public RelayCommand ReuseGeotechnicalHistoryCommand { get; }

    public AsyncRelayCommand ReanalyzeGeotechnicalHistoryCommand { get; }

    public RelayCommand DeleteGeotechnicalHistoryCommand { get; }

    public RelayCommand ApplyTowerCatalogLoadCommand { get; }

    public void RefreshAiStatus()
    {
        var settings = _settingsService.Load();
        if (settings.AiMode == AiOperatingMode.OfflineOnly)
        {
            AiStatusText = "手动离线";
            AiStatusDetail = "已禁止云端请求；所有基础计算仍可正常使用。";
            return;
        }

        if (!settings.HasApiKey && !settings.HasVisionApiKey)
        {
            AiStatusText = "AI 待配置";
            AiStatusDetail = "默认采用 AI 在线优先，但尚未配置文字或视觉 API 密钥。";
            return;
        }

        AiStatusText = "AI 在线优先";
        var available = new List<string>();
        if (settings.HasVisionApiKey)
        {
            available.Add($"视觉 {settings.VisionModel}");
        }
        if (settings.HasApiKey)
        {
            available.Add($"文字 {settings.DeepSeekModel}");
        }
        AiStatusDetail = $"已配置{string.Join("、", available)}；调用失败时自动降级为OCR或手工录入。";
    }

    public void NavigateToStep(int step, ProjectStage? completedStage = null)
    {
        if (completedStage is not null && !IsViewingCurrentWorkflowStep)
        {
            ReturnToCurrentWorkflow();
            return;
        }

        if (completedStage is { } stage && (int)Project.Stage < (int)stage)
        {
            Project.Stage = stage;
            Project.ModifiedAt = DateTimeOffset.Now;
            OnPropertyChanged(nameof(Project));
            NotifyWorkflowPositionChanged();
        }

        SelectedStep = step;
    }

    public void ReturnToCurrentWorkflow()
    {
        var targetStep = CurrentWorkflowStep;
        SelectedStep = targetStep;
        StatusMessage = $"已回到当前执行流程：{GetWorkflowStepName(targetStep)}。";
    }

    private void ReviseViewedWorkflowStep()
    {
        if (!CanReviseViewedWorkflowStep)
        {
            return;
        }

        var targetStep = SelectedStep;
        var confirmation = targetStep == 0
            ? "退回工程类型会清除已经形成的荷载、方案选择和成果状态；原始录入值仍保留，重新选择业务类型后可继续修改。"
            : $"将从“{GetWorkflowStepName(targetStep)}”重新执行。该步骤之后形成的荷载、候选方案、已选方案和成果状态会自动作废，但已经录入的原始参数不会删除。";
        if (!SuppressErrorDialogsForAutomation &&
            AppDialogWindow.Show(
                confirmation + "\n\n确定退回修改吗？",
                "退回修改前序参数",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        InvalidateWorkflowAfter(targetStep);
        SelectedStep = targetStep;
        StatusMessage = $"已退回“{GetWorkflowStepName(targetStep)}”。修改完成后按正常“下一步”重新计算。";
    }

    private void InvalidateWorkflowAfter(int targetStep)
    {
        var step = Math.Clamp(targetStep, 0, 4);
        if (step == 0)
        {
            Project.ProjectType = ProjectType.NotSelected;
        }

        if (step <= 2)
        {
            Project.Geotechnical.IsConfirmed = false;
            Project.FoundationSettings.Pile.IsConfirmed = false;
            Project.FoundationSettings.RigidShortPile.IsConfirmed = false;
        }

        if (step <= 3)
        {
            Project.TowerMast.IsConfirmed = false;
            Project.FoundationLoad = new FoundationLoad();
            LoadPreview = null;
        }

        Project.Schemes.Clear();
        Project.SelectedSchemeId = null;
        Schemes.Clear();
        AdjustmentAdvices.Clear();
        SelectedCandidate = null;
        CustomScheme = null;
        ShouldExpandAdvancedDesignParameters = false;
        LastOutputDirectory = null;
        Project.Stage = step switch
        {
            0 or 1 => ProjectStage.Created,
            2 => ProjectStage.SiteReady,
            3 => ProjectStage.GeotechnicalReady,
            _ => ProjectStage.LoadReady
        };
        Project.ModifiedAt = DateTimeOffset.Now;
        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "退回修改前序参数",
            Details = $"从{GetWorkflowStepName(step)}重新执行；该步骤之后的计算荷载、候选方案、已选方案和成果状态已作废，原始录入值保留。"
        });
        OnPropertyChanged(nameof(Project));
        NotifyProjectTypeChanged();
        NotifyWorkflowPositionChanged();
        OnPropertyChanged(nameof(SelectedScheme));
        OnPropertyChanged(nameof(SpecialtyReadinessSummary));
    }

    public bool CanOperateWorkflowStep(int step) =>
        IsViewingCurrentWorkflowStep && CurrentWorkflowStep == step;

    public void ConfirmGeotechnicalInputs()
    {
        Project.Geotechnical.IsConfirmed = true;
        if (Project.FoundationSettings.FoundationType is
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile)
        {
            Project.FoundationSettings.RigidShortPile.IsConfirmed = true;
        }
        else if (Project.FoundationSettings.FoundationType == FoundationType.Pile)
        {
            Project.FoundationSettings.Pile.IsConfirmed = true;
        }

        Project.ModifiedAt = DateTimeOffset.Now;
        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "弹窗确认地勘参数",
            Details = $"已确认{FoundationTypeDisplay}对应的地勘参数和资料来源。"
        });
        OnPropertyChanged(nameof(Project));
        StatusMessage = "地勘参数已确认，已进入荷载输入。";
    }

    public void ConfirmSpecialtyInputs()
    {
        var specialty = Project.FoundationSettings.SpecialtyDesign;
        var applicability = _specialtyAutoFillService.DetermineApplicability(Project);
        specialty.Deformation.Source.IsConfirmed = !applicability.NeedsDeformationLimits ||
            specialty.Deformation.AllowableTopDisplacementMm > 0 &&
            specialty.Deformation.AllowableTopRotationRad > 0;
        specialty.Settlement.Source.IsConfirmed = !applicability.NeedsSettlementParameters ||
            (Project.FoundationSettings.FoundationType == FoundationType.Pile
                ? IsConfirmedPileSettlementInput(Project.FoundationSettings.Pile, specialty.Settlement)
                : specialty.Settlement.AllowableSettlementMm > 0 &&
                  specialty.Settlement.ExperienceCoefficient > 0 &&
                  specialty.Settlement.SoilLayers.Any(layer =>
                      layer.ThicknessM > 0 && layer.CompressionModulusMpa > 0));
        specialty.Crack.Source.IsConfirmed = !applicability.NeedsCrackParameters ||
            specialty.Crack.MaximumCrackWidthMm > 0 &&
            specialty.Crack.ConcreteTensileStrengthStandardMpa > 0 &&
            specialty.Crack.ReinforcementElasticModulusMpa > 0 &&
            !string.IsNullOrWhiteSpace(specialty.Crack.EnvironmentCategory) &&
            !specialty.Crack.EnvironmentCategory.Contains("待确认", StringComparison.Ordinal);
        specialty.AnchorBolts.Source.IsConfirmed =
            specialty.AnchorBolts.ConnectionType is AnchorConnectionType.DirectEmbedded or AnchorConnectionType.Other ||
            specialty.AnchorBolts.ConnectionType == AnchorConnectionType.AnchorBoltCage &&
            specialty.AnchorBolts.BoltCount >= 3 &&
            specialty.AnchorBolts.NominalDiameterMm > 0 &&
            specialty.AnchorBolts.BoltCircleDiameterM > 0 &&
            specialty.AnchorBolts.TensileStrengthDesignMpa > 0 &&
            specialty.AnchorBolts.ShearStrengthDesignMpa > 0 &&
            specialty.AnchorBolts.ThreadStressAreaFactor is > 0 and <= 1 &&
            specialty.AnchorBolts.EmbedmentDepthM > 0;
        specialty.AnchorBolts.ConcreteCapacitySource.IsConfirmed =
            specialty.AnchorBolts.ConcreteBreakoutCapacityKn > 0 &&
            specialty.AnchorBolts.PulloutCapacityKn > 0 &&
            specialty.AnchorBolts.EdgeBreakoutCapacityKn > 0;
        specialty.PedestalStructure.Source.IsConfirmed =
            !applicability.NeedsPedestalStructuralParameters ||
            specialty.PedestalStructure.ConcreteCompressiveStrengthMpa > 0 &&
            specialty.PedestalStructure.LongitudinalBarDiameterMm > 0 &&
            specialty.PedestalStructure.LongitudinalBarCount >= 6 &&
            specialty.PedestalStructure.MinimumLongitudinalReinforcementRatio > 0 &&
            specialty.PedestalStructure.StirrupDiameterMm > 0 &&
            specialty.PedestalStructure.StirrupSpacingMm > 0 &&
            specialty.PedestalStructure.StirrupLegCount >= 2;
        specialty.Hydrogeology.Source.IsConfirmed =
            !applicability.NeedsHighWaterParameters ||
            specialty.Hydrogeology.DesignHighGroundwaterDepthM >= 0 &&
            specialty.Hydrogeology.AntiFlotationSafetyFactor >= 1;
        specialty.SpecialGround.Source.IsConfirmed =
            specialty.SpecialGround.CollapsibleLoess != EngineeringRiskState.NotAssessed &&
            specialty.SpecialGround.Liquefaction != EngineeringRiskState.NotAssessed &&
            specialty.SpecialGround.FrostHeave != EngineeringRiskState.NotAssessed;

        SetManualSourceDefaults(specialty.Deformation.Source, "塔型或塔脚连接允许变形");
        SetManualSourceDefaults(specialty.Settlement.Source, "地勘沉降参数与结构允许值");
        SetManualSourceDefaults(specialty.Crack.Source, "环境类别与裂缝限值");
        SetManualSourceDefaults(specialty.AnchorBolts.Source, "塔脚锚栓详图");
        SetManualSourceDefaults(specialty.AnchorBolts.ConcreteCapacitySource, "锚栓混凝土节点承载力");
        SetManualSourceDefaults(specialty.PedestalStructure.Source, "短柱结构材料与配筋");
        SetManualSourceDefaults(specialty.Hydrogeology.Source, "设计最高水位与抗浮系数");
        SetManualSourceDefaults(specialty.SpecialGround.Source, "特殊地基结论与处理");
        Project.ModifiedAt = DateTimeOffset.Now;
        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "确认专项参数与来源",
            Details = SpecialtyReadinessSummary
        });
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(SpecialtyReadinessSummary));
        StatusMessage = SpecialtyReadinessSummary;
    }

    public SpecialtyAutoFillResult PrepareAutomaticDesignInputs()
    {
        var result = _specialtyAutoFillService.ApplyRecommendedDefaults(Project);
        ConfirmSpecialtyInputs();
        OnPropertyChanged(nameof(SpecialtyReadinessSummary));
        StatusMessage =
            $"设计参数已自动整理{(result.FilledCategoryCount > 0 ? $" {result.FilledCategoryCount} 类" : string.Empty)}，请在基础方案页开始自动设计；如默认搜索范围不足，可先调整尺寸上限。";
        return result;
    }

    public SpecialtyAutoFillResult ApplySpecialtyCompletionAndRecalculate()
    {
        var result = PrepareAutomaticDesignInputs();
        if (CustomScheme is not null)
        {
            EvaluateCustomScheme();
        }
        else
        {
            GenerateSchemes(applyAutomaticDefaults: false);
        }
        StatusMessage = "自动补齐和复算已完成。" + SpecialtyReadinessSummary;
        return result;
    }

    private static void SetManualSourceDefaults(
        EngineeringParameterSource source,
        string topic)
    {
        if (!source.IsConfirmed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(source.SourceDocument))
        {
            source.SourceDocument = "项目专项设计参数（人工录入）";
        }
        if (string.IsNullOrWhiteSpace(source.SourceLocation))
        {
            source.SourceLocation = topic;
        }
    }

    private static bool IsConfirmedPileSettlementInput(
        PileFoundationSettings pile,
        SettlementDesignInput settlement)
    {
        if (settlement.AllowableSettlementMm <= 0)
        {
            return false;
        }

        return pile.SettlementMethod switch
        {
            PileSettlementMethod.StaticLoadTestCurve =>
                pile.SettlementSource.IsConfirmed &&
                pile.StaticLoadTestCurve.Count(point => point.LoadKn >= 0 && point.SettlementMm >= 0) >= 2,
            PileSettlementMethod.ReviewedSpecialCalculation =>
                pile.SettlementSource.IsConfirmed &&
                pile.UseConfirmedServiceSettlement &&
                pile.ServiceSettlementFromTestOrSpecialCalculationMm >= 0,
            _ => false
        };
    }

    public void ConfirmLoadInputs()
    {
        if (Project.ProjectType == ProjectType.CommunicationTower)
        {
            Project.TowerMast.IsConfirmed = true;
        }

        Project.ModifiedAt = DateTimeOffset.Now;
        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "弹窗确认荷载输入",
            Details = Project.ProjectType == ProjectType.CommunicationTower
                ? IsMultiLegFoundation && Project.TowerMast.UsesIndividualPileReactions
                    ? $"已确认一个塔脚的标准组合与基本组合控制反力；{PileLayoutSummary}。"
                    : "已确认塔桅基础端标准组合与基本组合荷载、正负号、单位和控制工况。"
                : "已确认监控杆几何、风压来源及软件形成的标准组合与基本组合基础端荷载。"
        });
        OnPropertyChanged(nameof(Project));
        StatusMessage = "荷载输入已确认，可以生成三种基础方案。";
    }

    private void NewProject()
    {
        Project = CreateNewProject();
        Schemes.Clear();
        AdjustmentAdvices.Clear();
        SelectedCandidate = null;
        CustomScheme = null;
        LoadPreview = null;
        ShouldExpandAdvancedDesignParameters = false;
        CurrentFilePath = null;
        LastOutputDirectory = null;
        SelectedStep = 0;
        StatusMessage = "已创建空白项目，请选择工程类型。";
        CustomResultSummary = "输入尺寸后点击“复算自定义尺寸”。";
        AiImportSummary = "尚未进行 AI 地勘识别。";
    }

    private void SelectProjectType(ProjectType projectType)
    {
        if (Project.ProjectType == projectType)
        {
            if (SelectedStep == 0)
            {
                SelectedStep = 1;
            }

            return;
        }

        var previousType = Project.ProjectType;
        Project.ProjectType = projectType;
        Project.Stage = ProjectStage.Created;
        Project.Schemes.Clear();
        Project.SelectedSchemeId = null;
        Project.FoundationLoad = new FoundationLoad();
        Schemes.Clear();
        AdjustmentAdvices.Clear();
        SelectedCandidate = null;
        CustomScheme = null;
        LoadPreview = null;
        ShouldExpandAdvancedDesignParameters = false;

        if (previousType == ProjectType.NotSelected ||
            Project.Name is "新建基础设计项目" or "新建监控杆基础项目" or "新建通信塔桅基础项目")
        {
            Project.Name = projectType == ProjectType.MonitoringPole
                ? "新建监控杆基础项目"
                : "新建通信塔桅基础项目";
        }

        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "选择工程类型",
            Details = projectType == ProjectType.MonitoringPole
                ? "选择监控杆基础流程。"
                : "选择通信塔桅基础流程。"
        });
        Project.ModifiedAt = DateTimeOffset.Now;
        if (projectType == ProjectType.MonitoringPole && previousType != ProjectType.MonitoringPole)
        {
            Project.MonitoringPole.RequireExplicitDrawingInputs = true;
            Project.MonitoringPole.ExplicitDrawingInputFields ??= [];
            Project.MonitoringPole.ExplicitDrawingInputFields.Clear();
            _manualCompletionCandidateId = null;
            RefreshMonitoringMissingInputs();
        }
        if (string.IsNullOrWhiteSpace(Project.Province))
        {
            SelectedProvince = Provinces.FirstOrDefault(item =>
                item.Name.Equals("甘肃省", StringComparison.Ordinal));
        }

        NotifyProjectTypeChanged();
        OnPropertyChanged(nameof(Project));
        NotifyWorkflowPositionChanged();
        SelectedStep = 1;
        StatusMessage = $"已进入{ProjectTypeDisplay}流程。";
        RaiseCommandStates();
    }

    private void SelectFoundationType(FoundationType foundationType)
    {
        if (Project.FoundationSettings.FoundationType == foundationType)
        {
            return;
        }

        Project.FoundationSettings.FoundationType = foundationType;
        Project.Geotechnical.IsConfirmed = false;
        Project.FoundationSettings.Pile.IsConfirmed = false;
        Project.FoundationSettings.RigidShortPile.IsConfirmed = false;
        AiImportSummary =
            $"已切换为{FoundationTypeDisplay}。地勘字段模板已同步变化，请重新识别或人工确认。";
        if (foundationType == FoundationType.CircularShortColumn)
        {
            Project.FoundationSettings.PedestalLengthM =
                Project.FoundationSettings.PedestalDiameterM;
            Project.FoundationSettings.PedestalWidthM =
                Project.FoundationSettings.PedestalDiameterM;
        }
        else if (foundationType == FoundationType.RigidShortPile)
        {
            CustomPileDiameterM =
                Project.FoundationSettings.RigidShortPile.MinimumDiameterM;
            CustomPileLengthM =
                Project.FoundationSettings.RigidShortPile.MinimumEmbeddedDepthM;
        }
        else if (foundationType == FoundationType.RigidRectangularShortPile)
        {
            CustomBaseLengthM =
                Project.FoundationSettings.RigidShortPile.MinimumRectangularLengthM;
            CustomBaseWidthM =
                Project.FoundationSettings.RigidShortPile.MinimumRectangularWidthM;
            CustomPileLengthM =
                Project.FoundationSettings.RigidShortPile.MinimumEmbeddedDepthM;
        }

        Project.Schemes.Clear();
        Project.SelectedSchemeId = null;
        Schemes.Clear();
        AdjustmentAdvices.Clear();
        SelectedCandidate = null;
        CustomScheme = null;
        ShouldExpandAdvancedDesignParameters = false;
        Project.ModifiedAt = DateTimeOffset.Now;
        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "前置选择基础形式",
            Details = $"选择{FoundationTypeDisplay}；原地勘确认状态已撤销，需按新基础形式重新核对。"
        });
        NotifyFoundationTypeChanged();
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(SpecialtyReadinessSummary));
        StatusMessage =
            $"已切换为{FoundationTypeDisplay}。请确认对应参数后重新生成三种方案。";
    }

    private void ApplySelectedTowerCatalogLoad()
    {
        if (SelectedTowerCatalogRecord is null)
        {
            return;
        }

        try
        {
            var record = _enterpriseTowerLoadService.ApplyDesignLoads(
                Project,
                SelectedTowerCatalogRecord.Id);
            Schemes.Clear();
            AdjustmentAdvices.Clear();
            SelectedCandidate = null;
            CustomScheme = null;
            LoadPreview = null;
            TowerCatalogStatus =
                Project.TowerMast.UsesIndividualPileReactions
                    ? $"已同步回填 {record.TowerCode} 的一个塔脚标准组合和基本组合；{PileLayoutSummary}。请核对并确认。"
                    : $"已同步回填 {record.TowerCode} 的整塔基础端标准组合和基本组合；请核对原图集来源，并在下一步弹窗中确认。";
            OnPropertyChanged(nameof(Project));
            NotifyWorkflowPositionChanged();
            OnPropertyChanged(nameof(IsTowerManualLoad));
            OnPropertyChanged(nameof(IsTowerCatalogLoad));
            OnPropertyChanged(nameof(SelectedTowerStructureType));
            OnPropertyChanged(nameof(IsMultiLegPileFoundation));
            OnPropertyChanged(nameof(IsMultiLegFoundation));
            OnPropertyChanged(nameof(PileLayoutSummary));
            OnPropertyChanged(nameof(PileCountDisplay));
            StatusMessage = TowerCatalogStatus;
            RaiseCommandStates();
        }
        catch (Exception exception)
        {
            ShowError("企业塔型荷载不能自动采用", exception);
        }
    }

    private void ClearTowerCatalogProvenance()
    {
        var tower = Project.TowerMast;
        tower.CatalogRecordId = string.Empty;
        tower.CatalogSourceTitle = string.Empty;
        tower.CatalogStandardNo = string.Empty;
        tower.CatalogVersion = string.Empty;
        tower.CatalogPdfPage = 0;
        tower.CatalogTableRow = 0;
        tower.CatalogReviewStatus = string.Empty;
    }

    private bool ShouldUseSelectedSingleLegLoad()
    {
        if (SelectedTowerCatalogRecord is null)
        {
            return false;
        }

        return PileLayoutRules.RequiresSingleLegReactions(
            new TowerMastInput
            {
                StructureType = EnterpriseTowerLoadService.InferStructureType(
                    SelectedTowerCatalogRecord.TowerType)
            },
            Project.FoundationSettings.FoundationType);
    }

    private bool CanApplySelectedTowerCatalogRecord()
    {
        if (SelectedTowerCatalogRecord is null)
        {
            return false;
        }

        return ShouldUseSelectedSingleLegLoad()
            ? SelectedTowerCatalogRecord.CanApplySingleLegDesignLoads
            : SelectedTowerCatalogRecord.CanApplyOverallDesignLoads;
    }

    private async Task OpenProjectAsync()
    {
        IReadOnlyList<ProjectCatalogEntry> entries;
        IsBusy = true;
        try
        {
            entries = await _projectCatalogService.ListAsync();
        }
        catch (Exception exception)
        {
            ShowError("项目目录读取失败", exception);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var dialog = new ProjectCatalogWindow(
            entries,
            _projectCatalogService.ProjectDirectory)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.SelectedProjectPath))
        {
            return;
        }

        IsBusy = true;
        try
        {
            Project = await _repository.LoadAsync(dialog.SelectedProjectPath);
            var evaluatedStage = _projectReadinessService.Evaluate(Project);
            Project.Stage = Project.Stage == ProjectStage.OutputReady &&
                            evaluatedStage is ProjectStage.SchemeSelected or ProjectStage.Verified
                ? ProjectStage.OutputReady
                : evaluatedStage;
            NotifyWorkflowPositionChanged();
            Schemes.Clear();
            foreach (var scheme in Project.Schemes)
            {
                Schemes.Add(scheme);
            }

            SelectedCandidate = SelectedScheme ?? Schemes.FirstOrDefault();
            LoadPreview = HasMeaningfulLoad(Project.FoundationLoad)
                ? Project.FoundationLoad
                : null;
            CurrentFilePath = dialog.SelectedProjectPath;
            LastOutputDirectory = null;
            SelectedStep = CurrentWorkflowStep;
            if (SelectedCandidate is not null)
            {
                UseCandidateAsCustom();
            }

            ShouldExpandAdvancedDesignParameters = false;

            StatusMessage = $"已打开项目：{Project.Name}";
        }
        catch (Exception exception)
        {
            ShowError("项目打开失败", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveProjectAsync(bool forceChoosePath)
    {
        string? path;
        try
        {
            path = CurrentFilePath;
            if (forceChoosePath)
            {
                var dialog = new SaveFileDialog
                {
                    Title = "另存塔基智设项目",
                    Filter = "塔基智设项目 (*.tjproj)|*.tjproj",
                    AddExtension = true,
                    DefaultExt = ".tjproj",
                    FileName = MakeSafeFileName(Project.Name) + ".tjproj",
                    InitialDirectory = _projectCatalogService.ProjectDirectory
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                path = dialog.FileName;
            }
            else if (string.IsNullOrWhiteSpace(path))
            {
                path = _projectCatalogService.CreateDefaultProjectPath(Project.Name);
            }
        }
        catch (Exception exception)
        {
            ShowError("项目保存失败", exception);
            return;
        }

        IsBusy = true;
        try
        {
            await _repository.SaveAsync(Project, path);
            CurrentFilePath = path;
            StatusMessage = forceChoosePath
                ? $"项目已另存为：{path}"
                : $"项目已保存：{path}";
        }
        catch (Exception exception)
        {
            ShowError("项目保存失败", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void GenerateSchemes() => GenerateSchemes(applyAutomaticDefaults: true);

    private void GenerateSchemes(bool applyAutomaticDefaults)
    {
        IsBusy = true;
        try
        {
            EnsureEnterpriseCatalogLoadMatchesCurrentFoundation();
            SpecialtyAutoFillResult? autoFill = null;
            if (applyAutomaticDefaults)
            {
                autoFill = _specialtyAutoFillService.ApplyRecommendedDefaults(Project);
                ConfirmSpecialtyInputs();
            }

            var generated = _workflow.GenerateSchemes(Project);
            Schemes.Clear();
            foreach (var scheme in generated)
            {
                Schemes.Add(scheme);
            }

            LoadPreview = Project.FoundationLoad;
            SelectedCandidate = Schemes.FirstOrDefault(item =>
                    item.Preference == OptimizationPreference.Constructability) ??
                Schemes.FirstOrDefault();
            CustomScheme = null;
            AdjustmentAdvices.Clear();
            if (SelectedCandidate is not null)
            {
                UseCandidateAsCustom();
            }

            ShouldExpandAdvancedDesignParameters = false;

            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(SelectedScheme));
            NotifyWorkflowPositionChanged();
            OnPropertyChanged(nameof(SpecialtyReadinessSummary));
            RaiseOutputCommandsCanExecuteChanged();
            SelectedStep = 4;
            StatusMessage = autoFill is null
                ? $"已重新生成 {Schemes.Count} 个推荐方案，可直接确认或调整尺寸复算。"
                : $"已自动整理 {autoFill.FilledCategoryCount} 类设计参数并生成 {Schemes.Count} 个方案，默认选中施工型；可直接进入成果。";
        }
        catch (Exception exception)
        {
            SelectedStep = 4;
            ShouldExpandAdvancedDesignParameters = true;
            var title = exception is FoundationOptimizationException optimizationException
                ? optimizationException.DialogTitle
                : "基础方案生成失败";
            ShowError(title, exception);
            if (exception is FoundationOptimizationException diagnostic)
            {
                StatusMessage = diagnostic.StatusSummary;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void EnsureEnterpriseCatalogLoadMatchesCurrentFoundation()
    {
        if (Project.ProjectType != ProjectType.CommunicationTower ||
            Project.TowerMast.LoadSourceType != TowerLoadSourceType.EnterpriseCatalog ||
            string.IsNullOrWhiteSpace(Project.TowerMast.CatalogRecordId))
        {
            return;
        }

        var tower = Project.TowerMast;
        var requiresSingleLeg = PileLayoutRules.RequiresSingleLegReactions(
            tower,
            Project.FoundationSettings.FoundationType);
        var topologyMatches = tower.UsesIndividualPileReactions == requiresSingleLeg;
        var standardAndBasicLoadsAreComplete = requiresSingleLeg
            ? tower.IndividualPileCompressionKn > 0 &&
              tower.IndividualPileUpliftKn > 0 &&
              tower.IndividualPileHorizontalKn >= 0 &&
              tower.BasicIndividualPileCompressionKn > 0 &&
              tower.BasicIndividualPileUpliftKn > 0 &&
              tower.BasicIndividualPileHorizontalKn >= 0
            : tower.VerticalKn > 0 &&
              (tower.ShearXKn > 0 || tower.ShearYKn > 0) &&
              (tower.MomentXKnM > 0 || tower.MomentYKnM > 0) &&
              tower.BasicVerticalKn > 0 &&
              (tower.BasicShearXKn > 0 || tower.BasicShearYKn > 0) &&
              (tower.BasicMomentXKnM > 0 || tower.BasicMomentYKnM > 0);
        if (topologyMatches && standardAndBasicLoadsAreComplete)
        {
            return;
        }

        var wasConfirmed = tower.IsConfirmed;
        _enterpriseTowerLoadService.ApplyDesignLoads(
            Project,
            tower.CatalogRecordId);
        Project.TowerMast.IsConfirmed = wasConfirmed;
        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "自动同步企业塔型完整荷载",
            Details = requiresSingleLeg
                ? "生成方案前已按当前基础形式重新回填一个塔脚的标准组合和基本组合。"
                : "生成方案前已按当前基础形式重新回填整塔基础端标准组合和基本组合。"
        });
    }

    private void SelectScheme()
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        try
        {
            _workflow.SelectScheme(Project, SelectedCandidate.Id);
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(SelectedScheme));
            NotifyWorkflowPositionChanged();
            RaiseOutputCommandsCanExecuteChanged();
            StatusMessage = $"已确认“{SelectedCandidate.Name}”。";
            SelectedStep = 5;
        }
        catch (Exception exception)
        {
            ShowError("方案选择失败", exception);
        }
    }

    private void UseCandidateAsCustom()
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        CustomBaseLengthM = SelectedCandidate.Geometry.BaseLengthM;
        CustomBaseWidthM = SelectedCandidate.Geometry.BaseWidthM;
        CustomBaseThicknessM = SelectedCandidate.Geometry.BaseThicknessM;
        CustomPileDiameterM = SelectedCandidate.Geometry.PileDiameterM;
        CustomPileLengthM = SelectedCandidate.Geometry.PileLengthM;
        CustomScheme = null;
        AdjustmentAdvices.Clear();
        CustomResultSummary = $"已复制“{SelectedCandidate.Name}”尺寸，可修改后复算。";
    }

    private void EvaluateCustomScheme()
    {
        IsBusy = true;
        try
        {
            var foundationType = Project.FoundationSettings.FoundationType;
            var pedestalLength =
                foundationType == FoundationType.CircularShortColumn
                    ? Project.FoundationSettings.PedestalDiameterM
                    : Project.FoundationSettings.PedestalLengthM;
            var pedestalWidth =
                foundationType == FoundationType.CircularShortColumn
                    ? Project.FoundationSettings.PedestalDiameterM
                    : Project.FoundationSettings.PedestalWidthM;
            var geometry = new FoundationGeometry
            {
                BaseLengthM = CustomBaseLengthM,
                BaseWidthM = CustomBaseWidthM,
                BaseThicknessM = CustomBaseThicknessM,
                PedestalLengthM = pedestalLength,
                PedestalWidthM = pedestalWidth,
                PedestalHeightM = foundationType switch
                {
                    FoundationType.RigidShortPile =>
                        Project.FoundationSettings.RigidShortPile.AboveGroundHeightM,
                    FoundationType.RigidRectangularShortPile =>
                        Project.FoundationSettings.RigidShortPile.AboveGroundHeightM,
                    FoundationType.Pile =>
                        Project.FoundationSettings.Pile.AboveGroundHeightM,
                    _ => Project.FoundationSettings.PedestalHeightM
                },
                PileDiameterM = CustomPileDiameterM,
                PileLengthM = CustomPileLengthM,
                PileCount = Project.FoundationSettings.Pile.PileCount,
                PileCenterSpacingM = Project.FoundationSettings.Pile.PileCenterSpacingM,
                TieBeamCount = Project.FoundationSettings.Pile.TieBeamRequired
                    ? Project.FoundationSettings.Pile.PileCount
                    : 0,
                TieBeamWidthM = Project.FoundationSettings.Pile.TieBeamWidthM,
                TieBeamHeightM = Project.FoundationSettings.Pile.TieBeamHeightM
            };
            CustomScheme = _workflow.EvaluateCustomScheme(Project, geometry);
            AdjustmentAdvices.Clear();
            foreach (var item in _workflow.GetAdjustmentAdvice(Project, CustomScheme))
            {
                AdjustmentAdvices.Add(item);
            }

            CustomResultSummary = CustomScheme.IsFeasible
                ? $"{CustomScheme.VerificationConclusion}；已完成校核最大利用率{CustomScheme.MaximumUtilization:P0}。"
                : $"共有{CustomScheme.Checks.Count(item => item.Status == CheckStatus.Fail)}项不满足，请按下方建议调整。";
            StatusMessage = "自定义尺寸复算完成。";
        }
        catch (Exception exception)
        {
            ShowError("自定义尺寸复算失败", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AdoptCustomScheme()
    {
        if (CustomScheme?.IsFeasible != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _workflow.AddAndSelectCustomScheme(Project, CustomScheme);
            Schemes.Clear();
            foreach (var scheme in Project.Schemes)
            {
                Schemes.Add(scheme);
            }

            SelectedCandidate = Project.Schemes.Single(item => item.Id == Project.SelectedSchemeId);
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(SelectedScheme));
            NotifyWorkflowPositionChanged();
            RaiseOutputCommandsCanExecuteChanged();
            StatusMessage = "已采用自定义方案。";
            SelectedStep = 5;
        }
        catch (Exception exception)
        {
            ShowError("采用自定义方案失败", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportDesignPackageAsync(bool chooseDirectory)
    {
        var configuredExportDirectory = _settingsService.Load().DefaultExportDirectory;
        string parentDirectory;
        if (chooseDirectory)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择设计成果保存位置",
                Multiselect = false,
                InitialDirectory = configuredExportDirectory
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }
            parentDirectory = dialog.FolderName;
        }
        else
        {
            parentDirectory = configuredExportDirectory;
        }

        IsBusy = true;
        try
        {
            var result = await _outputService.ExportPrototypePackageAsync(
                Project,
                parentDirectory);
            LastOutputDirectory = result.DirectoryPath;
            OnPropertyChanged(nameof(Project));
            var folderOpened = TryOpenDirectory(result.DirectoryPath);
            StatusMessage = folderOpened
                ? $"已导出计算书、CAD、配筋材料表和工程量，共{result.Files.Count}个文件；成果文件夹已打开。"
                : $"已导出计算书、CAD、配筋材料表和工程量，共{result.Files.Count}个文件；未能自动打开文件夹，请点击下方路径查看。";
        }
        catch (Exception exception)
        {
            ShowError("设计成果导出失败", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool TryOpenDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            !Directory.Exists(directoryPath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directoryPath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private void RaiseOutputCommandsCanExecuteChanged()
    {
        ExportPrototypePackageCommand.RaiseCanExecuteChanged();
        ExportPackageAsCommand.RaiseCanExecuteChanged();
    }

    public void RefreshGeotechnicalHistoryFromSettings() =>
        RefreshGeotechnicalHistory();

    private void RefreshGeotechnicalHistory(Guid? preferredId = null)
    {
        var selectedId = preferredId ?? SelectedGeotechnicalHistoryRecord?.Id;
        IReadOnlyList<GeotechnicalAnalysisRecord> records;
        try
        {
            records = _geotechnicalHistoryService.Load();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            InvalidOperationException)
        {
            GeotechnicalHistorySummary =
                $"地勘分析记录目录暂时不可用：{exception.Message}。当前项目和手工计算不受影响。";
            StatusMessage = "地勘记录目录不可用，已保留当前项目与手工计算流程。";
            return;
        }

        GeotechnicalHistoryRecords.Clear();
        foreach (var record in records)
        {
            GeotechnicalHistoryRecords.Add(record);
        }

        SelectedGeotechnicalHistoryRecord = selectedId is { } id
            ? GeotechnicalHistoryRecords.FirstOrDefault(item => item.Id == id)
            : GeotechnicalHistoryRecords.FirstOrDefault(item =>
                  item.FoundationType == Project.FoundationSettings.FoundationType &&
                  item.CanReuse) ?? GeotechnicalHistoryRecords.FirstOrDefault();
        UpdateGeotechnicalHistorySummary();
    }

    private void UpdateGeotechnicalHistorySummary()
    {
        var count = GeotechnicalHistoryRecords.Count;
        if (count == 0)
        {
            GeotechnicalHistorySummary =
                "本机还没有地勘分析记录。首次分析成功后会自动保存，下次可直接引用。";
            return;
        }

        if (SelectedGeotechnicalHistoryRecord is not { } selected)
        {
            GeotechnicalHistorySummary = $"本机已保存 {count} 条地勘分析记录，请选择一条。";
            return;
        }

        if (!selected.CanReuse)
        {
            GeotechnicalHistorySummary =
                $"本机已保存 {count} 条；当前所选仅有本地OCR文字，请点“按当前基础重新分析”。";
            return;
        }

        if (selected.FoundationType != Project.FoundationSettings.FoundationType)
        {
            GeotechnicalHistorySummary =
                $"本机已保存 {count} 条；所选记录按“{selected.FoundationTypeDisplay}”提取，当前为“{FoundationTypeDisplay}”，请重新分析以补齐对应字段。";
            return;
        }

        GeotechnicalHistorySummary =
            $"本机已保存 {count} 条；所选结果可直接引用并回填，本次不会调用AI。";
    }

    private bool CanReuseSelectedGeotechnicalHistory() =>
        !IsBusy &&
        CanOperateWorkflowStep(2) &&
        SelectedGeotechnicalHistoryRecord is
        {
            CanReuse: true
        } selected &&
        selected.FoundationType == Project.FoundationSettings.FoundationType;

    private GeotechnicalAnalysisRecord? SaveGeotechnicalHistory(
        string sourcePath,
        GeotechnicalAnalysisMethod method,
        GeotechnicalDocumentImportResult import,
        string model,
        int pageCount = 0,
        int processedPageCount = 0,
        double meanConfidence = 0,
        IReadOnlyList<string>? warnings = null)
    {
        try
        {
            var file = new FileInfo(sourcePath);
            var ocr = import.OcrResult;
            var record = _geotechnicalHistoryService.Save(
                new GeotechnicalAnalysisRecord
                {
                    SourceFilePath = file.Exists ? file.FullName : sourcePath,
                    SourceName = file.Name,
                    SourceFileLength = file.Exists ? file.Length : 0,
                    SourceFileLastWriteTime = file.Exists ? file.LastWriteTimeUtc : null,
                    AnalysisMethod = method,
                    FoundationType = Project.FoundationSettings.FoundationType,
                    ProviderDisplay = import.AiProviderDisplay,
                    Model = model,
                    AiSourceType = import.AiSourceType,
                    EvidencePaneTitle = import.EvidencePaneTitle,
                    DocumentContent = import.Document.Content,
                    AiResult = import.AiResult,
                    PageCount = pageCount > 0 ? pageCount : ocr?.PageCount ?? 0,
                    ProcessedPageCount = processedPageCount > 0
                        ? processedPageCount
                        : ocr?.ProcessedPageCount ?? 0,
                    MeanConfidence = meanConfidence > 0
                        ? meanConfidence
                        : ocr?.MeanConfidence ?? 0,
                    Warnings = warnings ?? ocr?.Warnings ?? []
                });
            RefreshGeotechnicalHistory(record.Id);
            return record;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            GeotechnicalHistorySummary =
                $"本次分析已完成，但历史记录未能保存：{exception.Message}";
            return null;
        }
    }

    private void MarkGeotechnicalHistoryApplied(GeotechnicalAnalysisRecord? record)
    {
        if (record is null)
        {
            return;
        }

        try
        {
            _geotechnicalHistoryService.MarkApplied(record.Id);
            RefreshGeotechnicalHistory(record.Id);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            GeotechnicalHistorySummary =
                $"结果已填入项目，但使用状态未能写回本机记录：{exception.Message}";
        }
    }

    private void ReuseSelectedGeotechnicalHistory()
    {
        if (!CanReuseSelectedGeotechnicalHistory() ||
            SelectedGeotechnicalHistoryRecord is not { } record)
        {
            return;
        }

        var import = new GeotechnicalDocumentImportResult
        {
            Document = new DocumentTextExtractionResult
            {
                SourceName = record.SourceName,
                Content = record.DocumentContent
            },
            AiResult = record.AiResult,
            AiProviderDisplay = record.ProviderDisplay,
            AiSourceType = record.AiSourceType,
            EvidencePaneTitle = record.EvidencePaneTitle
        };
        if (!ApplyAiGeotechnicalResult(import))
        {
            StatusMessage = "历史地勘候选未采用，项目参数没有改变。";
            return;
        }

        MarkGeotechnicalHistoryApplied(record);
        StatusMessage =
            $"已引用“{record.SourceName}”的历史地勘分析结果，本次未调用AI。";
    }

    private async Task ReanalyzeSelectedGeotechnicalHistoryAsync()
    {
        if (SelectedGeotechnicalHistoryRecord is not { } record)
        {
            return;
        }

        var existingPath = File.Exists(record.SourceFilePath)
            ? record.SourceFilePath
            : null;
        switch (record.AnalysisMethod)
        {
            case GeotechnicalAnalysisMethod.WordTextAi:
                await ImportGeotechnicalWordAsync(existingPath);
                break;
            case GeotechnicalAnalysisMethod.VisualPdfAi:
                await ImportGeotechnicalVisionPdfAsync(existingPath);
                break;
            default:
                await ImportGeotechnicalPdfAsync(existingPath);
                break;
        }
    }

    private void DeleteSelectedGeotechnicalHistory()
    {
        if (SelectedGeotechnicalHistoryRecord is not { } record)
        {
            return;
        }

        if (!SuppressErrorDialogsForAutomation &&
            AppDialogWindow.Show(
                $"确定删除“{record.SourceName}”的这条地勘分析记录吗？\n\n" +
                "只删除已经保存的分析结果，不删除原地勘文件，也不改变当前项目参数。",
                "删除地勘分析记录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (_geotechnicalHistoryService.Delete(record.Id))
            {
                SelectedGeotechnicalHistoryRecord = null;
                RefreshGeotechnicalHistory();
                StatusMessage = $"已删除地勘分析记录：{record.SourceName}";
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError("地勘分析记录删除失败", exception);
        }
    }

    private Task ImportGeotechnicalWordAsync() => ImportGeotechnicalWordAsync(null);

    private async Task ImportGeotechnicalWordAsync(string? sourcePath)
    {
        RefreshAiStatus();
        var settings = _settingsService.Load();
        if (settings.AiMode == AiOperatingMode.OfflineOnly)
        {
            AppDialogWindow.Show(
                "当前为纯离线模式。你可以继续手工录入地勘参数，或到“设置”切换为 AI 在线优先。",
                "AI 已关闭",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StatusMessage = "当前为纯离线模式，已保留手工地勘入口。";
            return;
        }

        if (!settings.HasApiKey)
        {
            AppDialogWindow.Show(
                "尚未配置 DeepSeek API 密钥。请先打开右上角“设置”保存密钥；当前仍可手工录入。",
                "DeepSeek 待配置",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StatusMessage = "DeepSeek 待配置，已保留手工地勘入口。";
            return;
        }

        var selectedPath = sourcePath;
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            var dialog = new OpenFileDialog
            {
                Title = sourcePath is null
                    ? "选择地勘 Word 文档"
                    : "原地勘 Word 已移动，请重新选择文件",
                Filter = "Word 文档 (*.docx)|*.docx",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedPath = dialog.FileName;
        }

        IsBusy = true;
        BeginAiProgress("正在本机读取 Word，并准备 DeepSeek 双轮分析", indeterminate: true);
        StatusMessage = "正在本地读取 Word 正文和表格，并交给 DeepSeek 提取候选参数…";
        try
        {
            var aiProgress = new Progress<AiOperationProgress>(UpdateAiProgress);
            var import = await _documentImportService.ImportWordAsync(
                selectedPath,
                Project.FoundationSettings.FoundationType,
                aiProgress: aiProgress);
            var historyRecord = SaveGeotechnicalHistory(
                selectedPath,
                GeotechnicalAnalysisMethod.WordTextAi,
                import,
                settings.DeepSeekModel);
            if (!ApplyAiGeotechnicalResult(import))
            {
                StatusMessage = "AI候选未写入项目；分析结果已保存在本机历史记录中。";
                return;
            }
            MarkGeotechnicalHistoryApplied(historyRecord);
            AiStatusText = "AI 本次可用";
            AiStatusDetail = $"已使用 {settings.DeepSeekModel} 完成一次地勘文字提取。";
            StatusMessage =
                $"已从“{import.Document.SourceName}”提取候选参数，请逐项核对并在下一步弹窗中确认。";
        }
        catch (Exception exception)
        {
            AiStatusText = "AI 故障降级";
            AiStatusDetail = "本次连接或识别失败；已自动切回手工录入，基础计算不受影响。";
            var message = GetUserFacingExceptionMessage(exception);
            AiImportSummary = message;
            AppDialogWindow.Show(
                message + "\n\n你可以继续在当前页面手工录入参数。",
                "AI 识别未完成",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusMessage = "AI 识别未完成，已自动降级为手工录入。";
        }
        finally
        {
            EndAiProgress();
            IsBusy = false;
        }
    }

    private Task ImportGeotechnicalPdfAsync() => ImportGeotechnicalPdfAsync(null);

    private async Task ImportGeotechnicalPdfAsync(string? sourcePath)
    {
        var selectedPath = sourcePath;
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            var dialog = new OpenFileDialog
            {
                Title = sourcePath is null
                    ? "选择地勘 PDF 文档"
                    : "原地勘 PDF 已移动，请重新选择文件",
                Filter = "PDF 文档 (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedPath = dialog.FileName;
        }

        IsBusy = true;
        BeginAiProgress("正在检查 PDF 文字层并准备本地 OCR", indeterminate: false);
        StatusMessage = "正在启动本地 OCR，PDF 不会上传到云端…";
        try
        {
            var progress = new Progress<OcrProgress>(item =>
            {
                StatusMessage = item.Message;
                IsAiProgressIndeterminate = item.TotalPages <= 0;
                AiProgressValue = item.TotalPages <= 0
                    ? 0
                    : 65d * item.CurrentPage / item.TotalPages;
                AiProgressMessage = item.Message;
                AiImportSummary =
                    $"本机文档读取：第 {item.CurrentPage} / {item.TotalPages} 页。识别完成前仍可在下方查看已有手工参数。";
            });
            var aiProgress = new Progress<AiOperationProgress>(item =>
            {
                IsAiProgressIndeterminate = false;
                AiProgressValue = 65d + 35d * item.CurrentStep / Math.Max(1, item.TotalSteps);
                AiProgressMessage = item.Message;
                StatusMessage = item.Message;
            });
            var import = await _documentImportService.ImportPdfAsync(
                selectedPath,
                Project.FoundationSettings.FoundationType,
                progress,
                aiProgress: aiProgress);
            var ocr = import.OcrResult ??
                      throw new InvalidOperationException("PDF OCR 没有返回文档结果。");

            Project.Geotechnical.SourceType = ParameterSourceType.LocalOcr;
            Project.Geotechnical.IsConfirmed = false;
            Project.AuditTrail.Add(new AuditRecord
            {
                Action = ocr.ExtractionMode == PdfTextExtractionMode.NativeTextLayer
                    ? "本地 PDF 文字层读取"
                    : "本地 PDF OCR",
                Details =
                    $"来源文件：{ocr.SourceName}；方式：{ocr.ExtractionMode}；共{ocr.PageCount}页，已处理{ocr.ProcessedPageCount}页；平均置信度{ocr.MeanConfidence:P0}。提取文字未自动确认为设计参数。"
            });

            var settings = _settingsService.Load();
            var historyRecord = SaveGeotechnicalHistory(
                selectedPath,
                import.AiResult is null
                    ? GeotechnicalAnalysisMethod.PdfOcrOnly
                    : GeotechnicalAnalysisMethod.PdfOcrAi,
                import,
                import.AiResult is null ? "本地OCR" : settings.DeepSeekModel);
            if (import.AiResult is { } aiResult)
            {
                if (!ApplyAiGeotechnicalResult(import))
                {
                    StatusMessage = "本地OCR已完成；AI候选未采用，分析结果已保存在本机历史记录中。";
                    return;
                }
                MarkGeotechnicalHistoryApplied(historyRecord);
                AiStatusText = "OCR + AI 可用";
                AiStatusDetail =
                    $"PDF 已在本机完成 OCR，随后使用 {settings.DeepSeekModel} 整理候选字段；原始 PDF 未上传。";
                StatusMessage =
                    $"已完成“{ocr.SourceName}”本地 OCR 和 AI 候选提取，请对照原报告逐项确认。";
            }
            else
            {
                var warningText = ocr.Warnings.Count == 0
                    ? string.Empty
                    : $"；{string.Join("；", ocr.Warnings)}";
                AiImportSummary =
                    $"本地 OCR 已完成：{ocr.ProcessedPageCount}/{ocr.PageCount}页，平均置信度{ocr.MeanConfidence:P0}{warningText}。{import.AiSkipReason}参数仍由你在下方手工确认。";
                StatusMessage =
                    "本地 OCR 已完成；当前为离线或未配置 AI，已保留完整手工录入流程。";
            }

            OnPropertyChanged(nameof(Project));
        }
        catch (Exception exception)
        {
            var message = GetUserFacingExceptionMessage(exception);
            AiImportSummary = message;
            StatusMessage = "PDF OCR 未完成，已自动回到手工录入。";
            AppDialogWindow.Show(
                message + "\n\n你仍可继续手工录入并完成基础计算。",
                "本地 OCR 未完成",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            EndAiProgress();
            IsBusy = false;
        }
    }

    private Task ImportGeotechnicalVisionPdfAsync() =>
        ImportGeotechnicalVisionPdfAsync(null);

    private async Task ImportGeotechnicalVisionPdfAsync(string? sourcePath)
    {
        RefreshAiStatus();
        var settings = _settingsService.Load();
        if (settings.AiMode == AiOperatingMode.OfflineOnly)
        {
            AppDialogWindow.Show(
                "当前为纯离线模式。你可以使用本地PDF文字/OCR或继续手工录入。",
                "视觉 AI 已关闭",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StatusMessage = "当前为纯离线模式，已保留本地OCR和手工地勘入口。";
            return;
        }

        if (!settings.HasVisionApiKey)
        {
            AppDialogWindow.Show(
                "尚未配置百炼视觉 API。请在右上角“设置”导入业务空间CSV或填写视觉密钥。",
                "视觉 AI 待配置",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StatusMessage = "视觉AI待配置，仍可使用本地OCR或手工录入。";
            return;
        }

        var selectedPath = sourcePath;
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            var dialog = new OpenFileDialog
            {
                Title = sourcePath is null
                    ? "选择需要视觉大模型直接分析的地勘 PDF"
                    : "原地勘 PDF 已移动，请重新选择文件",
                Filter = "PDF 文档 (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedPath = dialog.FileName;
        }

        IsBusy = true;
        BeginAiProgress($"{settings.VisionModel} 正在准备直接观察PDF页面", indeterminate: false);
        StatusMessage = $"正在由 {settings.VisionModel} 直接读取PDF图像、表格和结论…";
        try
        {
            var progress = new Progress<AiOperationProgress>(UpdateAiProgress);
            var analysis = await _visualGeotechnicalAiService.AnalyzePdfAsync(
                selectedPath,
                GeotechnicalDocumentImportService.BuildFoundationSpecificRequirements(
                    Project.FoundationSettings.FoundationType),
                progress,
                switchOptions: CreateVisionModelSwitchOptions());
            var import = new GeotechnicalDocumentImportResult
            {
                Document = new DocumentTextExtractionResult
                {
                    SourceName = $"{analysis.SourceName}（{analysis.Model}视觉直读）",
                    Content = analysis.EvidenceText
                },
                AiResult = analysis.AiResult,
                AiProviderDisplay = $"百炼视觉 {analysis.Model}",
                AiSourceType = ParameterSourceType.VisualAi,
                EvidencePaneTitle = "视觉模型逐页证据摘录"
            };
            var historyRecord = SaveGeotechnicalHistory(
                selectedPath,
                GeotechnicalAnalysisMethod.VisualPdfAi,
                import,
                analysis.Model,
                analysis.PageCount,
                analysis.ProcessedPageCount,
                warnings: analysis.Warnings);
            if (!ApplyAiGeotechnicalResult(import))
            {
                StatusMessage = "视觉AI候选未采用，项目参数未改变；分析结果已保存在本机历史记录中。";
                return;
            }
            MarkGeotechnicalHistoryApplied(historyRecord);

            AiStatusText = "视觉 AI 本次可用";
            AiStatusDetail =
                $"{analysis.Model} 已直接观察 {analysis.ProcessedPageCount}/{analysis.PageCount} 页；候选值已按证据复核后填入。";
            var warningText = analysis.Warnings.Count == 0
                ? string.Empty
                : $"；{string.Join("；", analysis.Warnings)}";
            StatusMessage = $"视觉地勘分析完成，候选参数已经确认并填入{warningText}";
            OnPropertyChanged(nameof(Project));
        }
        catch (Exception exception)
        {
            AiStatusText = "视觉 AI 故障降级";
            AiStatusDetail = "本次视觉识别失败；可改用本地OCR或手工录入，基础计算不受影响。";
            var message = GetUserFacingExceptionMessage(exception);
            AiImportSummary = message;
            AppDialogWindow.Show(
                message + "\n\n你可以改用本地PDF文字/OCR，或继续手工录入。",
                "视觉地勘分析未完成",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusMessage = "视觉地勘分析未完成，已安全降级。";
        }
        finally
        {
            EndAiProgress();
            IsBusy = false;
        }
    }

    private async Task RecognizeMonitoringDrawingsAsync()
    {
        RefreshAiStatus();
        var settings = _settingsService.Load();
        if (settings.AiMode == AiOperatingMode.OfflineOnly)
        {
            AppDialogWindow.Show(
                "当前为纯离线模式。监控杆参数仍可完整手工录入并由本地内核计算。",
                "视觉识图已关闭",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (!settings.HasVisionApiKey)
        {
            AppDialogWindow.Show(
                "尚未配置视觉API。请在设置中导入业务空间CSV或填写视觉密钥；手工录入不受影响。",
                "视觉识图待配置",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择一个或多个监控杆施工图 PDF",
            Filter = "PDF 文档 (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Project.MonitoringDrawingCandidates ??= [];
        var pendingPaths = new List<string>();
        var reusedFromProject = new List<MonitoringDrawingCandidate>();
        var reusedFromHistory = new List<MonitoringDrawingCandidate>();
        foreach (var path in dialog.FileNames)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var cached = Project.MonitoringDrawingCandidates
                .Where(candidate => candidate.SourceFileSha256.Equals(
                    hash,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (cached.Length > 0)
            {
                reusedFromProject.AddRange(cached);
            }
            else
            {
                var historical = _monitoringDrawingRecognitionHistoryService
                    .FindBySourceHash(hash);
                if (historical.Count > 0)
                {
                    reusedFromHistory.AddRange(historical);
                    foreach (var candidate in historical)
                    {
                        Project.MonitoringDrawingCandidates.RemoveAll(existing =>
                            existing.SourceFileSha256.Equals(
                                candidate.SourceFileSha256,
                                StringComparison.OrdinalIgnoreCase) &&
                            existing.PageNumber == candidate.PageNumber);
                        Project.MonitoringDrawingCandidates.Add(candidate);
                    }
                }
                else
                {
                    pendingPaths.Add(path);
                }
            }
        }

        IsBusy = true;
        BeginAiProgress(
            pendingPaths.Count == 0
                ? "正在从项目或本机记录读取已识别候选"
                : $"{settings.VisionModel} 正在准备识别 {pendingPaths.Count} 个PDF",
            indeterminate: false);
        try
        {
            var result = pendingPaths.Count == 0
                ? new MonitoringDrawingVisionBatchResult()
                : await _monitoringDrawingVisionAiService.AnalyzePdfsAsync(
                    pendingPaths,
                    new Progress<AiOperationProgress>(UpdateAiProgress),
                    switchOptions: CreateVisionModelSwitchOptions());

            foreach (var candidate in result.Candidates)
            {
                Project.MonitoringDrawingCandidates.RemoveAll(existing =>
                    existing.SourceFileSha256.Equals(
                        candidate.SourceFileSha256,
                        StringComparison.OrdinalIgnoreCase) &&
                    existing.PageNumber == candidate.PageNumber);
                Project.MonitoringDrawingCandidates.Add(candidate);
            }
            _monitoringDrawingRecognitionHistoryService.Save(result.Candidates);
            RefreshMonitoringDrawingCandidates();
            SelectedMonitoringDrawingCandidate = result.Candidates.FirstOrDefault() ??
                                                 reusedFromHistory.FirstOrDefault() ??
                                                 reusedFromProject.FirstOrDefault() ??
                                                 SelectedMonitoringDrawingCandidate;
            Project.ModifiedAt = DateTimeOffset.Now;

            var reusedCount = reusedFromProject.Count + reusedFromHistory.Count;
            var successCount = result.Candidates.Count + reusedCount;
            var failureText = result.Failures.Count == 0
                ? string.Empty
                : $"；失败{result.Failures.Count}项：{string.Join("；", result.Failures)}";
            StatusMessage = pendingPaths.Count == 0
                ? $"已复用{reusedCount}个图纸候选（项目{reusedFromProject.Count}个、本机记录{reusedFromHistory.Count}个），无需重新识别。"
                : $"视觉识图完成：得到{successCount}个候选，新增结果已保存到本机识别记录，请核对证据后采用{failureText}";
            OnPropertyChanged(nameof(MonitoringDrawingCandidateSummary));

            if (successCount == 0 && result.Failures.Count > 0)
            {
                AppDialogWindow.Show(
                    string.Join(Environment.NewLine, result.Failures) +
                    "\n\n未用人工基准替代模型结果；可继续手工录入。",
                    "监控杆图纸视觉识别未完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowError("监控杆图纸视觉识别未完成", exception);
        }
        finally
        {
            EndAiProgress();
            IsBusy = false;
        }
    }

    private static VisionModelSwitchOptions CreateVisionModelSwitchOptions() => new()
    {
        ConfirmAsync = (request, _) =>
        {
            bool Confirm()
            {
                var result = AppDialogWindow.Show(
                    $"{request.Operation}未能由当前视觉模型完成。\n\n" +
                    $"当前模型：{request.CurrentModel}\n" +
                    $"建议切换：{request.ProposedModel}\n" +
                    $"失败原因：{request.FailureReason}\n\n" +
                    "是否仅为本次任务切换到建议模型？选择“否”将停止本次切换，已有数据不会被覆盖。",
                    "确认更换视觉模型",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                return result == MessageBoxResult.Yes;
            }

            return System.Windows.Application.Current.Dispatcher.CheckAccess()
                ? Task.FromResult(Confirm())
                : System.Windows.Application.Current.Dispatcher.InvokeAsync(Confirm).Task;
        }
    };

    private void ApplySelectedMonitoringDrawingCandidate()
    {
        if (SelectedMonitoringDrawingCandidate is null)
        {
            return;
        }

        var result = MonitoringDrawingCandidateApplicator.Apply(
            Project,
            SelectedMonitoringDrawingCandidate);
        if (result.AppliedFieldCount > 0)
        {
            _monitoringDrawingRecognitionHistoryService.MarkApplied(
                SelectedMonitoringDrawingCandidate.Id);
        }
        _manualCompletionCandidateId = SelectedMonitoringDrawingCandidate.Id;
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(PoleBottomDiameterMm));
        OnPropertyChanged(nameof(PoleTopDiameterMm));
        OnPropertyChanged(nameof(PoleWallThicknessMm));
        OnPropertyChanged(nameof(ArmNearDiameterMm));
        OnPropertyChanged(nameof(ArmFarDiameterMm));
        OnPropertyChanged(nameof(ArmWallThicknessMm));
        OnPropertyChanged(nameof(PoleHeightInput));
        OnPropertyChanged(nameof(ArmMountingHeightInput));
        OnPropertyChanged(nameof(ArmLengthInput));
        OnPropertyChanged(nameof(ArmCountInput));
        OnPropertyChanged(nameof(AttachmentProjectedAreaInput));
        OnPropertyChanged(nameof(AttachmentWeightInput));
        OnPropertyChanged(nameof(SelectedPoleSectionType));
        OnPropertyChanged(nameof(SelectedArmSectionType));
        OnPropertyChanged(nameof(PoleSectionTypeDisplay));
        OnPropertyChanged(nameof(ArmSectionTypeDisplay));
        OnPropertyChanged(nameof(ArmSegmentSummary));
        OnPropertyChanged(nameof(MonitoringDrawingCandidateSummary));
        RefreshMonitoringMissingInputs();

        var applySummary = result.AppliedFieldCount > 0
            ? $"已采用并填入{result.AppliedFieldCount}个图纸候选字段；未识别或未采用字段已进入第二次人工补录。"
            : "没有字段被回填；低置信或冲突字段必须同时勾选“采用”和“人工确认”。";
        StatusMessage = result.Messages.Count > 0
            ? $"{applySummary} {string.Join(" ", result.Messages)}"
            : applySummary;
        if (result.AppliedFieldCount == 0 && !SuppressErrorDialogsForAutomation)
        {
            AppDialogWindow.Show(
                StatusMessage,
                "候选尚未采用",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void ApplyMonitoringMissingInputs()
    {
        if (MonitoringMissingInputs.Count == 0)
        {
            return;
        }

        var parsed = new Dictionary<string, double>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in MonitoringMissingInputs)
        {
            if (!TryParseManualNumber(item.InputText, out var value))
            {
                errors.Add($"{item.DisplayName}必须填写有效数字，不能留空。");
                continue;
            }

            var definition = MonitoringInputDefinitions.First(definition =>
                definition.FieldName == item.FieldName);
            if (item.FieldName == MonitoringDrawingFieldNames.ArmCount &&
                (value < 1 || Math.Abs(value - Math.Round(value)) > 1e-9))
            {
                errors.Add("横杆数量必须填写不小于1的整数。");
                continue;
            }
            if (definition.AllowZero ? value < 0 : value <= 0)
            {
                errors.Add($"{item.DisplayName}{(definition.AllowZero ? "不得小于0" : "必须大于0")}。");
                continue;
            }

            parsed[item.FieldName] = value;
        }

        if (errors.Count == 0)
        {
            var poleHeight = ManualOrCurrent(parsed, MonitoringDrawingFieldNames.PoleHeight, Project.MonitoringPole.PoleHeightM);
            var mountingHeight = ManualOrCurrent(parsed, MonitoringDrawingFieldNames.ArmMountingHeight, Project.MonitoringPole.ArmMountingHeightM);
            var poleBottom = ManualOrCurrent(parsed, MonitoringDrawingFieldNames.PoleBottomDimension, Project.MonitoringPole.PoleBottomDiameterM * 1000);
            var poleTop = ManualOrCurrent(parsed, MonitoringDrawingFieldNames.PoleTopDimension, Project.MonitoringPole.PoleTopDiameterM * 1000);
            var armNear = ManualOrCurrent(parsed, MonitoringDrawingFieldNames.ArmNearDimension, Project.MonitoringPole.ArmNearDiameterM * 1000);
            var armFar = ManualOrCurrent(parsed, MonitoringDrawingFieldNames.ArmFarDimension, Project.MonitoringPole.ArmFarDiameterM * 1000);
            if (poleHeight is < 2 or > 30)
            {
                errors.Add("立杆高度应在2～30m范围内。");
            }
            if (mountingHeight > poleHeight)
            {
                errors.Add("横杆安装高度不得超过立杆高度。");
            }
            if (poleTop > poleBottom)
            {
                errors.Add("立杆上端尺寸不得大于下端尺寸。");
            }
            if (armFar > armNear)
            {
                errors.Add("横杆远端尺寸不得大于近端尺寸。");
            }
        }

        if (errors.Count > 0)
        {
            StatusMessage = string.Join(" ", errors);
            if (!SuppressErrorDialogsForAutomation)
            {
                AppDialogWindow.Show(
                    StatusMessage,
                    "请补齐AI未识别参数",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return;
        }

        foreach (var item in MonitoringMissingInputs.ToArray())
        {
            ApplyManualCompletionValue(item.FieldName, parsed[item.FieldName]);
        }

        Project.ModifiedAt = DateTimeOffset.Now;
        Project.AuditTrail.Add(new AuditRecord
        {
            Action = "人工补齐监控杆图纸缺失参数",
            Details = $"补录{parsed.Count}项：{string.Join("、", MonitoringMissingInputs.Select(item => item.DisplayName))}。"
        });
        NotifyMonitoringDrawingInputsChanged();
        RefreshMonitoringMissingInputs();
        StatusMessage = "AI未识别或未采用的参数已经人工补齐，可以继续核对并进入基础方案。";
    }

    private void ApplyManualCompletionValue(string fieldName, double value)
    {
        var input = Project.MonitoringPole;
        switch (fieldName)
        {
            case MonitoringDrawingFieldNames.PoleHeight:
                input.PoleHeightM = value;
                break;
            case MonitoringDrawingFieldNames.PoleBottomDimension:
                input.PoleBottomDiameterM = value / 1000;
                break;
            case MonitoringDrawingFieldNames.PoleTopDimension:
                input.PoleTopDiameterM = value / 1000;
                break;
            case MonitoringDrawingFieldNames.PoleWallThickness:
                input.PoleWallThicknessM = value / 1000;
                break;
            case MonitoringDrawingFieldNames.ArmMountingHeight:
                input.ArmMountingHeightM = value;
                break;
            case MonitoringDrawingFieldNames.ArmLength:
                input.ArmLengthM = value;
                break;
            case MonitoringDrawingFieldNames.ArmNearDimension:
                input.ArmNearDiameterM = value / 1000;
                break;
            case MonitoringDrawingFieldNames.ArmFarDimension:
                input.ArmFarDiameterM = value / 1000;
                break;
            case MonitoringDrawingFieldNames.ArmWallThickness:
                input.ArmWallThicknessM = value / 1000;
                break;
            case MonitoringDrawingFieldNames.ArmCount:
                input.ArmCount = (int)Math.Round(value);
                break;
            case MonitoringDrawingFieldNames.AttachmentProjectedArea:
                input.AttachmentProjectedAreaM2 = value;
                break;
            case MonitoringDrawingFieldNames.AttachmentWeight:
                input.AttachmentWeightKn = value;
                break;
        }

        input.ExplicitDrawingInputFields ??= [];
        input.ExplicitDrawingInputFields.Add(fieldName);
    }

    private static bool TryParseManualNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static double ManualOrCurrent(
        IReadOnlyDictionary<string, double> values,
        string fieldName,
        double currentValue) =>
        values.TryGetValue(fieldName, out var value) ? value : currentValue;

    private bool ApplyAiGeotechnicalResult(
        GeotechnicalDocumentImportResult import)
    {
        var reviewWindow = new GeotechnicalEvidenceReviewWindow(import)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (reviewWindow.ShowDialog() != true)
        {
            AiImportSummary =
                "AI候选未写入项目，手工参数保持不变；文件分析结果仍保存在本机历史记录中，可稍后再次引用。";
            return false;
        }

        var application = _documentImportService.ApplyAiCandidates(Project, import);
        var contextAssignments = ApplyAiProjectContext(import.AiResult);
        AiImportSummary = contextAssignments.Count == 0
            ? application.Summary
            : $"已直接填入：{string.Join("、", contextAssignments)}；{application.Summary}";
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SpecialtyReadinessSummary));
        return true;
    }

    private List<string> ApplyAiProjectContext(GeotechnicalAiExtractionResult? result)
    {
        var assigned = new List<string>();
        if (result is null)
        {
            return assigned;
        }

        var location = string.Join(
            string.Empty,
            result.Province,
            result.City,
            result.County,
            result.SiteLocation);
        if (string.IsNullOrWhiteSpace(location))
        {
            return assigned;
        }

        var province = Provinces.FirstOrDefault(item =>
            ContainsAdministrativeName(location, item.Name));
        RegionOption? city = null;
        RegionOption? county = null;
        if (province is null)
        {
            foreach (var provinceCandidate in Provinces)
            {
                foreach (var cityCandidate in _regionWindCatalog.GetCities(provinceCandidate.Code))
                {
                    var countyCandidate = _regionWindCatalog
                        .GetCounties(cityCandidate.Code)
                        .FirstOrDefault(item => ContainsAdministrativeName(location, item.Name));
                    if (countyCandidate is null)
                    {
                        continue;
                    }

                    province = provinceCandidate;
                    city = cityCandidate;
                    county = countyCandidate;
                    break;
                }
                if (province is not null)
                {
                    break;
                }
            }
        }

        if (province is not null)
        {
            if (_selectedProvince?.Code != province.Code)
            {
                SelectedProvince = province;
                assigned.Add($"省“{province.Name}”");
            }

            city ??= Cities.FirstOrDefault(item =>
                ContainsAdministrativeName(location, item.Name));
        }

        if (city is not null && _selectedCity?.Code != city.Code)
        {
            SelectedCity = city;
            assigned.Add($"市“{city.Name}”");
        }

        county ??= Counties.FirstOrDefault(item =>
            ContainsAdministrativeName(location, item.Name));
        if (county is not null && _selectedCounty?.Code != county.Code)
        {
            SelectedCounty = county;
            assigned.Add($"县区“{county.Name}”");
        }

        RefreshLocationSeismicReference();
        return assigned;
    }

    private static bool ContainsAdministrativeName(string text, string name)
    {
        if (text.Contains(name, StringComparison.Ordinal))
        {
            return true;
        }

        var normalized = name;
        foreach (var suffix in new[]
                 {
                     "特别行政区", "自治区", "自治州", "自治县", "地区",
                     "省", "市", "县", "区"
                 })
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        return normalized.Length >= 2 && text.Contains(normalized, StringComparison.Ordinal);
    }

    private void BeginAiProgress(string message, bool indeterminate)
    {
        AiProgressMessage = message;
        AiProgressValue = 0;
        IsAiProgressIndeterminate = indeterminate;
        IsAiProgressVisible = true;
    }

    private void UpdateAiProgress(AiOperationProgress progress)
    {
        IsAiProgressIndeterminate = false;
        AiProgressValue = 100d * progress.CurrentStep / Math.Max(1, progress.TotalSteps);
        AiProgressMessage = progress.Message;
        StatusMessage = progress.Message;
    }

    private void EndAiProgress()
    {
        IsAiProgressVisible = false;
        IsAiProgressIndeterminate = false;
        AiProgressValue = 0;
        AiProgressMessage = string.Empty;
    }

    private void ShowError(string title, Exception exception)
    {
        var message = GetUserFacingExceptionMessage(exception);
        StatusMessage = message;
        if (!SuppressErrorDialogsForAutomation)
        {
            AppDialogWindow.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string GetUserFacingExceptionMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null &&
               (current is System.Reflection.TargetInvocationException ||
                current is TypeInitializationException ||
                current is AggregateException))
        {
            current = current.InnerException;
        }

        if (current is DllNotFoundException)
        {
            return "本地 OCR 运行组件缺失或没有被正确加载。请使用完整发布文件夹中的程序，"
                   + "并保留同目录下的 x64 文件夹。";
        }

        return string.IsNullOrWhiteSpace(current.Message)
            ? "处理过程中发生了未能识别的错误。"
            : current.Message;
    }

    private void NotifyWorkflowPositionChanged()
    {
        OnPropertyChanged(nameof(CurrentWorkflowStep));
        OnPropertyChanged(nameof(ProgressText));
        NotifyWorkflowViewChanged();
    }

    private void NotifyWorkflowViewChanged()
    {
        OnPropertyChanged(nameof(IsViewingCurrentWorkflowStep));
        OnPropertyChanged(nameof(IsBrowsingWorkflowStep));
        OnPropertyChanged(nameof(IsBrowsingPastWorkflowStep));
        OnPropertyChanged(nameof(IsBrowsingFutureWorkflowStep));
        OnPropertyChanged(nameof(CanReviseViewedWorkflowStep));
        OnPropertyChanged(nameof(WorkflowBrowseSummary));
        RaiseCommandStates();
    }

    private static string GetWorkflowStepName(int step) => step switch
    {
        0 => "① 工程类型",
        1 => "② 项目与基础",
        2 => "③ 地勘参数",
        3 => "④ 荷载输入",
        4 => "⑤ 基础方案",
        5 => "⑥ 成果与记录",
        _ => "当前步骤"
    };

    private void NotifyProjectTypeChanged()
    {
        OnPropertyChanged(nameof(HasProjectType));
        OnPropertyChanged(nameof(IsMonitoringProject));
        OnPropertyChanged(nameof(IsTowerProject));
        OnPropertyChanged(nameof(ProjectTypeDisplay));
        OnPropertyChanged(nameof(LoadStepDescription));
        OnPropertyChanged(nameof(IsTowerManualLoad));
        OnPropertyChanged(nameof(IsTowerCatalogLoad));
    }

    private void NotifyFoundationTypeChanged()
    {
        PileLayoutRules.Synchronize(Project);
        Project.TowerMast.UsesIndividualPileReactions =
            PileLayoutRules.RequiresSingleLegReactions(
                Project.TowerMast,
                Project.FoundationSettings.FoundationType);
        OnPropertyChanged(nameof(IsRectangularFoundation));
        OnPropertyChanged(nameof(IsCircularFoundation));
        OnPropertyChanged(nameof(IsRaftFoundation));
        OnPropertyChanged(nameof(IsPileFoundation));
        OnPropertyChanged(nameof(IsRigidShortPileFoundation));
        OnPropertyChanged(nameof(IsRigidCircularShortPileFoundation));
        OnPropertyChanged(nameof(IsRigidRectangularShortPileFoundation));
        OnPropertyChanged(nameof(IsShallowFoundation));
        OnPropertyChanged(nameof(IsPileLikeFoundation));
        OnPropertyChanged(nameof(IsBaseThicknessRelevant));
        OnPropertyChanged(nameof(IsBasePlanRelevant));
        OnPropertyChanged(nameof(IsCircularPileDiameterRelevant));
        OnPropertyChanged(nameof(IsMultiLegPileFoundation));
        OnPropertyChanged(nameof(IsMultiLegFoundation));
        OnPropertyChanged(nameof(PileLayoutSummary));
        OnPropertyChanged(nameof(PileCountDisplay));
        OnPropertyChanged(nameof(FoundationTypeDisplay));
        OnPropertyChanged(nameof(PedestalDiameterM));
        OnPropertyChanged(nameof(SelectedTowerCatalogStandardSummary));
        OnPropertyChanged(nameof(SelectedTowerCatalogBasicSummary));
        ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
        UpdateGeotechnicalHistorySummary();
        ReuseGeotechnicalHistoryCommand?.RaiseCanExecuteChanged();
        ReanalyzeGeotechnicalHistoryCommand?.RaiseCanExecuteChanged();
    }

    private void SyncRegionSelectionFromProject()
    {
        var province = Provinces.FirstOrDefault(item =>
            item.Name.Equals(Project.Province, StringComparison.Ordinal));
        _selectedProvince = province;
        Cities.Clear();
        Counties.Clear();
        if (province is not null)
        {
            foreach (var cityOption in _regionWindCatalog.GetCities(province.Code))
            {
                Cities.Add(cityOption);
            }
        }

        var city = Cities.FirstOrDefault(item =>
            item.Name.Equals(Project.City, StringComparison.Ordinal));
        _selectedCity = city;
        if (city is not null)
        {
            foreach (var countyOption in _regionWindCatalog.GetCounties(city.Code))
            {
                Counties.Add(countyOption);
            }
        }

        _selectedCounty = Counties.FirstOrDefault(item =>
            item.Name.Equals(Project.County, StringComparison.Ordinal));
        OnPropertyChanged(nameof(SelectedProvince));
        OnPropertyChanged(nameof(SelectedCity));
        OnPropertyChanged(nameof(SelectedCounty));
        RefreshWindPressureFromAddress();
        RefreshLocationSeismicReference();
    }

    private void RefreshTowerCatalogTypesAndRecords()
    {
        TowerCatalogTypes.Clear();
        TowerCatalogTypes.Add("全部塔型");
        var source = SelectedTowerCatalogSource == AllCurrentCatalogsLabel
            ? null
            : SelectedTowerCatalogSource;
        foreach (var towerType in _enterpriseTowerLoadService.GetTowerTypes(source))
        {
            TowerCatalogTypes.Add(towerType);
        }

        if (!TowerCatalogTypes.Contains(SelectedTowerCatalogType))
        {
            _selectedTowerCatalogType = "全部塔型";
            OnPropertyChanged(nameof(SelectedTowerCatalogType));
        }

        RefreshTowerCatalogDimensionsAndRecords();
    }

    private void RefreshTowerCatalogDimensionsAndRecords()
    {
        var source = SelectedTowerCatalogSource == AllCurrentCatalogsLabel
            ? null
            : SelectedTowerCatalogSource;
        var towerType = SelectedTowerCatalogType == "全部塔型"
            ? null
            : SelectedTowerCatalogType;
        var previousHeight = SelectedTowerCatalogHeight.Value;
        var previousWindPressure = SelectedTowerCatalogWindPressure.Value;

        TowerCatalogHeights.Clear();
        TowerCatalogHeights.Add(new CatalogNumericFilterOption("全部塔高", null));
        foreach (var height in _enterpriseTowerLoadService.GetTowerHeights(source, towerType))
        {
            TowerCatalogHeights.Add(new CatalogNumericFilterOption($"{height:0.##} m", height));
        }

        TowerCatalogWindPressures.Clear();
        TowerCatalogWindPressures.Add(new CatalogNumericFilterOption("全部风压", null));
        foreach (var windPressure in _enterpriseTowerLoadService.GetWindPressures(source, towerType))
        {
            TowerCatalogWindPressures.Add(
                new CatalogNumericFilterOption($"{windPressure:0.00} kPa", windPressure));
        }

        _selectedTowerCatalogHeight =
            TowerCatalogHeights.FirstOrDefault(item => item.Value == previousHeight) ??
            TowerCatalogHeights[0];
        _selectedTowerCatalogWindPressure =
            TowerCatalogWindPressures.FirstOrDefault(item => item.Value == previousWindPressure) ??
            TowerCatalogWindPressures[0];
        OnPropertyChanged(nameof(SelectedTowerCatalogHeight));
        OnPropertyChanged(nameof(SelectedTowerCatalogWindPressure));
        RefreshTowerCatalogRecords(clearSelectionIfExcluded: true);
    }

    private void RefreshTowerCatalogRecords(bool clearSelectionIfExcluded = false)
    {
        var source = SelectedTowerCatalogSource == AllCurrentCatalogsLabel
            ? null
            : SelectedTowerCatalogSource;
        var towerType = SelectedTowerCatalogType == "全部塔型"
            ? null
            : SelectedTowerCatalogType;
        var matches = _enterpriseTowerLoadService.Filter(
            source,
            towerType,
            TowerCatalogSearchText,
            SelectedTowerCatalogHeight.Value,
            SelectedTowerCatalogWindPressure.Value);

        FilteredTowerCatalogRecords.Clear();
        foreach (var record in matches)
        {
            FilteredTowerCatalogRecords.Add(record);
        }

        if (clearSelectionIfExcluded &&
            SelectedTowerCatalogRecord is not null &&
            FilteredTowerCatalogRecords.All(item =>
                item.Id != SelectedTowerCatalogRecord.Id))
        {
            SelectedTowerCatalogRecord = null;
        }
        TowerCatalogStatus = SelectedTowerCatalogRecord is null
            ? BuildTowerCatalogEmptyOrCountStatus()
            : TowerCatalogStatus;
        OnPropertyChanged(nameof(TowerCatalogMatchSummary));
        ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
    }

    private void SyncTowerCatalogSelectionFromProject()
    {
        if (!string.IsNullOrEmpty(_towerCatalogSearchText))
        {
            _towerCatalogSearchText = string.Empty;
            OnPropertyChanged(nameof(TowerCatalogSearchText));
        }

        var record = _enterpriseTowerLoadService.FindById(Project.TowerMast.CatalogRecordId);
        if (record is null)
        {
            SelectedTowerCatalogRecord = null;
            return;
        }

        if (!_enterpriseTowerLoadService.IsCurrentRecord(record.Id))
        {
            SelectedTowerCatalogRecord = null;
            TowerCatalogStatus =
                "当前项目保存的是历史来源反力，原数据仍保留用于追溯；重新计算前请从当前企业塔型库重新选择，或切换为手工反力。";
            return;
        }

        _selectedTowerCatalogSource = record.SourceTitle;
        OnPropertyChanged(nameof(SelectedTowerCatalogSource));
        RefreshTowerCatalogTypesAndRecords();
        _selectedTowerCatalogType = record.TowerType;
        OnPropertyChanged(nameof(SelectedTowerCatalogType));
        RefreshTowerCatalogRecords();
        SelectedTowerCatalogRecord = FilteredTowerCatalogRecords.FirstOrDefault(item => item.Id == record.Id);
    }

    private string BuildTowerCatalogEmptyOrCountStatus() =>
        HasCurrentTowerCatalogRecords
            ? $"当前筛选得到 {FilteredTowerCatalogRecords.Count} 条现行记录；可用记录优先排列。"
            : TowerCatalogAvailabilitySummary;

    private void RefreshWindPressureFromAddress()
    {
        if (IsTowerProject)
        {
            WindPressureSummary =
                "通信塔桅基础直接采用企业图集或厂家提供的塔脚反力；风作用已包含在基础端荷载中，不再按城市重复计算风压。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Project.Province) ||
            string.IsNullOrWhiteSpace(Project.City))
        {
            WindPressureSummary = "选择省、市、县区后，软件将匹配 GB 50009-2012 表E.5的50年重现期基本风压。";
            return;
        }

        var lookup = _regionWindCatalog.Lookup(
            Project.Province,
            Project.City,
            Project.County);
        if (!lookup.HasValue)
        {
            Project.MonitoringPole.IsMinimumBasicWindPressureApplied = false;
            Project.MonitoringPole.BasicWindPressureSourceType =
                BasicWindPressureSourceType.Manual;
            Project.MonitoringPole.BasicWindPressureSourceStation = string.Empty;
            Project.MonitoringPole.BasicWindPressureSourceNote = lookup.Explanation;
            WindPressureSummary = lookup.Explanation;
            OnPropertyChanged(nameof(WindPressureSourceBadge));
            return;
        }

        _isApplyingWindLookup = true;
        try
        {
            var sourceWindPressure = lookup.FiftyYearKpa!.Value;
            var adoptedWindPressure = Math.Max(
                MonitoringPoleInput.MinimumBasicWindPressureKpa,
                sourceWindPressure);
            Project.MonitoringPole.SourceBasicWindPressureKpa = sourceWindPressure;
            Project.MonitoringPole.BasicWindPressureKpa = adoptedWindPressure;
            Project.MonitoringPole.IsMinimumBasicWindPressureApplied =
                sourceWindPressure < MonitoringPoleInput.MinimumBasicWindPressureKpa;
            Project.MonitoringPole.BasicWindPressureSourceType =
                lookup.SourceKind == WindPressureSourceKind.DirectNormativeStation
                    ? BasicWindPressureSourceType.DirectNormativeStation
                    : BasicWindPressureSourceType.ParentCityReference;
            Project.MonitoringPole.BasicWindPressureSourceStation =
                lookup.SourceStation;
            var minimumNote = sourceWindPressure < MonitoringPoleInput.MinimumBasicWindPressureKpa
                ? $" 查得值{sourceWindPressure:F2} kPa低于GB 50135-2019第4.2.1条规定的0.35 kPa下限，最终计算按0.35 kPa采用。"
                : string.Empty;
            Project.MonitoringPole.BasicWindPressureSourceNote =
                lookup.Explanation + minimumNote;
            WindPressureSummary = Project.MonitoringPole.BasicWindPressureSourceNote;
            OnPropertyChanged(nameof(BasicWindPressureKpa));
            OnPropertyChanged(nameof(WindPressureSourceBadge));
        }
        finally
        {
            _isApplyingWindLookup = false;
        }
    }

    private void RefreshManualWindStations()
    {
        ManualWindStations.Clear();
        if (string.IsNullOrWhiteSpace(Project.Province))
        {
            return;
        }

        foreach (var station in _regionWindCatalog
                     .GetStations(Project.Province)
                     .OrderBy(item => item.City, StringComparer.Ordinal))
        {
            ManualWindStations.Add(station);
        }
    }

    private void RefreshLocationSeismicReference()
    {
        var result = _locationSeismicReferenceService.ApplyIfAvailable(Project);
        SeismicLocationSummary = result.Message;
        if (result.Applied)
        {
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(SpecialtyReadinessSummary));
        }
    }

    private void ClearLocationForTowerProject()
    {
        Project.Province = string.Empty;
        Project.City = string.Empty;
        Project.County = string.Empty;
        _selectedProvince = null;
        _selectedCity = null;
        _selectedCounty = null;
        Cities.Clear();
        Counties.Clear();
        ManualWindStations.Clear();
        _selectedManualWindStation = null;
        OnPropertyChanged(nameof(SelectedProvince));
        OnPropertyChanged(nameof(SelectedCity));
        OnPropertyChanged(nameof(SelectedCounty));
        OnPropertyChanged(nameof(SelectedManualWindStation));
        RefreshWindPressureFromAddress();
    }

    private void RefreshMonitoringDrawingCandidates()
    {
        var selectedId = SelectedMonitoringDrawingCandidate?.Id;
        MonitoringDrawingCandidates.Clear();
        foreach (var candidate in Project.MonitoringDrawingCandidates ?? [])
        {
            MonitoringDrawingCandidates.Add(candidate);
        }
        SelectedMonitoringDrawingCandidate = selectedId.HasValue
            ? MonitoringDrawingCandidates.FirstOrDefault(candidate => candidate.Id == selectedId.Value) ??
              MonitoringDrawingCandidates.FirstOrDefault()
            : MonitoringDrawingCandidates.FirstOrDefault();
        OnPropertyChanged(nameof(MonitoringDrawingCandidateSummary));
    }

    private void RefreshMonitoringMissingInputs()
    {
        var retainedText = MonitoringMissingInputs.ToDictionary(
            item => item.FieldName,
            item => item.InputText,
            StringComparer.Ordinal);
        MonitoringMissingInputs.Clear();

        var candidate = SelectedMonitoringDrawingCandidate;
        var isActivated = candidate is not null &&
                          (candidate.AppliedAt.HasValue || _manualCompletionCandidateId == candidate.Id);
        if (Project.ProjectType == ProjectType.MonitoringPole && isActivated)
        {
            Project.MonitoringPole.ExplicitDrawingInputFields ??= [];
            foreach (var definition in MonitoringInputDefinitions.Where(definition =>
                         !Project.MonitoringPole.ExplicitDrawingInputFields.Contains(definition.FieldName)))
            {
                var field = candidate!.Fields.FirstOrDefault(item =>
                    item.FieldName == definition.FieldName);
                var status = field?.Value is null
                    ? "AI未识别：请查阅原图或设备资料后填写"
                    : field.IsHighConfidence
                        ? "候选尚未采用：请核对原图后填写"
                        : $"候选{field.ValueDisplay}未通过自动采用：请人工复核后重新填写";
                MonitoringMissingInputs.Add(new MonitoringManualCompletionItem
                {
                    FieldName = definition.FieldName,
                    DisplayName = definition.DisplayName,
                    Unit = definition.Unit,
                    Explanation = definition.Explanation,
                    RecognitionStatus = status,
                    InputText = retainedText.GetValueOrDefault(definition.FieldName, string.Empty)
                });
            }
        }

        OnPropertyChanged(nameof(HasMonitoringMissingInputs));
        OnPropertyChanged(nameof(MonitoringMissingInputSummary));
        ApplyMonitoringMissingInputsCommand?.RaiseCanExecuteChanged();
    }

    private void RaiseCommandStates()
    {
        ReturnToCurrentWorkflowCommand.RaiseCanExecuteChanged();
        ReviseViewedWorkflowStepCommand.RaiseCanExecuteChanged();
        NewProjectCommand.RaiseCanExecuteChanged();
        OpenProjectCommand.RaiseCanExecuteChanged();
        SaveProjectCommand.RaiseCanExecuteChanged();
        SaveProjectAsCommand.RaiseCanExecuteChanged();
        SelectMonitoringProjectCommand.RaiseCanExecuteChanged();
        SelectTowerProjectCommand.RaiseCanExecuteChanged();
        GenerateSchemesCommand.RaiseCanExecuteChanged();
        SelectSchemeCommand.RaiseCanExecuteChanged();
        UseCandidateAsCustomCommand.RaiseCanExecuteChanged();
        EvaluateCustomSchemeCommand.RaiseCanExecuteChanged();
        AdoptCustomSchemeCommand.RaiseCanExecuteChanged();
        ExportPrototypePackageCommand.RaiseCanExecuteChanged();
        ExportPackageAsCommand.RaiseCanExecuteChanged();
        ImportGeotechnicalWordCommand.RaiseCanExecuteChanged();
        ImportGeotechnicalPdfCommand.RaiseCanExecuteChanged();
        ImportGeotechnicalVisionPdfCommand.RaiseCanExecuteChanged();
        RecognizeMonitoringDrawingsCommand.RaiseCanExecuteChanged();
        ApplyMonitoringDrawingCandidateCommand.RaiseCanExecuteChanged();
        ApplyMonitoringMissingInputsCommand.RaiseCanExecuteChanged();
        ReuseGeotechnicalHistoryCommand.RaiseCanExecuteChanged();
        ReanalyzeGeotechnicalHistoryCommand.RaiseCanExecuteChanged();
        DeleteGeotechnicalHistoryCommand.RaiseCanExecuteChanged();
        ApplyTowerCatalogLoadCommand.RaiseCanExecuteChanged();
    }

    private static ProjectModel CreateNewProject()
    {
        return new ProjectModel
        {
            Name = "新建基础设计项目",
            ProjectType = ProjectType.NotSelected,
            Province = "甘肃省"
        };
    }

    private static bool HasMeaningfulLoad(FoundationLoad load)
    {
        return Math.Abs(load.VerticalKn) > 1e-9 ||
               Math.Abs(load.ShearXKn) > 1e-9 ||
               Math.Abs(load.ShearYKn) > 1e-9 ||
               Math.Abs(load.MomentXKnM) > 1e-9 ||
               Math.Abs(load.MomentYKnM) > 1e-9;
    }

    private static string MakeSafeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(character =>
            invalidCharacters.Contains(character) ? '_' : character));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private bool IsExplicitDrawingValue(string fieldName)
    {
        var input = Project.MonitoringPole;
        input.ExplicitDrawingInputFields ??= [];
        return !input.RequireExplicitDrawingInputs ||
               input.ExplicitDrawingInputFields.Contains(fieldName);
    }

    private double? GetExplicitDrawingValue(string fieldName, double value) =>
        IsExplicitDrawingValue(fieldName) ? value : null;

    private void SetExplicitDrawingValue(
        string fieldName,
        double? value,
        Action<double> apply,
        [CallerMemberName] string? propertyName = null)
    {
        var input = Project.MonitoringPole;
        input.ExplicitDrawingInputFields ??= [];
        if (value.HasValue)
        {
            apply(value.Value);
            input.ExplicitDrawingInputFields.Add(fieldName);
        }
        else
        {
            input.ExplicitDrawingInputFields.Remove(fieldName);
        }
        Project.ModifiedAt = DateTimeOffset.Now;
        OnPropertyChanged(propertyName);
        RefreshMonitoringMissingInputs();
    }

    private void SetExplicitDrawingValue(
        string fieldName,
        int? value,
        Action<int> apply,
        [CallerMemberName] string? propertyName = null)
    {
        var input = Project.MonitoringPole;
        input.ExplicitDrawingInputFields ??= [];
        if (value.HasValue)
        {
            apply(value.Value);
            input.ExplicitDrawingInputFields.Add(fieldName);
        }
        else
        {
            input.ExplicitDrawingInputFields.Remove(fieldName);
        }
        Project.ModifiedAt = DateTimeOffset.Now;
        OnPropertyChanged(propertyName);
        RefreshMonitoringMissingInputs();
    }

    private void NotifyMonitoringDrawingInputsChanged()
    {
        OnPropertyChanged(nameof(PoleHeightInput));
        OnPropertyChanged(nameof(PoleBottomDiameterMm));
        OnPropertyChanged(nameof(PoleTopDiameterMm));
        OnPropertyChanged(nameof(PoleWallThicknessMm));
        OnPropertyChanged(nameof(ArmMountingHeightInput));
        OnPropertyChanged(nameof(ArmLengthInput));
        OnPropertyChanged(nameof(ArmNearDiameterMm));
        OnPropertyChanged(nameof(ArmFarDiameterMm));
        OnPropertyChanged(nameof(ArmWallThicknessMm));
        OnPropertyChanged(nameof(ArmCountInput));
        OnPropertyChanged(nameof(AttachmentProjectedAreaInput));
        OnPropertyChanged(nameof(AttachmentWeightInput));
        OnPropertyChanged(nameof(ArmSegmentSummary));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
