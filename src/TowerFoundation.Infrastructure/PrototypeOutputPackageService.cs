using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TowerFoundation.Application;
using TowerFoundation.Domain;

namespace TowerFoundation.Infrastructure;

public sealed class PrototypeOutputPackageService : IProjectOutputService
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<OutputPackageResult> ExportPrototypePackageAsync(
        ProjectModel project,
        string parentDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);

        var scheme = project.SelectedSchemeId is { } selectedId
            ? project.Schemes.SingleOrDefault(item => item.Id == selectedId)
            : null;
        if (scheme is null)
        {
            throw new InvalidOperationException("请先确认一个基础方案。");
        }

        var safeProjectName = MakeSafeFileName(project.Name);
        var packageDirectory = CreateUniqueDirectory(
            Path.GetFullPath(parentDirectory),
            $"{safeProjectName}_设计成果_{DateTime.Now:yyyyMMdd_HHmmss}");

        var calculationBookPath = Path.Combine(packageDirectory, "01_基础计算书.docx");
        var materialPath = Path.Combine(packageDirectory, "02_配筋及材料表.csv");
        var quantityPath = Path.Combine(packageDirectory, "03_工程量.csv");
        var dxfPath = Path.Combine(packageDirectory, "04_基础配筋图.dxf");
        var cuttingSchedulePath = Path.Combine(packageDirectory, "05_钢筋下料表.csv");
        var dwgScriptPath = Path.Combine(packageDirectory, "06_CAD转DWG.scr");
        var simplexFontPath = Path.Combine(packageDirectory, "simplex.shx");
        var gbcbigFontPath = Path.Combine(packageDirectory, "gbcbig.shx");
        var manifestPath = Path.Combine(packageDirectory, "manifest.json");

        WriteCalculationBook(calculationBookPath, project, scheme);
        await File.WriteAllTextAsync(
            materialPath,
            BuildMaterialCsv(scheme),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        await File.WriteAllTextAsync(
            quantityPath,
            BuildQuantityCsv(scheme),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        await File.WriteAllTextAsync(
            dxfPath,
            BuildDxf(project, scheme),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        await File.WriteAllTextAsync(
            cuttingSchedulePath,
            BuildRebarCuttingScheduleCsv(scheme),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        var simplexFontWritten = await TryWriteEmbeddedResourceAsync(
            "TowerFoundation.Infrastructure.CadFonts.simplex.shx",
            simplexFontPath,
            cancellationToken);
        var gbcbigFontWritten = await TryWriteEmbeddedResourceAsync(
            "TowerFoundation.Infrastructure.CadFonts.gbcbig.shx",
            gbcbigFontPath,
            cancellationToken);
        var generatedFiles = new List<string>
        {
            calculationBookPath,
            materialPath,
            quantityPath,
            dxfPath,
            cuttingSchedulePath
        };
        if (simplexFontWritten)
        {
            generatedFiles.Add(simplexFontPath);
        }

        if (gbcbigFontWritten)
        {
            generatedFiles.Add(gbcbigFontPath);
        }
        if (project.FoundationSettings.Drawing.GenerateDwgConversionScript)
        {
            await File.WriteAllTextAsync(
                dwgScriptPath,
                BuildDwgConversionScript(dxfPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            generatedFiles.Add(dwgScriptPath);
        }

        var manifest = new
        {
            schemaVersion = 2,
            projectId = project.Id,
            projectName = project.Name,
            projectType = project.ProjectType,
            rulePackVersion = project.RulePackVersion,
            selectedSchemeId = scheme.Id,
            generatedAt = DateTimeOffset.Now,
            status = BuildPackageStatus(scheme),
            disclaimer = BuildPackageDisclaimer(scheme),
            drawing = new
            {
                project.FoundationSettings.Drawing.CompanyName,
                project.FoundationSettings.Drawing.DrawingTitle,
                project.FoundationSettings.Drawing.DrawingNumber,
                project.FoundationSettings.Drawing.Designer,
                project.FoundationSettings.Drawing.Checker,
                project.FoundationSettings.Drawing.Approver,
                project.FoundationSettings.Drawing.DrawingScale,
                project.FoundationSettings.Drawing.PaperSize
            },
            combinations = project.FoundationLoad.CombinationTrace.Select(item => new
            {
                item.Kind,
                item.GoverningCase,
                item.Expression,
                item.SourceDocument,
                item.IsConfirmed
            }),
            files = generatedFiles.Select(Path.GetFileName).ToArray()
        };
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        generatedFiles.Add(manifestPath);

        project.AuditTrail.Add(new AuditRecord
        {
            Action = "导出设计成果包",
            Details = packageDirectory
        });
        project.ModifiedAt = DateTimeOffset.Now;

        return new OutputPackageResult(
            packageDirectory,
            generatedFiles);
    }

    private static async Task<bool> TryWriteEmbeddedResourceAsync(
        string resourceName,
        string path,
        CancellationToken cancellationToken)
    {
        await using var source = typeof(PrototypeOutputPackageService).Assembly
            .GetManifestResourceStream(resourceName);
        if (source is null)
        {
            return false;
        }

        await using var destination = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
        return true;
    }

    private static void WriteCalculationBook(
        string path,
        ProjectModel project,
        FoundationScheme scheme)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteZipEntry(archive, "[Content_Types].xml", ContentTypesXml);
        WriteZipEntry(archive, "_rels/.rels", PackageRelationshipsXml);
        WriteZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            DocumentRelationshipsXml);
        WriteZipEntry(archive, "word/styles.xml", WordStylesXml);
        WriteZipEntry(archive, "word/header1.xml", WordHeaderXml);
        WriteZipEntry(archive, "word/footer1.xml", WordFooterXml);
        WriteZipEntry(
            archive,
            "word/document.xml",
            BuildCalculationDocumentXml(project, scheme));
    }

    private static string BuildCalculationDocumentXml(
        ProjectModel project,
        FoundationScheme scheme)
    {
        var body = new StringBuilder();
        AppendWordParagraph(
            body,
            $"{FormatFoundationType(scheme)}结构安全计算书",
            "Title");
        AppendWordParagraph(body, project.Name, "Subtitle");
        AppendWordParagraph(
            body,
            scheme.IsFormalVerificationComplete
                ? "成果状态：当前规则包正式验算完成。"
                : $"成果状态：{scheme.VerificationConclusion}。本文件为复核稿，不得标记为全部通过。",
            scheme.IsFormalVerificationComplete ? "ResultPass" : "Warning");

        AppendWordHeading(body, "一、设计依据与计算范围");
        foreach (var reference in BuildDesignReferences(scheme.FoundationType))
        {
            AppendWordBullet(body, reference);
        }
        AppendWordParagraph(
            body,
            "地基承载力、基础尺寸及安全系数法稳定验算采用基础端标准组合控制值；基础高度、冲切、受剪、受弯、配筋和材料强度采用承载能力极限状态基本组合。只有来源资料未提供基本组合时，才按标准组合系数推导并在本计算书中明确标识。",
            "Note");
        AppendWordParagraph(
            body,
            BuildScopeStatement(scheme),
            "Warning");

        AppendWordHeading(body, "二、项目、地勘与材料参数");
        AppendWordTable(
            body,
            ["项目", "取值", "项目", "取值"],
            [
                ["项目名称", project.Name, "工程类型", FormatProjectType(project.ProjectType)],
                ["基础形式", FormatFoundationType(scheme), "规则包", project.RulePackVersion],
                ["控制工况", project.FoundationLoad.GoverningCase, "荷载来源", BuildLoadSourceDescription(project)],
                ["场址/风作用", BuildLocationOrWindDescription(project), "地勘来源", FormatParameterSource(project.Geotechnical.SourceType)]
            ],
            [1500, 3180, 1500, 3180]);
        if (project.ProjectType == ProjectType.MonitoringPole)
        {
            AppendWordTable(
                body,
                ["监控杆参数", "取值", "计算说明"],
                BuildMonitoringPoleRows(project),
                [2400, 3300, 3660]);
        }
        AppendWordTable(
            body,
            ["地勘参数", "取值", "说明"],
            BuildGeotechnicalRows(project, scheme.FoundationType),
            [2400, 1800, 5160]);
        AppendWordTable(
            body,
            ["材料与组合参数", "取值", "单位/说明"],
            BuildMaterialAndCombinationRows(project),
            [3000, 1800, 4560]);

        AppendWordHeading(body, "三、基础端荷载与组合");
        AppendWordParagraph(body, "标准组合：用于地基承载力、基础尺寸、单桩承载力及安全系数法稳定验算。", "Note");
        if (project.FoundationLoad.UsesIndividualPileReactions)
        {
            AppendWordTable(
                body,
                ["一个塔脚最大压力 (kN)", "一个塔脚最大上拔力 (kN)", "一个塔脚最大水平力 (kN)", "基础单元数", "布置说明"],
                [[
                    project.FoundationLoad.IndividualPileCompressionKn.ToString("F2"),
                    project.FoundationLoad.IndividualPileUpliftKn.ToString("F2"),
                    project.FoundationLoad.IndividualPileHorizontalKn.ToString("F2"),
                    project.FoundationLoad.FoundationUnitCount.ToString(CultureInfo.InvariantCulture),
                    FormatFoundationLayoutNote(project)
                ]],
                [2000, 2000, 2000, 1000, 2360]);
        }
        else
        {
            AppendWordTable(
                body,
                ["N (kN)", "Vx (kN)", "Vy (kN)", "Mx (kN·m)", "My (kN·m)", "T (kN·m)"],
                [[
                    project.FoundationLoad.VerticalKn.ToString("F2"),
                    project.FoundationLoad.ShearXKn.ToString("F2"),
                    project.FoundationLoad.ShearYKn.ToString("F2"),
                    project.FoundationLoad.MomentXKnM.ToString("F2"),
                    project.FoundationLoad.MomentYKnM.ToString("F2"),
                    project.FoundationLoad.TorsionKnM.ToString("F2")
                ]],
                [1560, 1560, 1560, 1560, 1560, 1560]);
        }
        AppendWordEquation(body, "Nk = N；Vk = √(Vx² + Vy²)；Mkx = Mx + Vy·d；Mky = My + Vx·d");
        AppendWordEquation(
            body,
            $"代入：N={project.FoundationLoad.VerticalKn:F2} kN，Vx={project.FoundationLoad.ShearXKn:F2} kN，" +
            $"Vy={project.FoundationLoad.ShearYKn:F2} kN，Mx={project.FoundationLoad.MomentXKnM:F2} kN·m，" +
            $"My={project.FoundationLoad.MomentYKnM:F2} kN·m");
        var structuralLoad = project.FoundationLoad.ResolveStructuralDesignLoad(
            project.FoundationSettings);
        AppendWordParagraph(
            body,
            "基本组合：用于基础高度、冲切、受剪、受弯、配筋和材料强度。" +
            project.FoundationLoad.DescribeStructuralCombination(project.FoundationSettings),
            project.FoundationLoad.HasExplicitStructuralCombination ? "Note" : "Warning");
        if (structuralLoad.UsesIndividualPileReactions)
        {
            AppendWordTable(
                body,
                ["基本组合一个塔脚压力 (kN)", "基本组合一个塔脚上拔力 (kN)", "基本组合一个塔脚水平力 (kN)", "结构重要性系数"],
                [[
                    structuralLoad.IndividualPileCompressionKn.ToString("F2"),
                    structuralLoad.IndividualPileUpliftKn.ToString("F2"),
                    structuralLoad.IndividualPileHorizontalKn.ToString("F2"),
                    project.FoundationSettings.StructureImportanceFactor.ToString("F2")
                ]],
                [2340, 2340, 2340, 2340]);
        }
        else
        {
            AppendWordTable(
                body,
                ["Nd (kN)", "Vdx (kN)", "Vdy (kN)", "Mdx (kN·m)", "Mdy (kN·m)", "Td (kN·m)"],
                [[
                    structuralLoad.VerticalKn.ToString("F2"),
                    structuralLoad.ShearXKn.ToString("F2"),
                    structuralLoad.ShearYKn.ToString("F2"),
                    structuralLoad.MomentXKnM.ToString("F2"),
                    structuralLoad.MomentYKnM.ToString("F2"),
                    structuralLoad.TorsionKnM.ToString("F2")
                ]],
                [1560, 1560, 1560, 1560, 1560, 1560]);
        }
        AppendWordEquation(
            body,
            project.FoundationLoad.HasExplicitStructuralCombination
                ? "Sd = γ0·Sactive（按当前选中的基本/地震/偶然组合）"
                : "Sd = γ0·γF·Sk（仅用于缺少明确基本组合时的回退）");
        AppendWordEquation(
            body,
            $"代入：γ0={project.FoundationSettings.StructureImportanceFactor:F2}，" +
            $"标准组合推导回退系数γF={project.FoundationSettings.StructuralDesignLoadFactor:F2}，" +
            $"γG={project.FoundationSettings.FoundationPermanentLoadFactor:F2}");
        if (project.FoundationLoad.CombinationTrace.Count > 0)
        {
            AppendWordHeading2(body, "荷载组合生成与采用轨迹");
            AppendWordTable(
                body,
                ["组合类型", "工况", "表达式", "来源", "确认状态"],
                project.FoundationLoad.CombinationTrace.Select(item => new[]
                {
                    item.Kind.ToString(),
                    item.GoverningCase,
                    item.Expression,
                    item.SourceDocument,
                    item.IsConfirmed ? "已确认" : "程序生成/待复核"
                }),
                [1200, 2100, 2300, 2460, 1300]);
        }

        AppendWordHeading(body, "四、基础几何、自重与浮力");
        AppendWordParagraph(body, $"采用方案：{scheme.Name}；{scheme.Description}");
        AppendWordParagraph(body, scheme.GeometrySummary, "Note");
        AppendWordTable(
            body,
            ["几何参数", "取值", "单位/说明"],
            BuildGeometryRows(scheme),
            [3000, 1800, 4560]);
        AppendFoundationWeightProcess(body, project, scheme);

        AppendWordHeading(body, "五、结构安全验算汇总");
        AppendWordTable(
            body,
            ["验算项目", "控制工况", "作用效应", "承载能力/限值", "利用率", "结论"],
            scheme.Checks
                .Where(IncludeInSafetySummary)
                .Select(check => new[]
            {
                FormatCalculationCheckName(check),
                check.GoverningCase,
                $"{check.DemandDisplay} {check.Unit}",
                $"{check.CapacityDisplay} {check.Unit}",
                check.UtilizationDisplay,
                FormatCheckStatus(check.Status)
            }),
            [2100, 1800, 1500, 1700, 960, 1300]);

        AppendWordHeading(body, "六、结构安全详细验算（计算校核明细）");
        var detailIndex = 1;
        if (IsShallowFoundation(scheme.FoundationType))
        {
            AppendShallowUpliftApplicability(body, project, scheme, detailIndex++);
        }
        foreach (var check in scheme.Checks.Where(IncludeInDetailedVerification))
        {
            AppendDetailedVerification(body, project, scheme, check, detailIndex++);
        }

        if (scheme.CalculatedResults.Count > 0)
        {
            AppendWordHeading(body, "六-A、计算过程结果（不单独判定通过）");
            AppendWordTable(
                body,
                ["计算结果", "数值", "单位", "控制工况", "依据/说明"],
                scheme.CalculatedResults.Select(check => new[]
                {
                    check.Name,
                    check.DemandDisplay,
                    check.Unit,
                    check.GoverningCase,
                    $"{check.RuleReference}；{check.Explanation}"
                }),
                [1800, 1000, 800, 1800, 3960]);
        }

        if (scheme.ScopeAndInputItems.Count > 0)
        {
            AppendWordHeading(body, "六-B、自动处理与专业核对清单");
            AppendWordParagraph(
                body,
                "下列项目不作为安全性通过结论。其中已由软件自动采用规范候选值的项目应核对来源，其余项目已转交相应专业或详图复核，不阻断本流程继续完成。",
                "Warning");
            AppendWordTable(
                body,
                ["项目", "状态", "依据", "处理要求"],
                scheme.ScopeAndInputItems.Select(check => new[]
                {
                    check.Name,
                    FormatCheckStatus(check.Status),
                    check.RuleReference,
                    check.Explanation
                }),
                [1800, 1100, 2500, 3960]);
        }

        if (scheme.DeliveryReminders.Count > 0)
        {
            AppendWordHeading(body, "六-C、施工与交付提醒");
            AppendWordParagraph(
                body,
                "下列内容属于施工图深化、施工组织或现场验收事项，不是可通过补一个数值完成的计算参数，也不计入待补参数完成率。",
                "Normal");
            AppendWordTable(
                body,
                ["提醒事项", "依据", "落实要求"],
                scheme.DeliveryReminders.Select(check => new[]
                {
                    check.Name,
                    check.RuleReference,
                    check.Explanation
                }),
                [2000, 2700, 4660]);
        }

        AppendWordHeading(body, "七、配筋计算结果与实配");
        AppendReinforcementProcess(body, project, scheme);
        AppendWordTable(
            body,
            ["构件", "方向", "采用钢筋", "计算As", "实配As", "根数", "总长", "重量", "状态"],
            scheme.ReinforcementDesigns.Select(item => new[]
            {
                item.Component,
                item.Direction,
                item.BarSpecification,
                $"{item.RequiredAreaMm2:F0} mm²",
                $"{item.ProvidedAreaMm2:F0} mm²",
                item.BarCount.ToString(CultureInfo.InvariantCulture),
                $"{item.TotalLengthM:F2} m",
                $"{item.CalculatedWeightKg:F2} kg",
                FormatCheckStatus(item.Status)
            }),
            [1350, 650, 1450, 1200, 1200, 650, 1050, 1010, 800]);
        AppendWordParagraph(body, BuildUncalculatedReinforcementScope(scheme), "Warning");

        AppendWordHeading(body, "八、工程量汇总");
        AppendWordTable(
            body,
            ["项目", "数值", "单位", "范围说明"],
            [
                ["混凝土", $"{scheme.Quantities.ConcreteM3:F3}", "m³", "按当前基础几何计算"],
                ["基坑开挖", $"{scheme.Quantities.ExcavationM3:F3}", "m³", BuildExcavationScope(scheme.FoundationType)],
                ["回填土", $"{scheme.Quantities.BackfillM3:F3}", "m³", BuildBackfillScope(scheme.FoundationType)],
                ["已计算钢筋", $"{scheme.Quantities.EstimatedReinforcementKg:F2}", "kg", "仅包含本计算书配筋表列明构件"]
            ],
            [1500, 1100, 800, 5960]);

        AppendWordHeading(body, "九、结论与限制");
        AppendWordParagraph(
            body,
            !scheme.IsFeasible
                ? "结论：本方案存在结构安全验算不满足项，不得作为推荐方案或施工依据使用。"
                : scheme.IsFormalVerificationComplete
                    ? "结论：当前规则包内的确定性安全验算已经完成且均满足。"
                    : $"结论：{scheme.VerificationConclusion}。已完成的确定性验算未发现不满足项，但待补参数和专项复核项目不构成通过结论；本文件仅为复核稿。",
            !scheme.IsFeasible
                ? "ResultFail"
                : scheme.IsFormalVerificationComplete ? "ResultPass" : "Warning");
        AppendWordParagraph(body, BuildScopeStatement(scheme), "Warning");
        AppendWordParagraph(
            body,
            "本计算书的每一项结论均应同时核对其控制工况、公式适用条件和规范依据。未输入的荷载组合不得按零值外推为安全结论。",
            "Warning");
        AppendWordParagraph(body, $"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
               "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
               "<w:body>" + body +
               "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdHeader\"/>" +
               "<w:footerReference w:type=\"default\" r:id=\"rIdFooter\"/>" +
               "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
               "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" " +
               "w:header=\"708\" w:footer=\"708\"/>" +
               "</w:sectPr></w:body></w:document>";
    }

    private static IReadOnlyList<string> BuildDesignReferences(FoundationType type)
    {
        var references = new List<string>
        {
            "《建筑地基基础设计规范》GB 50007-2011",
            "《混凝土结构设计规范》GB/T 50010-2010（2024年版）",
            "《工程结构通用规范》GB 55001-2021",
            "《建筑与市政地基基础通用规范》GB 55003-2021",
            "《建筑结构可靠性设计统一标准》GB 50068-2018",
            "《高耸结构设计标准》GB 50135-2019"
        };
        if (type is FoundationType.Pile or
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile)
        {
            references.Add("《建筑桩基技术规范》JGJ 94-2008");
        }
        if (type is FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile)
        {
            references.Add("《移动通信工程钢塔桅结构设计规范》YD/T 5131-2019");
            references.Add("《单管塔规程计算刚性桩(yy).xls》经审计公式链（仅作既有算法来源，不替代现行规范）");
        }
        return references;
    }

    private static string BuildLoadSourceDescription(ProjectModel project) =>
        project.ProjectType switch
        {
            ProjectType.MonitoringPole => "已确认的杆体几何、设备与规范风压由本地内核形成基础端荷载",
            ProjectType.CommunicationTower when project.FoundationLoad.UsesIndividualPileReactions =>
                "企业图集/厂家一个塔脚的压力、上拔和水平反力包络",
            ProjectType.CommunicationTower =>
                "企业图集或厂家基础端标准组合反力",
            _ => "用户确认"
        };

    private static IEnumerable<IReadOnlyList<string>> BuildMonitoringPoleRows(
        ProjectModel project)
    {
        var pole = project.MonitoringPole;
        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "立杆截面",
                FormatTubeSectionType(pole.PoleSectionType),
                $"H={pole.PoleHeightM:F3}m；下/上端={pole.PoleBottomDiameterM * 1000:F1}/{pole.PoleTopDiameterM * 1000:F1}mm；t={pole.PoleWallThicknessM * 1000:F1}mm"
            },
            new[]
            {
                "横杆截面",
                FormatTubeSectionType(pole.ArmSectionType),
                $"L={pole.ArmLengthM:F3}m；近/远端={pole.ArmNearDiameterM * 1000:F1}/{pole.ArmFarDiameterM * 1000:F1}mm；数量={pole.ArmCount}"
            },
            new[]
            {
                "横杆分段",
                FormatArmSegments(pole),
                pole.ArmSegments.Count == 0
                    ? "按总参数作单段精确积分"
                    : "各段迎风面积、钢材量、自重、弯矩和扭矩分别积分累计，不采用平均壁厚"
            },
            new[]
            {
                "设备参数",
                $"迎风面积={pole.AttachmentProjectedAreaM2:F3}m²；重量={pole.AttachmentWeightKn:F3}kN",
                "图纸未给时保留用户确认原值，不由AI补造"
            }
        };
        var applied = project.MonitoringDrawingCandidates
            .Where(candidate => candidate.AppliedAt.HasValue)
            .OrderByDescending(candidate => candidate.AppliedAt)
            .FirstOrDefault();
        if (applied is not null)
        {
            rows.Add(new[]
            {
                "图纸候选来源",
                $"{applied.SourceFileName} 第{applied.PageNumber}页",
                $"{applied.VisionModel}；SHA256 {applied.SourceFileSha256[..Math.Min(12, applied.SourceFileSha256.Length)]}…；已人工采用"
            });
        }
        return rows;
    }

    private static string FormatTubeSectionType(TubeSectionType type) => type switch
    {
        TubeSectionType.RegularOctagonDiagonalTube => "正八边形（对角尺寸）",
        _ => "圆形管"
    };

    private static string FormatArmSegments(MonitoringPoleInput pole) =>
        pole.ArmSegments.Count == 0
            ? $"单段；t={pole.ArmWallThicknessM * 1000:F1}mm"
            : string.Join("；", pole.ArmSegments.Select((segment, index) =>
                $"第{index + 1}段{segment.LengthM:F3}m，" +
                $"{segment.NearDimensionM * 1000:F1}→{segment.FarDimensionM * 1000:F1}mm，" +
                $"t={segment.WallThicknessM * 1000:F1}mm"));

    private static string FormatFoundationLayoutNote(ProjectModel project) =>
        project.FoundationSettings.FoundationType == FoundationType.Pile
            ? project.FoundationLoad.TieBeamsRequired
                ? "独立灌注桩以连梁拉接，不设承台"
                : "单桩，无承台"
            : "相互独立的基础单元；连接构造另按总图核对";

    private static string BuildLocationOrWindDescription(ProjectModel project) =>
        project.ProjectType == ProjectType.CommunicationTower
            ? "塔脚反力已经包含风作用，不再按城市重复叠加"
            : $"{project.Province}{project.City}{project.County}；基本风压取值" +
              $"{project.MonitoringPole.BasicWindPressureKpa:F2} kPa";

    private static string FormatParameterSource(ParameterSourceType source) => source switch
    {
        ParameterSourceType.DeepSeek => "本地文档提取+文本AI候选，已由用户确认",
        ParameterSourceType.VisualAi => "视觉模型直接分析PDF候选，已由用户确认",
        ParameterSourceType.PdfText or
        ParameterSourceType.LocalOcr or
        ParameterSourceType.WordDocument => "本地文档提取，已由用户确认",
        ParameterSourceType.EnterpriseCatalog => "企业图集/资料库，已由用户确认",
        ParameterSourceType.BuiltInDatabase => "软件内置数据库，已由用户确认",
        ParameterSourceType.ExcelImport => "Excel导入，已由用户确认",
        _ => "用户手工录入并确认"
    };

    private static IEnumerable<IReadOnlyList<string>> BuildGeotechnicalRows(
        ProjectModel project,
        FoundationType type)
    {
        var geotechnical = project.Geotechnical;
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "土重度 γ", $"{geotechnical.SoilUnitWeightKnPerM3:F2} kN/m³", geotechnical.SoilDescription },
            new[] { "地下水埋深", $"{geotechnical.GroundwaterDepthM:F2} m", "基础底面以下时不扣减浮力" }
        };
        if (IsShallowFoundation(type))
        {
            rows.Insert(0, new[]
            {
                geotechnical.UseBearingCapacityCorrection
                    ? "地基承载力特征值 fak"
                    : "修正后承载力特征值 fa",
                $"{(geotechnical.UseBearingCapacityCorrection ? geotechnical.CharacteristicBearingCapacityKpa : geotechnical.BearingCapacityKpa):F2} kPa",
                geotechnical.UseBearingCapacityCorrection
                    ? $"ηb={geotechnical.BearingCapacityWidthCorrectionFactor:F2}；ηd={geotechnical.BearingCapacityDepthCorrectionFactor:F2}"
                    : "采用地勘确认的修正后值"
            });
            rows.Add(new[]
            {
                "基底摩擦系数 μ",
                geotechnical.BaseFrictionCoefficient.ToString("F3"),
                $"抗滑安全系数要求{project.FoundationSettings.RequiredSlidingSafetyFactor:F2}"
            });
        }
        if (type is FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile)
        {
            rows.Add(new[]
            {
                "土内摩擦角 φ",
                $"{geotechnical.InternalFrictionAngleDegree:F2}°",
                "用于刚性短柱桩土抗力与抗倾覆公式"
            });
            var influenceRows = project.FoundationSettings.RigidShortPile.SoilLayers
                .Where(item => item.ThicknessM > 0)
                .Select((item, index) => new[]
                {
                    $"m值分层{index + 1}：{item.Name}",
                    $"hi={item.ThicknessM:F2} m",
                    $"m={item.HorizontalResistanceCoefficientMnPerM4:F2} MN/m⁴"
                });
            rows.AddRange(influenceRows);
        }
        if (type == FoundationType.Pile)
        {
            var pileRows = project.FoundationSettings.Pile.SoilLayers
                .Where(item => item.ThicknessM > 0)
                .Select((item, index) => new[]
                {
                    $"桩土层{index + 1}：{item.Name}",
                    $"hi={item.ThicknessM:F2} m",
                    $"qsik={item.SideResistanceKpa:F2} kPa；qpk={item.TipResistanceKpa:F2} kPa；λ={item.UpliftCoefficient:F2}"
                });
            rows.AddRange(pileRows);
        }
        return rows;
    }

    private static IEnumerable<IReadOnlyList<string>> BuildMaterialAndCombinationRows(
        ProjectModel project)
    {
        var settings = project.FoundationSettings;
        return new List<IReadOnlyList<string>>
        {
            new[] { "混凝土重度 γc", settings.ConcreteUnitWeightKnPerM3.ToString("F2"), "kN/m³" },
            new[] { "水重度 γw", settings.WaterUnitWeightKnPerM3.ToString("F2"), "kN/m³" },
            new[] { "混凝土抗拉强度 ft", settings.ConcreteTensileStrengthMpa.ToString("F2"), "MPa" },
            new[] { "钢筋屈服强度 fy", settings.ReinforcementYieldStrengthMpa.ToString("F0"), "MPa" },
            new[] { "保护层厚度 c", settings.ConcreteCoverMm.ToString("F0"), "mm" },
            new[] { "结构重要性系数 γ0", settings.StructureImportanceFactor.ToString("F2"), "结构设计组合" },
            new[] { "标准组合推导回退系数 γF", settings.StructuralDesignLoadFactor.ToString("F2"), project.FoundationLoad.HasExplicitBasicCombination ? "本项目有明确基本组合，不参与上部作用换算" : "本项目缺少基本组合，结构验算暂按此系数推导" },
            new[] { "基础永久作用系数 γG", settings.FoundationPermanentLoadFactor.ToString("F2"), "基础及覆土永久作用" }
        };
    }

    private static IEnumerable<IReadOnlyList<string>> BuildGeometryRows(FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        var rows = scheme.FoundationType switch
        {
            FoundationType.Pile => new List<IReadOnlyList<string>>
            {
                new[] { "桩径 d", geometry.PileDiameterM.ToString("F2"), "m" },
                new[] { "桩埋深 l", geometry.PileLengthM.ToString("F2"), "m" },
                new[] { "出地面高度 hp", geometry.PedestalHeightM.ToString("F2"), "m" },
                new[] { "独立桩数量", geometry.PileCount.ToString(CultureInfo.InvariantCulture), "1/3/4根；无承台" },
                new[] { "连梁", geometry.TieBeamCount.ToString(CultureInfo.InvariantCulture), $"{geometry.TieBeamWidthM:F2}×{geometry.TieBeamHeightM:F2} m" }
            },
            FoundationType.RigidShortPile => new List<IReadOnlyList<string>>
            {
                new[] { "独立基础单元数量", geometry.FoundationUnitCount.ToString(CultureInfo.InvariantCulture), "个；以下尺寸为单个基础" },
                new[] { "圆形截面直径 d", geometry.PileDiameterM.ToString("F2"), "m" },
                new[] { "埋深 h", geometry.PileLengthM.ToString("F2"), "m" },
                new[] { "出地面高度 hp", geometry.PedestalHeightM.ToString("F2"), "m" }
            },
            FoundationType.RigidRectangularShortPile => new List<IReadOnlyList<string>>
            {
                new[] { "独立基础单元数量", geometry.FoundationUnitCount.ToString(CultureInfo.InvariantCulture), "个；以下尺寸为单个基础" },
                new[] { "矩形截面长 L", geometry.BaseLengthM.ToString("F2"), "m" },
                new[] { "矩形截面宽 B", geometry.BaseWidthM.ToString("F2"), "m" },
                new[] { "埋深 h", geometry.PileLengthM.ToString("F2"), "m" },
                new[] { "出地面高度 hp", geometry.PedestalHeightM.ToString("F2"), "m" }
            },
            _ => new List<IReadOnlyList<string>>
            {
                new[] { "基础单元数量", geometry.FoundationUnitCount.ToString(CultureInfo.InvariantCulture), geometry.FoundationUnitCount > 1 ? "个；以下尺寸为单个独立基础" : "个" },
                new[] { "底板长度 L", geometry.BaseLengthM.ToString("F2"), "m" },
                new[] { "底板宽度 B", geometry.BaseWidthM.ToString("F2"), "m" },
                new[] { "底板厚度 h", geometry.BaseThicknessM.ToString("F2"), "m" },
                new[] { "基础柱/塔柱截面", $"{geometry.PedestalLengthM:F2}×{geometry.PedestalWidthM:F2}", "m；圆柱分支以直径表示" },
                new[] { "基础柱高度 hp", geometry.PedestalHeightM.ToString("F2"), "m" },
                new[] { "基础埋置深度 d", geometry.EmbedmentDepthM.ToString("F2"), "m" }
            }
        };
        if (geometry.TieBeamCount > 0 && scheme.FoundationType != FoundationType.Pile)
        {
            rows.Add(new[]
            {
                "闭合周边连系梁",
                geometry.TieBeamCount.ToString(CultureInfo.InvariantCulture),
                $"根；轴线长{geometry.PileCenterSpacingM:F2}m；b×h={geometry.TieBeamWidthM:F2}×{geometry.TieBeamHeightM:F2}m"
            });
        }

        return rows;
    }

    private static void AppendFoundationWeightProcess(
        StringBuilder body,
        ProjectModel project,
        FoundationScheme scheme)
    {
        var settings = project.FoundationSettings;
        var geotechnical = project.Geotechnical;
        var geometry = scheme.Geometry;
        AppendWordHeading2(body, "4.1 基础体积与有效自重");
        if (IsShallowFoundation(scheme.FoundationType))
        {
            var pedestalArea = scheme.FoundationType == FoundationType.CircularShortColumn
                ? Math.PI * geometry.PedestalLengthM * geometry.PedestalLengthM / 4
                : geometry.PedestalLengthM * geometry.PedestalWidthM;
            var slabVolume = geometry.BaseLengthM * geometry.BaseWidthM * geometry.BaseThicknessM;
            var pedestalVolume = pedestalArea * geometry.PedestalHeightM;
            var concreteVolume = slabVolume + pedestalVolume;
            var soilCoverArea = Math.Max(0, geometry.BaseLengthM * geometry.BaseWidthM - pedestalArea);
            var soilCoverVolume = soilCoverArea * geometry.PedestalHeightM;
            var submergedPedestalHeight = Math.Clamp(
                geometry.PedestalHeightM - geotechnical.GroundwaterDepthM,
                0,
                geometry.PedestalHeightM);
            var submergedSlabHeight = Math.Clamp(
                geometry.EmbedmentDepthM - Math.Max(geotechnical.GroundwaterDepthM, geometry.PedestalHeightM),
                0,
                geometry.BaseThicknessM);
            var submergedConcreteVolume = pedestalArea * submergedPedestalHeight +
                                          geometry.BaseLengthM * geometry.BaseWidthM * submergedSlabHeight;
            var submergedSoilVolume = soilCoverArea * submergedPedestalHeight;
            var grossConcreteWeight = concreteVolume * settings.ConcreteUnitWeightKnPerM3;
            var grossSoilWeight = soilCoverVolume * geotechnical.SoilUnitWeightKnPerM3;
            var buoyancy = (submergedConcreteVolume + submergedSoilVolume) * settings.WaterUnitWeightKnPerM3;
            AppendWordEquation(body, "Vc = L·B·h + Ap·hp");
            AppendWordEquation(
                body,
                $"代入：Vc={geometry.BaseLengthM:F2}×{geometry.BaseWidthM:F2}×{geometry.BaseThicknessM:F2}+" +
                $"{pedestalArea:F3}×{geometry.PedestalHeightM:F2}={concreteVolume:F3} m³");
            AppendWordEquation(body, "Vs = (L·B - Ap)·hp");
            AppendWordEquation(body, $"代入：Vs={soilCoverVolume:F3} m³");
            AppendWordEquation(body, "Gk,eff = γcVc + γsVs - γw(Vc,sub + Vs,sub)");
            AppendWordEquation(
                body,
                $"代入：Gk,eff={grossConcreteWeight:F2}+{grossSoilWeight:F2}-{buoyancy:F2}=" +
                $"{grossConcreteWeight + grossSoilWeight - buoyancy:F2} kN");
            if (geometry.FoundationUnitCount > 1)
            {
                AppendWordParagraph(
                    body,
                    $"以上为一个塔脚对应的单个基础验算值；共{geometry.FoundationUnitCount}个同型独立基础，材料工程量汇总混凝土{scheme.Quantities.ConcreteM3:F3} m³。",
                    "Note");
            }
        }
        else if (scheme.FoundationType == FoundationType.Pile)
        {
            var pileArea = Math.PI * geometry.PileDiameterM * geometry.PileDiameterM / 4;
            var clearBeamLength = geometry.TieBeamCount > 0
                ? Math.Max(0, geometry.PileCenterSpacingM - geometry.PileDiameterM)
                : 0;
            var pileVolume = geometry.PileCount * pileArea *
                             (geometry.PileLengthM + geometry.PedestalHeightM);
            var beamVolume = geometry.TieBeamCount * clearBeamLength *
                             geometry.TieBeamWidthM * geometry.TieBeamHeightM;
            AppendWordEquation(body, "Ap = πd²/4；Vc = np·Ap(l+hp) + nb·lb·bb·hb");
            AppendWordEquation(
                body,
                $"代入：Ap={pileArea:F3} m²，桩体积={pileVolume:F3} m³，连梁体积={beamVolume:F3} m³，" +
                $"Vc={scheme.Quantities.ConcreteM3:F3} m³");
        }
        else
        {
            var area = scheme.FoundationType == FoundationType.RigidShortPile
                ? Math.PI * geometry.PileDiameterM * geometry.PileDiameterM / 4
                : geometry.BaseLengthM * geometry.BaseWidthM;
            AppendWordEquation(body, "A = 截面面积；Vc = A(h + hp)");
            AppendWordEquation(
                body,
                $"代入：A={area:F3} m²，h={geometry.PileLengthM:F2} m，hp={geometry.PedestalHeightM:F2} m，" +
                $"单个基础Vc={area * (geometry.PileLengthM + geometry.PedestalHeightM):F3} m³；" +
                $"{geometry.FoundationUnitCount}个基础合计{scheme.Quantities.ConcreteM3:F3} m³");
            AppendWordEquation(body, "Gk,eff = A[(h-hsub+hp)γc + hsub(γc-γw)]");
        }
    }

    private static bool IsShallowFoundation(FoundationType type) =>
        type is FoundationType.RectangularShortColumn or
            FoundationType.CircularShortColumn or
            FoundationType.Raft;

    private static bool IncludeInSafetySummary(FoundationCheckResult check) =>
        check.Status is CheckStatus.Pass or CheckStatus.Fail &&
        check.Code != "DIMENSION_LIMITS" &&
        check.Code != "PILE_LAYOUT" &&
        check.Code != "PILE_LAYER_LENGTH" &&
        check.Code != "STRUCTURAL_COMBINATION" &&
        !check.Code.EndsWith("_SCOPE", StringComparison.Ordinal) &&
        !check.Code.EndsWith("REMAINING_SCOPE", StringComparison.Ordinal) &&
        check.Code != "PILE_SINGLE_STRUCTURAL_SCOPE";

    private static bool IncludeInDetailedVerification(FoundationCheckResult check) =>
        IncludeInSafetySummary(check) &&
        check.Code is not "STRUCTURAL_COMBINATION" and not "PILE_SINGLE_STRUCTURAL_SCOPE";

    private static string FormatCalculationCheckName(FoundationCheckResult check) => check.Code switch
    {
        "CONTACT" => "抗倾覆稳定与基底接触",
        "BEARING_AVERAGE" => "地基抗压承载力（平均压力）",
        "BEARING_MAX" => "地基抗压承载力（边缘最大压力）",
        "SLIDING" => "抗滑移/水平抗剪稳定",
        "PILE_COMPRESSION" => "单桩竖向抗压承载力",
        "PILE_UPLIFT" => "单桩竖向抗拔承载力",
        "PILE_HORIZONTAL" => "单桩水平抗剪承载力",
        _ => check.Name
    };

    private static void AppendShallowUpliftApplicability(
        StringBuilder body,
        ProjectModel project,
        FoundationScheme scheme,
        int index)
    {
        var geometry = scheme.Geometry;
        var settings = project.FoundationSettings;
        var geotechnical = project.Geotechnical;
        var pedestalArea = scheme.FoundationType == FoundationType.CircularShortColumn
            ? Math.PI * geometry.PedestalLengthM * geometry.PedestalLengthM / 4
            : geometry.PedestalLengthM * geometry.PedestalWidthM;
        var concreteVolume = geometry.BaseLengthM * geometry.BaseWidthM * geometry.BaseThicknessM +
                             pedestalArea * geometry.PedestalHeightM;
        var soilVolume = Math.Max(0, geometry.BaseLengthM * geometry.BaseWidthM - pedestalArea) *
                         geometry.PedestalHeightM;
        var grossResistance = concreteVolume * settings.ConcreteUnitWeightKnPerM3 +
                              soilVolume * geotechnical.SoilUnitWeightKnPerM3;
        var upliftDemand = Math.Max(0, -project.FoundationLoad.VerticalKn);
        AppendWordHeading2(body, $"6.{index} 抗拔/抗浮验算及荷载适用性");
        AppendWordParagraph(
            body,
            "本项用于识别所选控制工况是否包含向上竖向作用。基础有效自重和覆土重可作为抗拔稳定作用；土体侧阻、锚栓和扩展破坏体只有在具有对应地勘与构造模型时方可计入。",
            "Note");
        AppendWordEquation(body, "Uk = max(0, -Nk)；Rk,gross = γcVc + γsVs");
        AppendWordEquation(
            body,
            $"代入：Nk={project.FoundationLoad.VerticalKn:F2} kN，Uk={upliftDemand:F2} kN，" +
            $"未扣浮力的基础及覆土重Rk,gross={grossResistance:F2} kN");
        AppendWordParagraph(
            body,
            upliftDemand <= 1e-9
                ? "本控制工况为向下压力工况，Uk=0，抗拔不控制。当前荷载记录未包含独立上拔组合，因此本段不外推为其他上拔工况的安全结论。"
                : $"当前上拔作用Uk={upliftDemand:F2} kN；应在计入地下水浮力和项目采用的抗拔安全系数后另行比较。",
            upliftDemand <= 1e-9 ? "ResultPass" : "Warning");
    }

    private static void AppendDetailedVerification(
        StringBuilder body,
        ProjectModel project,
        FoundationScheme scheme,
        FoundationCheckResult check,
        int index)
    {
        AppendWordHeading2(body, $"6.{index} {FormatCalculationCheckName(check)}");
        AppendWordKeyValue(body, "控制工况", check.GoverningCase);
        AppendWordKeyValue(body, "规范依据", check.RuleReference);
        foreach (var formula in BuildFormulaLines(project, scheme, check))
        {
            AppendWordEquation(body, formula);
        }
        AppendWordParagraph(body, $"数值代入与计算：{check.Explanation}");
        if (check.Status is CheckStatus.Warning or CheckStatus.NotEvaluated or
            CheckStatus.Result or CheckStatus.PendingInput or CheckStatus.SpecialReview or
            CheckStatus.Advisory)
        {
            AppendWordParagraph(
                body,
                $"本项状态：{FormatCheckStatus(check.Status)}。本项未形成结构安全通过结论。",
                "Warning");
            return;
        }
        AppendWordEquation(
            body,
            $"验算：{check.Demand:F3} {check.Unit} ≤ {check.Capacity:F3} {check.Unit}");
        AppendWordParagraph(
            body,
            check.Status == CheckStatus.Pass
                ? $"结论：满足。利用率={check.Utilization:P1}。"
                : $"结论：不满足。利用率={check.Utilization:P1}，必须调整基础尺寸、材料或配筋。",
            check.Status == CheckStatus.Pass ? "ResultPass" : "ResultFail");
    }

    private static IReadOnlyList<string> BuildFormulaLines(
        ProjectModel project,
        FoundationScheme scheme,
        FoundationCheckResult check)
    {
        var geometry = scheme.Geometry;
        return check.Code switch
        {
            "CONTACT" =>
            [
                "NΣ = Nk + Gf,eff + Gs,eff",
                "Mx,b = |Mx| + |Vy|·d；My,b = |My| + |Vx|·d",
                "pavg = NΣ/(L·B)",
                "pmax,min = pavg ± 6Mx,b/(L·B²) ± 6My,b/(B·L²)；并按基底接触面积判定抗倾覆稳定",
                $"代入荷载：N={project.FoundationLoad.VerticalKn:F2} kN，Mx={project.FoundationLoad.MomentXKnM:F2} kN·m，" +
                $"My={project.FoundationLoad.MomentYKnM:F2} kN·m，d={geometry.EmbedmentDepthM:F2} m"
            ],
            "BEARING_CAPACITY_CORRECTION" =>
            ["fa = fak + ηb·γ·(b-3) + ηd·γm·(d-0.5)"],
            "BEARING_AVERAGE" =>
            ["pk = NΣ/A ≤ fa"],
            "BEARING_MAX" =>
            ["pk,max ≤ 1.2fa"],
            "SLIDING" =>
            ["Hk = √(Vx²+Vy²)", "Rf = μ(Nk+Gf,eff)", "Hk ≤ Rf/Ks"],
            "GROUNDWATER" =>
            ["Fb = γw(Vc,sub + Vs,sub)；Gk,eff = Gk,gross - Fb"],
            "BENDING_APPLICABILITY" =>
            ["a/h ≤ 2.5；ex ≤ L/6；ey ≤ B/6；pmin ≥ 0"],
            var code when code.StartsWith("PUNCHING_", StringComparison.Ordinal) =>
            ["Fl = pj·Al ≤ 0.7βhp·ft·am·h0"],
            var code when code.StartsWith("SHEAR_", StringComparison.Ordinal) =>
            ["Vs = pj,avg·Aload ≤ 0.7βhs·ft·A0"],
            var code when code.StartsWith("BOTTOM_REINFORCEMENT_", StringComparison.Ordinal) =>
            ["As,calc = M·10⁶/(0.9fyh0)", "As,req = max(As,calc, ρmin·b·h)", "As,prov = n·πd²/4 ≥ As,req"],
            "PILE_COMPRESSION" =>
            ["R = [uΣ(ψsi·qsik·li) + ψp·qpk·Ap]/K", "Nk ≤ R"],
            "PILE_UPLIFT" =>
            ["Rt = uΣ(λi·qsik·li)/K + Gp,eff", "Tk ≤ Rt"],
            "PILE_HORIZONTAL" =>
            ["Hk = √(Vx²+Vy²)；Hk ≤ Rha"],
            "PILE_LONGITUDINAL_REINFORCEMENT" =>
            ["As,min = ρmin·Ap", "As,prov = n·πd²/4 ≥ As,min"],
            "SETTLEMENT" =>
            [
                "p0 = [Nk + (γc-γs)Vc]/A",
                "s = ψs·Σ(p0·Ii·hi/Esi) ≤ [s]",
                "Ii按矩形或圆形均布荷载中心线Boussinesq附加应力系数分层积分"
            ],
            "CRACK_WIDTH" =>
            [
                "σs = Msk·10⁶/(0.87h0As)",
                "ψ = 1.1 - 0.65ftk/(ρteσs)，并限制在0.2～1.0",
                "wmax = αcr·ψ·σs/Es·(1.9c + 0.08deq/ρte) ≤ wlim"
            ],
            "ANCHOR_STEEL_TENSION" =>
            ["Nt,max = max(0,-Nu)/n + 4M/(nD)", "Nt,max ≤ Ase·ft/1000"],
            "ANCHOR_STEEL_SHEAR" =>
            ["Vb = V/n + 2T/(nD)", "Vb ≤ Ase·fv/1000"],
            "ANCHOR_STEEL_INTERACTION" =>
            ["Nt/Nt,Rd + V/V,Rd ≤ 1.0"],
            "ANCHOR_PLATE_CONCRETE_BEARING" =>
            ["Aln = π(Dp²-dh²)/4", "Fl = fc·Aln ≥ Nt,max（不计局部受压提高系数）"],
            "ANCHOR_PLATE_THICKNESS" =>
            ["q = Nt,max/Aln", "m = q·c²/2", "treq = √(6m/fy) ≤ tprov"],
            "ANCHOR_CONCRETE_TENSION" =>
            ["Nc,Rd = min(Nbreakout,Rd, Npullout,Rd)", "Nt,max ≤ Nc,Rd"],
            "ANCHOR_CONCRETE_EDGE" =>
            ["Vb ≤ Vedge,Rd"],
            "ANCHOR_CONCRETE_INTERACTION" =>
            ["Nt/Nc,Rd + V/Vedge,Rd ≤ 1.0"],
            "PEDESTAL_LONGITUDINAL_REINFORCEMENT" =>
            ["As,req = max(ρmin·Ac, 2Mx/(0.9fyh0x), 2My/(0.9fyh0y))", "As,prov = n·πd²/4 ≥ As,req"],
            "PEDESTAL_AXIAL_BENDING_INTERACTION" =>
            ["N/N0 + Mx/Mrx + My/Mry ≤ 1.0（不计轴压有利作用的保守包络）", "N0 = fc·Ac"],
            "PEDESTAL_GROSS_SHEAR" =>
            ["V ≤ 0.25βc·fc·b·h0"],
            "PEDESTAL_STIRRUP_REINFORCEMENT" =>
            ["Asv,prov/s = nleg·πdsv²/(4s) ≥ Asv,req/s"],
            "HIGH_WATER_ANTIFLOTATION" =>
            ["Gk/Nw,k ≥ Kw", "一般情况Kw=1.05；Gk计基础及有效压重，Nw,k按设计最高水位排水体积计算"],
            "PILE_STRUCTURAL_COMBINATION" =>
            ["桩身结构和配筋采用承载能力极限状态基本组合；标准组合回退不形成完整通过结论"],
            "PILE_M_METHOD_CLASSIFICATION" =>
            ["b0按JGJ 94式(5.7.5)取值", "EI = 0.85EcI0", "α = (m·b0/EI)^(1/5)"],
            "PILE_TOP_DISPLACEMENT" =>
            ["EI·y''''(z) + m·b0·z·y(z) = 0", "地面边界输入H0、M0，有限长梁离散求y(0)"],
            "PILE_TOP_ROTATION" =>
            ["θ0 = y'(0)，由同一m法有限长地基梁离散解取得"],
            "PILE_INTERNAL_FORCE_ENVELOPE" =>
            ["M(z) = EI·y''(z)", "V(z) = EI·y'''(z)；沿桩长取|M|、|V|最大值"],
            "PILE_AXIAL_BENDING_INTERACTION" =>
            ["N/N0 + M/Mr ≤ 1.0（保守包络）", "N0 = 0.85(fcAc+fyAs)"],
            "PILE_STRUCTURAL_LONGITUDINAL_REINFORCEMENT" =>
            ["As,req = max(ρminAc, 2Mmax/(0.9fyh0), Tuk/fy + 2Mmax/(0.9fyh0))", "As,prov=nπd²/4 ≥ As,req"],
            "PILE_GROSS_SHEAR" =>
            ["Vmax ≤ 0.25βc·fc·beq·h0"],
            "PILE_STIRRUP_REINFORCEMENT" =>
            ["Asv,req/s = max[0,(Vmax-0.7ft·beq·h0)/(fyv·h0)]", "Asv,prov/s ≥ Asv,req/s"],
            "PILE_CRACK_WIDTH" =>
            ["σs=Msk·10⁶/(0.87h0As,t)", "wmax=αcr·ψ·σs/Es·(1.9c+0.08deq/ρte) ≤ wlim"],
            "SETTLEMENT_PILE_METHOD" =>
            ["sconfirmed ≤ [s]；sconfirmed来自静载试验或经审查的等代实体/Mindlin专项计算"],
            "TIE_BEAM_LONGITUDINAL_REINFORCEMENT" =>
            ["As,req=max(Nt/fy + M/(0.9fyh0), ρminbh)", "As,prov=nπd²/4 ≥ As,req"],
            "TIE_BEAM_GROSS_SHEAR" =>
            ["V ≤ 0.25βc·fc·b·h0"],
            "TIE_BEAM_STIRRUP_REINFORCEMENT" =>
            ["Asv,req/s=max[0,(V-0.7ftbh0)/(fyvh0)]", "Asv,prov/s ≥ Asv,req/s"],
            "RIGID_OVERTURNING" =>
            ["Mkd = √(Mx²+My²) + V·hp", "E = k·b1·h²/2", "Mkd ≤ Mu/2"],
            "RIGID_CLASSIFICATION" =>
            ["b0 = 0.9(d+1)，d>1 m；b0 = 0.9(1.5d+0.5)，d≤1 m", "EI = 0.85EcI0", "α = (m·b0/EI)^(1/5)；αh ≤ 2.5"],
            "RIGID_TOP_DISPLACEMENT" =>
            ["δk = 24(M + 0.75Vh)/(k0h²)"],
            "RIGID_TOP_ROTATION" =>
            ["θk = 12(3M/h + 2V)/(k0h²)"],
            "RIGID_INTERNAL_FORCE" =>
            ["V(y)=γF[V-k0δky²/(2h)+k0θky³/(3h)]", "M(y)=γF[M+Vy-k0δky³/(6h)+k0θky⁴/(12h)]"],
            "RIGID_LONGITUDINAL_REINFORCEMENT" =>
            ["按圆形偏心受压截面求As,calc", "As,req=max(As,calc,ρminA)；As,prov=nπd²/4"],
            "RIGID_GROSS_SHEAR" =>
            ["V ≤ 0.25βc·fc·b·h0"],
            "RIGID_STIRRUP_REINFORCEMENT" =>
            ["Asv/s ≥ [V-1.75ftbh0/(λ+1)-0.07N]/(fyvh0)"],
            var code when code.StartsWith("RIGID_RECT_OVERTURNING_", StringComparison.Ordinal) =>
            ["Mkd,i = Mi + Vi·hp", "沿i方向按受力边长、垂直投影宽度求土抗力Ei和抗倾覆力矩Mui", "Mkd,i ≤ Mui/2"],
            var code when code.StartsWith("RIGID_RECT_CLASSIFICATION_", StringComparison.Ordinal) =>
            ["b0,i = bproj+1，bproj>1 m；b0,i = 1.5bproj+0.5，bproj≤1 m", "EIi = 0.85EcIi", "αi=(m·b0,i/EIi)^(1/5)；αih≤2.5"],
            var code when code.StartsWith("RIGID_RECT_DISPLACEMENT_", StringComparison.Ordinal) =>
            ["δk,i = 24(Mi+0.75Vih)/(k0,ih²)"],
            var code when code.StartsWith("RIGID_RECT_ROTATION_", StringComparison.Ordinal) =>
            ["θk,i = 12(3Mi/h+2Vi)/(k0,ih²)"],
            var code when code.StartsWith("RIGID_RECT_INTERNAL_FORCE_", StringComparison.Ordinal) =>
            ["分别沿X、Y主轴求N(y)、Vi(y)、Mi(y)，再在同一控制截面合成N、Vx、Vy、Mx、My"],
            "RIGID_RECT_BIAXIAL_COMPRESSION" =>
            ["1/Nu ≈ 1/Nux + 1/Nuy - 1/Nu0", "N ≤ Nu"],
            "RIGID_RECT_LONGITUDINAL_REINFORCEMENT" =>
            ["As,req=max(双向偏压所需纵筋,ρmin·L·B)", "As,prov=nπd²/4 ≥ As,req"],
            var code when code.StartsWith("RIGID_RECT_GROSS_SHEAR_", StringComparison.Ordinal) =>
            ["Vi ≤ 0.25βc·fc·bi·h0,i"],
            "RIGID_RECT_STIRRUP_REINFORCEMENT" =>
            ["Asv/s按X、Y方向受剪需求取较大值；Asv,prov/s ≥ Asv,req/s"],
            _ => [$"作用效应S={check.Demand:F3} {check.Unit}；承载能力/限值R={check.Capacity:F3} {check.Unit}"]
        };
    }

    private static void AppendReinforcementProcess(
        StringBuilder body,
        ProjectModel project,
        FoundationScheme scheme)
    {
        if (scheme.ReinforcementDesigns.Count == 0)
        {
            AppendWordParagraph(body, "当前方案未形成可用的结构化配筋结果。", "Warning");
            return;
        }
        foreach (var item in scheme.ReinforcementDesigns)
        {
            AppendWordHeading2(body, $"{item.Component}（{item.Direction}）");
            AppendWordEquation(body, "单根钢筋面积 Ab = πdb²/4；钢筋理论重量 q = db²/162");
            AppendWordEquation(
                body,
                $"代入：db={item.BarDiameterMm:F0} mm，根数={item.BarCount}，" +
                $"As,req={item.RequiredAreaMm2:F0} mm²，As,prov={item.ProvidedAreaMm2:F0} mm²");
            AppendWordEquation(
                body,
                $"长度：单根{item.SingleBarLengthM:F3} m，总长{item.TotalLengthM:F3} m；" +
                $"q={item.UnitWeightKgPerM:F3} kg/m，重量={item.CalculatedWeightKg:F2} kg");
            if (!string.IsNullOrWhiteSpace(item.CuttingLengthExplanation))
            {
                AppendWordEquation(body, "矩形箍筋下料长度：" + item.CuttingLengthExplanation);
            }
            AppendWordParagraph(body, $"采用：{item.BarSpecification}；依据：{item.RuleReference}");
            AppendWordParagraph(
                body,
                item.Status == CheckStatus.Pass
                    ? "配筋结论：实配面积不小于计算所需面积，满足。"
                    : "配筋结论：实配面积小于计算所需面积，不满足，必须增配。",
                item.Status == CheckStatus.Pass ? "ResultPass" : "ResultFail");
        }
    }

    private static string BuildMaterialCsv(FoundationScheme scheme)
    {
        var builder = new StringBuilder();
        builder.AppendLine("构件,方向,钢筋规格,计算面积(mm2),实配面积(mm2),根数,单根长度(m),总长度(m),理论重量(kg/m),计算重量(kg),箍身周长(m),135度弯钩量度增量(m),两端弯后平直段增量(m),下料公式,状态,规范依据");
        foreach (var item in scheme.ReinforcementDesigns)
        {
            builder.AppendLine(string.Join(",",
                Csv(item.Component),
                Csv(item.Direction),
                Csv(item.BarSpecification),
                Invariant(item.RequiredAreaMm2),
                Invariant(item.ProvidedAreaMm2),
                item.BarCount.ToString(CultureInfo.InvariantCulture),
                Invariant(item.SingleBarLengthM),
                Invariant(item.TotalLengthM),
                Invariant(item.UnitWeightKgPerM),
                Invariant(item.CalculatedWeightKg),
                Invariant(item.StirrupBodyPerimeterM),
                Invariant(item.HookBendAllowanceM),
                Invariant(item.HookStraightAllowanceM),
                Csv(item.CuttingLengthExplanation),
                Csv(FormatCheckStatus(item.Status)),
                Csv(item.RuleReference)));
        }

        builder.AppendLine();
        builder.AppendLine("未计量内容,,,,,,,,,,,,,,专项设计," +
                           Csv(BuildUncalculatedReinforcementScope(scheme)));
        return builder.ToString();
    }

    private static string BuildRebarCuttingScheduleCsv(FoundationScheme scheme)
    {
        var builder = new StringBuilder();
        builder.AppendLine("编号,构件,方向,钢筋规格,直径(mm),数量,单根计算长度(m),总长度(m),理论重量(kg/m),合计重量(kg),下料说明,状态");
        for (var index = 0; index < scheme.ReinforcementDesigns.Count; index++)
        {
            var item = scheme.ReinforcementDesigns[index];
            builder.AppendLine(string.Join(",",
                Csv($"B{index + 1:00}"),
                Csv(item.Component),
                Csv(item.Direction),
                Csv(item.BarSpecification),
                Invariant(item.BarDiameterMm),
                item.BarCount.ToString(CultureInfo.InvariantCulture),
                Invariant(item.SingleBarLengthM),
                Invariant(item.TotalLengthM),
                Invariant(item.UnitWeightKgPerM),
                Invariant(item.CalculatedWeightKg),
                Csv(string.IsNullOrWhiteSpace(item.CuttingLengthExplanation)
                    ? "当前为结构计算直线/中心线长度；未单列的弯钩、锚固、搭接、接头错开及施工余量须按最终详图复核后下料"
                    : item.CuttingLengthExplanation + "；已计入矩形箍筋135°弯钩量度差和两端弯后平直段，施工偏差仍按最终详图复核。"),
                Csv(FormatCheckStatus(item.Status))));
        }

        if (scheme.ReinforcementDesigns.Count == 0)
        {
            builder.AppendLine("B00,无已计算钢筋,,,,,,,,,请先完成配筋验算,待补参数");
        }
        return builder.ToString();
    }

    private static string BuildDwgConversionScript(string dxfPath)
    {
        var dwgPath = Path.ChangeExtension(dxfPath, ".dwg");
        return string.Join("\r\n",
            "FILEDIA",
            "0",
            "_.OPEN",
            $"\"{dxfPath}\"",
            "_.SAVEAS",
            "2018",
            $"\"{dwgPath}\"",
            "_.CLOSE",
            "FILEDIA",
            "1",
            string.Empty);
    }

    private static string BuildSummary(ProjectModel project, FoundationScheme scheme)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 基础设计方案摘要");
        builder.AppendLine();
        builder.AppendLine(scheme.IsFormalVerificationComplete
            ? "> **当前规则包正式验算完成。**"
            : $"> **{scheme.VerificationConclusion}：本成果为复核稿，不得标记为全部通过。**");
        builder.AppendLine($"> {BuildScopeStatement(scheme)}");
        builder.AppendLine();
        builder.AppendLine($"- 项目：{project.Name}");
        builder.AppendLine($"- 工程类型：{FormatProjectType(project.ProjectType)}");
        builder.AppendLine($"- 基础形式：{FormatFoundationType(scheme)}");
        builder.AppendLine(project.ProjectType == ProjectType.CommunicationTower
            ? "- 城市风压：不适用（基础设计直接采用已包含风作用的塔脚反力）"
            : $"- 场址：{project.Province}{project.City}{project.County}");
        builder.AppendLine($"- 规则包：{project.RulePackVersion}");
        builder.AppendLine($"- 控制工况：{project.FoundationLoad.GoverningCase}");
        if (project.ProjectType == ProjectType.MonitoringPole)
        {
            builder.AppendLine($"- 立杆截面：{FormatTubeSectionType(project.MonitoringPole.PoleSectionType)}");
            builder.AppendLine($"- 横杆截面：{FormatTubeSectionType(project.MonitoringPole.ArmSectionType)}");
            builder.AppendLine($"- 横杆分段：{FormatArmSegments(project.MonitoringPole)}");
        }
        builder.AppendLine();
        builder.AppendLine("## 基础端荷载");
        builder.AppendLine();
        builder.AppendLine("### 标准组合");
        builder.AppendLine();
        if (project.FoundationLoad.UsesIndividualPileReactions)
        {
            builder.AppendLine("| 一个塔脚最大压力 (kN) | 一个塔脚最大上拔力 (kN) | 一个塔脚最大水平力 (kN) | 基础单元数 | 布置说明 |");
            builder.AppendLine("|---:|---:|---:|---:|---|");
            builder.AppendLine(
                $"| {project.FoundationLoad.IndividualPileCompressionKn:F2} | " +
                $"{project.FoundationLoad.IndividualPileUpliftKn:F2} | " +
                $"{project.FoundationLoad.IndividualPileHorizontalKn:F2} | " +
                $"{project.FoundationLoad.FoundationUnitCount} | " +
                $"{FormatFoundationLayoutNote(project)} |");
        }
        else
        {
            builder.AppendLine("| N (kN) | Vx (kN) | Vy (kN) | Mx (kN·m) | My (kN·m) | T (kN·m) |");
            builder.AppendLine("|---:|---:|---:|---:|---:|---:|");
            builder.AppendLine(
                $"| {project.FoundationLoad.VerticalKn:F2} | " +
                $"{project.FoundationLoad.ShearXKn:F2} | " +
                $"{project.FoundationLoad.ShearYKn:F2} | " +
                $"{project.FoundationLoad.MomentXKnM:F2} | " +
                $"{project.FoundationLoad.MomentYKnM:F2} | " +
                $"{project.FoundationLoad.TorsionKnM:F2} |");
        }
        builder.AppendLine();
        var structuralLoad = project.FoundationLoad.ResolveStructuralDesignLoad(
            project.FoundationSettings);
        builder.AppendLine("### 基本组合及结构设计作用");
        builder.AppendLine();
        builder.AppendLine($"> {project.FoundationLoad.DescribeStructuralCombination(project.FoundationSettings)}");
        builder.AppendLine();
        if (structuralLoad.UsesIndividualPileReactions)
        {
            builder.AppendLine("| 一个塔脚压力 (kN) | 一个塔脚上拔力 (kN) | 一个塔脚水平力 (kN) | 控制工况 |");
            builder.AppendLine("|---:|---:|---:|---|");
            builder.AppendLine(
                $"| {structuralLoad.IndividualPileCompressionKn:F2} | " +
                $"{structuralLoad.IndividualPileUpliftKn:F2} | " +
                $"{structuralLoad.IndividualPileHorizontalKn:F2} | " +
                $"{EscapeMarkdown(structuralLoad.GoverningCase)} |");
        }
        else
        {
            builder.AppendLine("| N (kN) | Vx (kN) | Vy (kN) | Mx (kN·m) | My (kN·m) | T (kN·m) |");
            builder.AppendLine("|---:|---:|---:|---:|---:|---:|");
            builder.AppendLine(
                $"| {structuralLoad.VerticalKn:F2} | " +
                $"{structuralLoad.ShearXKn:F2} | " +
                $"{structuralLoad.ShearYKn:F2} | " +
                $"{structuralLoad.MomentXKnM:F2} | " +
                $"{structuralLoad.MomentYKnM:F2} | " +
                $"{structuralLoad.TorsionKnM:F2} |");
        }
        builder.AppendLine();
        builder.AppendLine("## 方案尺寸");
        builder.AppendLine();
        builder.AppendLine($"- 方案：{scheme.Name}");
        AppendGeometrySummary(builder, scheme);
        builder.AppendLine();
        builder.AppendLine("## 安全性验算");
        builder.AppendLine();
        builder.AppendLine("| 校核项 | 状态 | 需求 | 能力 | 利用率 | 说明 |");
        builder.AppendLine("|---|---|---:|---:|---:|---|");
        foreach (var check in scheme.VerificationChecks)
        {
            builder.AppendLine(
                $"| {check.Name} | {FormatCheckStatus(check.Status)} | " +
                $"{check.DemandDisplay} | {check.CapacityDisplay} | " +
                $"{check.UtilizationDisplay} | {EscapeMarkdown(check.Explanation)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## 计算过程结果");
        foreach (var check in scheme.CalculatedResults)
        {
            builder.AppendLine($"- {check.Name}：{check.DemandDisplay} {check.Unit}；{check.Explanation}");
        }

        builder.AppendLine();
        builder.AppendLine("## 自动处理与专业核对");
        foreach (var check in scheme.ScopeAndInputItems)
        {
            builder.AppendLine($"- [{FormatCheckStatus(check.Status)}] {check.Name}：{check.Explanation}");
        }
        foreach (var check in scheme.DeliveryReminders)
        {
            builder.AppendLine($"- [施工提醒] {check.Name}：{check.Explanation}");
        }

        return builder.ToString();
    }

    private static string BuildQuantityCsv(FoundationScheme scheme)
    {
        var builder = new StringBuilder();
        builder.AppendLine("项目,数值,单位,说明");
        builder.AppendLine($"混凝土,{Invariant(scheme.Quantities.ConcreteM3)},m3,按基础几何体积");
        builder.AppendLine(
            $"基坑开挖,{Invariant(scheme.Quantities.ExcavationM3)},m3,{Csv(BuildExcavationScope(scheme.FoundationType))}");
        builder.AppendLine(
            $"回填土,{Invariant(scheme.Quantities.BackfillM3)},m3,{Csv(BuildBackfillScope(scheme.FoundationType))}");
        builder.AppendLine(
            $"已计算钢筋,{Invariant(scheme.Quantities.EstimatedReinforcementKg)},kg," +
            "仅汇总配筋及材料表中已经计算的钢筋，不采用经验含钢量");
        builder.AppendLine(
            $"未计量钢筋,,,\"{BuildUncalculatedReinforcementScope(scheme)}\"");
        return builder.ToString();
    }

    private static string BuildDxf(ProjectModel project, FoundationScheme scheme)
    {
        var builder = new StringBuilder();
        AddDxfPreamble(builder);

        if (scheme.FoundationType == FoundationType.Pile)
        {
            AddSingleCastInPlacePileDxfEntities(builder, scheme);
            return FinalizeDxf(builder, project, scheme);
        }

        if (scheme.Geometry.TieBeamCount > 0 && scheme.Geometry.FoundationUnitCount > 1)
        {
            AddIndependentFoundationTieBeamDxfEntities(builder, scheme);
            return FinalizeDxf(builder, project, scheme);
        }

        if (scheme.FoundationType == FoundationType.RigidShortPile)
        {
            AddRigidShortPileDxfEntities(builder, scheme);
            return FinalizeDxf(builder, project, scheme);
        }

        if (scheme.FoundationType == FoundationType.RigidRectangularShortPile)
        {
            AddRigidRectangularShortPileDxfEntities(builder, scheme);
            return FinalizeDxf(builder, project, scheme);
        }

        AddShallowFoundationDxfEntities(builder, project, scheme);
        return FinalizeDxf(builder, project, scheme);
    }

    private static void AddShallowFoundationDxfEntities(
        StringBuilder builder,
        ProjectModel project,
        FoundationScheme scheme)
    {
        var tower = project.TowerMast;
        var geometry = scheme.Geometry;
        var displayScale = Math.Clamp(
            4.0 / Math.Max(geometry.BaseLengthM, geometry.BaseWidthM),
            1.0,
            2.0);
        var displayBaseLength = geometry.BaseLengthM * displayScale;
        var displayBaseWidth = geometry.BaseWidthM * displayScale;
        var displayBaseThickness = geometry.BaseThicknessM * displayScale;
        var displayPedestalLength = geometry.PedestalLengthM * displayScale;
        var displayPedestalWidth = geometry.PedestalWidthM * displayScale;
        var displayPedestalHeight = geometry.PedestalHeightM * displayScale;
        var halfLength = displayBaseLength / 2;
        var halfWidth = displayBaseWidth / 2;
        var halfPedestalLength = displayPedestalLength / 2;
        var halfPedestalWidth = displayPedestalWidth / 2;
        var sectionOriginX = halfLength + 0.9;
        var isRaft = scheme.FoundationType == FoundationType.Raft;
        var foundationId = isRaft ? "BPB01" : "DJj01";

        AddRectangle(builder, -halfLength, -halfWidth, halfLength, halfWidth, "FOUNDATION");
        var sharedRaftLegCount = isRaft ? ResolveDrawingTowerLegCount(tower) : 1;
        var sharedRaftLegSpacingM = geometry.PileCenterSpacingM > 0
            ? geometry.PileCenterSpacingM
            : project.FoundationSettings.Pile.PileCenterSpacingM;
        var sharedRaftLegCenters = sharedRaftLegCount > 1
            ? IndependentPileCenters(sharedRaftLegCount, sharedRaftLegSpacingM * displayScale)
            : [(0d, 0d)];
        var sharedRaftCutY = sharedRaftLegCount > 1
            ? sharedRaftLegCenters.Min(item => item.Y)
            : 0;
        var sharedRaftSectionLegs = sharedRaftLegCenters
            .Select((center, index) => (Center: center, Number: index + 1))
            .Where(item => Math.Abs(item.Center.Y - sharedRaftCutY) < 1e-6)
            .OrderBy(item => item.Center.X)
            .ToList();
        var sharedRaftEdgeReserve = Math.Max(0, project.FoundationSettings.DimensionStepM) *
                                    displayScale;
        var sharedRaftLayoutFits = sharedRaftLegCount <= 1 ||
                                   (sharedRaftLegCenters.All(item =>
                                        Math.Abs(item.X) + halfPedestalLength + sharedRaftEdgeReserve <=
                                        halfLength + 1e-6) &&
                                    sharedRaftLegCenters.All(item =>
                                        Math.Abs(item.Y) + halfPedestalWidth + sharedRaftEdgeReserve <=
                                        halfWidth + 1e-6));
        if (isRaft && sharedRaftLegCount > 1)
        {
            for (var index = 0; index < sharedRaftLegCenters.Count; index++)
            {
                var center = sharedRaftLegCenters[index];
                AddRectangle(
                    builder,
                    center.X - halfPedestalLength,
                    center.Y - halfPedestalWidth,
                    center.X + halfPedestalLength,
                    center.Y + halfPedestalWidth,
                    "PEDESTAL");
                AddTowerLegMarker(builder, center, index + 1, halfPedestalLength, halfPedestalWidth);
            }
        }
        else if (scheme.FoundationType == FoundationType.CircularShortColumn)
        {
            AddCircle(builder, 0, 0, displayPedestalLength / 2, "PEDESTAL");
        }
        else
        {
            AddRectangle(
                builder,
                -halfPedestalLength,
                -halfPedestalWidth,
                halfPedestalLength,
                halfPedestalWidth,
                "PEDESTAL");
        }

        AddPlanAxes(
            builder,
            -halfLength,
            -halfWidth,
            halfLength,
            halfWidth,
            sharedRaftLegCount > 1
                ? BuildAxisDefinitions(sharedRaftLegCenters.Select(item => item.X), numeric: true)
                : [(0d, "1")],
            sharedRaftLegCount > 1
                ? BuildAxisDefinitions(sharedRaftLegCenters.Select(item => item.Y), numeric: false)
                : [(0d, "甲")]);
        AddReinforcementEntities(builder, scheme, sectionOriginX, displayScale);
        AddSectionCutMark(
            builder,
            -halfLength - 0.20,
            halfLength + 0.20,
            sharedRaftCutY,
            "1");
        AddHorizontalDimension(
            builder,
            -halfLength,
            halfLength,
            -halfWidth,
            -halfWidth - 0.55,
            Millimetres(geometry.BaseLengthM));
        if (sharedRaftLegCount > 1)
        {
            AddHorizontalDimension(
                builder,
                sharedRaftLegCenters.Min(item => item.X),
                sharedRaftLegCenters.Max(item => item.X),
                sharedRaftCutY,
                -halfWidth - 0.90,
                sharedRaftLegCount == 3
                    ? $"底边塔脚中心距 {Millimetres(sharedRaftLegSpacingM)}"
                    : $"塔脚横向中心距 {Millimetres(sharedRaftLegSpacingM)}");
        }
        else
        {
            AddHorizontalDimension(
                builder,
                -halfPedestalLength,
                halfPedestalLength,
                -halfWidth,
                -halfWidth - 0.90,
                $"{Millimetres((geometry.BaseLengthM - geometry.PedestalLengthM) / 2)}  {Millimetres(geometry.PedestalLengthM)}  {Millimetres((geometry.BaseLengthM - geometry.PedestalLengthM) / 2)}");
        }
        AddVerticalDimension(
            builder,
            -halfLength,
            -halfWidth,
            halfWidth,
            -halfLength - 0.55,
            Millimetres(geometry.BaseWidthM));
        if (sharedRaftLegCount > 1)
        {
            var verticalSpacingM = sharedRaftLegCount == 3
                ? Math.Sqrt(3) * sharedRaftLegSpacingM / 2
                : sharedRaftLegSpacingM;
            AddVerticalDimension(
                builder,
                sharedRaftLegCenters.Min(item => item.X),
                sharedRaftLegCenters.Min(item => item.Y),
                sharedRaftLegCenters.Max(item => item.Y),
                -halfLength - 0.90,
                sharedRaftLegCount == 3
                    ? $"三角形高 {Millimetres(verticalSpacingM)}"
                    : $"塔脚纵向中心距 {Millimetres(verticalSpacingM)}");
        }

        var xBottom = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Direction == "X向" && item.Status == CheckStatus.Pass);
        var yBottom = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Direction == "Y向" && item.Status == CheckStatus.Pass);
        AddTextOnLayer(
            builder,
            -halfLength,
            halfWidth + 3.10,
            0.18,
            isRaft
                ? $"{foundationId}, h={Millimetres(geometry.BaseThicknessM)}"
                : $"{foundationId}, h1={Millimetres(geometry.BaseThicknessM)}",
            "ANNOTATION");
        AddTextOnLayer(
            builder,
            -halfLength,
            halfWidth + 2.80,
            0.15,
            $"B：X {RebarLabel(xBottom)}；Y {RebarLabel(yBottom)}",
            "ANNOTATION");
        AddTextOnLayer(
            builder,
            -halfLength,
            halfWidth + 2.50,
            0.14,
            isRaft
                ? "T：筏板顶筋待结构计算确认，图中不绘假配筋"
                : BuildPedestalFlatNotation(scheme),
            "ANNOTATION");
        AddTextOnLayer(
            builder,
            -halfLength,
            halfWidth + 2.20,
            0.13,
            "基础底标高：待项目标高确认；尺寸单位为毫米，标高单位为米",
            "ANNOTATION");
        AddViewTitle(
            builder,
            0,
            -halfWidth - 1.28,
            isRaft && sharedRaftLegCount > 1
                ? $"{sharedRaftLegCount}塔脚共用筏板基础平面图"
                : isRaft
                    ? "筏板基础平面图"
                    : "独立基础平面图",
            "1:50");

        AddRectangle(
            builder,
            sectionOriginX,
            0,
            sectionOriginX + displayBaseLength,
            displayBaseThickness,
            "FOUNDATION");
        AddConcreteHatch(
            builder,
            sectionOriginX,
            0,
            sectionOriginX + displayBaseLength,
            displayBaseThickness,
            0.34 * displayScale);
        AddRectangle(
            builder,
            sectionOriginX - 0.10,
            -0.10,
            sectionOriginX + displayBaseLength + 0.10,
            0,
            "BLINDING");
        var sectionPedestalStarts = sharedRaftLegCount > 1
            ? sharedRaftSectionLegs
                .Select(item => (
                    StartX: sectionOriginX + displayBaseLength / 2 + item.Center.X - halfPedestalLength,
                    item.Number))
                .ToList()
            :
            [
                (
                    StartX: sectionOriginX + (displayBaseLength - displayPedestalLength) / 2,
                    Number: 1)
            ];
        foreach (var pedestal in sectionPedestalStarts)
        {
            AddRectangle(
                builder,
                pedestal.StartX,
                displayBaseThickness,
                pedestal.StartX + displayPedestalLength,
                displayBaseThickness + displayPedestalHeight,
                "PEDESTAL_SECTION");
            if (sharedRaftLegCount > 1)
            {
                AddCenteredText(
                    builder,
                    pedestal.StartX + halfPedestalLength,
                    displayBaseThickness + displayPedestalHeight + 0.16,
                    0.13,
                    $"塔脚{pedestal.Number}",
                    "ANNOTATION");
            }
        }
        AddGroundLine(
            builder,
            sectionOriginX - 0.45,
            sectionOriginX + displayBaseLength + 0.45,
            displayBaseThickness + Math.Min(0.35, displayPedestalHeight / 3));
        AddReinforcementSectionBars(builder, scheme, sectionOriginX, displayScale);
        if (sharedRaftLegCount <= 1)
        {
            AddPedestalReinforcementEntities(builder, scheme, sectionOriginX, displayScale);
        }
        if (!isRaft || sharedRaftLegCount <= 1)
        {
            AddPedestalPlanReinforcement(builder, scheme, displayScale);
        }
        AddVerticalDimension(
            builder,
            sectionOriginX + displayBaseLength,
            -0.10,
            0,
            sectionOriginX + displayBaseLength + 0.38,
            "100");
        AddVerticalDimension(
            builder,
            sectionOriginX + displayBaseLength,
            0,
            displayBaseThickness,
            sectionOriginX + displayBaseLength + 0.72,
            Millimetres(geometry.BaseThicknessM));
        AddVerticalDimension(
            builder,
            sectionOriginX + displayBaseLength,
            displayBaseThickness,
            displayBaseThickness + displayPedestalHeight,
            sectionOriginX + displayBaseLength + 0.75,
            Millimetres(geometry.PedestalHeightM));
        AddHorizontalDimension(
            builder,
            sectionOriginX,
            sectionOriginX + displayBaseLength,
            -0.10,
            -0.48,
            Millimetres(geometry.BaseLengthM));
        AddLeader(
            builder,
            sectionOriginX + 0.16,
            -0.04,
            sectionOriginX - 0.28,
            -0.80,
            "100厚C15素混凝土垫层");
        AddLeader(
            builder,
            sectionOriginX + displayBaseLength / 2,
            0.06,
            sectionOriginX + displayBaseLength + 0.25,
            -0.82,
            $"B：X {RebarLabel(xBottom)}；Y {RebarLabel(yBottom)}");
        AddElevationMark(
            builder,
            sectionPedestalStarts.Max(item => item.StartX) + displayPedestalLength,
            displayBaseThickness,
            "基础顶标高（相对值）");
        AddViewTitle(
            builder,
            sectionOriginX + displayBaseLength / 2,
            -1.26,
            sharedRaftLegCount > 1 ? "1-1塔脚列筏板剖面图" : "1-1剖面图",
            "1:50");

        AddTextOnLayer(
            builder,
            sectionOriginX,
            displayBaseThickness + displayPedestalHeight +
            (sharedRaftLegCount > 1 ? 1.50 : 0.45),
            0.13,
            isRaft
                ? sharedRaftLegCount > 1
                    ? $"1-1剖面通过塔脚1、2；共用整体筏板承托{sharedRaftLegCount}个塔脚；筏板本身形成整体连接，不另设独立连系梁。"
                    : "筏板采用平板式筏形基础表达；未计算的顶筋和附加筋不在图中虚构。"
                : "独立基础采用平面集中标注；底筋在平面仅作局部揭示，数量按间距及实长计算。",
            "ANNOTATION");
        if (isRaft && sharedRaftLegCount > 1)
        {
            AddTextOnLayer(
                builder,
                sectionOriginX,
                displayBaseThickness + displayPedestalHeight + 1.20,
                0.13,
                sharedRaftLayoutFits
                    ? "塔脚柱配筋、柱下局部冲切附加筋及筏板顶筋须经塔架—筏板整体分析确认，图中不绘假配筋。"
                    : "警告：当前筏板尺寸不能包络实际塔脚根开及短柱，禁止作为施工图，应返回基础方案调整。",
                "ANNOTATION");
        }
    }

    private static string BuildPedestalFlatNotation(FoundationScheme scheme)
    {
        var main = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("短柱纵筋", StringComparison.Ordinal));
        var stirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("短柱箍筋", StringComparison.Ordinal));
        var shape = scheme.FoundationType == FoundationType.CircularShortColumn
            ? $"圆形直径{Millimetres(scheme.Geometry.PedestalLengthM)}"
            : $"{Millimetres(scheme.Geometry.PedestalLengthM)}×{Millimetres(scheme.Geometry.PedestalWidthM)}";
        return $"DZ01 {shape}；纵筋 {RebarLabel(main)}；箍筋 {RebarLabel(stirrup)}";
    }

    private static void AddReinforcementSectionBars(
        StringBuilder builder,
        FoundationScheme scheme,
        double sectionOriginX,
        double displayScale = 1.0)
    {
        var geometry = scheme.Geometry;
        var cover = 0.05 * displayScale;
        var displayLength = geometry.BaseLengthM * displayScale;
        var barY = cover;
        AddLine(
            builder,
            sectionOriginX + cover,
            barY,
            sectionOriginX + displayLength - cover,
            barY,
            "REBAR_BOTTOM_SECTION");
        foreach (var x in SpacedCoordinates(
                     sectionOriginX + cover,
                     sectionOriginX + displayLength - cover,
                     0.22 * displayScale))
        {
            AddCircle(builder, x, barY + 0.012, 0.012, "REBAR_BOTTOM_SECTION");
        }
    }

    private static void AddDxfPreamble(StringBuilder builder)
    {
        AddPair(builder, 0, "SECTION");
        AddPair(builder, 2, "HEADER");
        AddPair(builder, 9, "$ACADVER");
        // The drawing primitives below intentionally use the compact R12 entity
        // grammar. Declaring a newer AC1027 file while emitting R12 entities makes
        // AutoCAD reject the whole drawing as an incomplete DXF. Keep the header,
        // tables and entities on the same, widely supported R12 contract.
        AddPair(builder, 1, "AC1009");
        AddPair(builder, 9, "$INSBASE");
        AddPair(builder, 10, "0");
        AddPair(builder, 20, "0");
        AddPair(builder, 30, "0");
        AddPair(builder, 9, "$LTSCALE");
        AddPair(builder, 40, "1");
        AddPair(builder, 9, "$TEXTSIZE");
        AddPair(builder, 40, "0.15");
        AddPair(builder, 0, "ENDSEC");

        AddPair(builder, 0, "SECTION");
        AddPair(builder, 2, "TABLES");
        AddPair(builder, 0, "TABLE");
        AddPair(builder, 2, "LTYPE");
        AddPair(builder, 70, "3");
        AddDxfLinetype(builder, "CONTINUOUS", "Solid line", []);
        AddDxfLinetype(builder, "CENTER2", "Axis ____ _ ____", [0.60, -0.12, 0.12, -0.12]);
        AddDxfLinetype(builder, "HIDDEN2", "Hidden __ __ __", [0.25, -0.10]);
        AddPair(builder, 0, "ENDTAB");

        var layers = new (string Name, int Color, string Linetype, int Lineweight)[]
        {
            ("0", 7, "CONTINUOUS", 18),
            ("FOUNDATION", 7, "CONTINUOUS", 50),
            ("INDEPENDENT_FOUNDATION", 7, "CONTINUOUS", 50),
            ("INDEPENDENT_FOUNDATION_SECTION", 7, "CONTINUOUS", 50),
            ("PEDESTAL", 7, "CONTINUOUS", 50),
            ("PEDESTAL_SECTION", 7, "CONTINUOUS", 50),
            ("CAST_IN_PLACE_PILE", 7, "CONTINUOUS", 50),
            ("RIGID_SHORT_PILE", 7, "CONTINUOUS", 50),
            ("RIGID_RECT_SHORT_PILE", 7, "CONTINUOUS", 50),
            ("OUTLINE", 7, "CONTINUOUS", 50),
            ("REBAR_BOTTOM_X", 1, "CONTINUOUS", 25),
            ("REBAR_BOTTOM_Y", 1, "CONTINUOUS", 25),
            ("REBAR_TOP_X", 1, "CONTINUOUS", 25),
            ("REBAR_TOP_Y", 1, "CONTINUOUS", 25),
            ("REBAR_REVEAL_BOUNDARY", 1, "CONTINUOUS", 35),
            ("REBAR_BOTTOM_SECTION", 1, "CONTINUOUS", 35),
            ("PEDESTAL_MAIN_REBAR", 1, "CONTINUOUS", 35),
            ("PEDESTAL_STIRRUP", 1, "CONTINUOUS", 25),
            ("PILE_LONGITUDINAL_REBAR", 1, "CONTINUOUS", 35),
            ("PILE_STIRRUP", 1, "CONTINUOUS", 25),
            ("RIGID_LONGITUDINAL_REBAR", 1, "CONTINUOUS", 35),
            ("RIGID_STIRRUP", 1, "CONTINUOUS", 25),
            ("RIGID_RECT_LONGITUDINAL_REBAR", 1, "CONTINUOUS", 35),
            ("RIGID_RECT_STIRRUP", 1, "CONTINUOUS", 25),
            ("TIE_BEAM", 7, "CONTINUOUS", 35),
            ("TIE_BEAM_REBAR", 1, "CONTINUOUS", 25),
            ("TOWER_LEG_MARK", 3, "CONTINUOUS", 25),
            ("CONSTRUCTION_DETAIL", 7, "CONTINUOUS", 25),
            ("BREAK_SYMBOL", 7, "CONTINUOUS", 25),
            ("AXIS", 3, "CENTER2", 13),
            ("DIMENSION", 3, "CONTINUOUS", 13),
            ("SECTION_MARK", 3, "CONTINUOUS", 25),
            ("LEADER", 3, "CONTINUOUS", 13),
            ("BLINDING", 8, "CONTINUOUS", 25),
            ("CONCRETE_HATCH", 8, "CONTINUOUS", 9),
            ("GROUND_LINE", 3, "CONTINUOUS", 25),
            ("HIDDEN", 8, "HIDDEN2", 13),
            ("ANNOTATION", 7, "CONTINUOUS", 18),
            ("VIEW_TITLE", 7, "CONTINUOUS", 25),
            ("DRAWING_FRAME", 7, "CONTINUOUS", 50),
            ("TITLE_BLOCK", 7, "CONTINUOUS", 25),
            ("REBAR_SCHEDULE", 7, "CONTINUOUS", 18),
            ("MATERIAL_SCHEDULE", 7, "CONTINUOUS", 18)
        };
        AddPair(builder, 0, "TABLE");
        AddPair(builder, 2, "LAYER");
        AddPair(builder, 70, layers.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var layer in layers)
        {
            AddDxfLayer(builder, layer.Name, layer.Color, layer.Linetype, layer.Lineweight);
        }
        AddPair(builder, 0, "ENDTAB");

        AddPair(builder, 0, "TABLE");
        AddPair(builder, 2, "STYLE");
        AddPair(builder, 70, "2");
        AddPair(builder, 0, "STYLE");
        AddPair(builder, 2, "STANDARD");
        AddPair(builder, 70, "0");
        AddPair(builder, 40, "0");
        AddPair(builder, 41, "1");
        AddPair(builder, 50, "0");
        AddPair(builder, 71, "0");
        AddPair(builder, 42, "0.15");
        AddPair(builder, 3, "simplex.shx");
        AddPair(builder, 4, "gbcbig.shx");
        AddPair(builder, 0, "STYLE");
        AddPair(builder, 2, "_TCH_DIM");
        AddPair(builder, 70, "0");
        AddPair(builder, 40, "0");
        AddPair(builder, 41, "0.7");
        AddPair(builder, 50, "0");
        AddPair(builder, 71, "0");
        AddPair(builder, 42, "0.15");
        AddPair(builder, 3, "simplex.shx");
        AddPair(builder, 4, "gbcbig.shx");
        AddPair(builder, 0, "ENDTAB");
        AddPair(builder, 0, "ENDSEC");

        AddPair(builder, 0, "SECTION");
        AddPair(builder, 2, "BLOCKS");
        AddDxfSpaceBlock(builder, "$MODEL_SPACE", isPaperSpace: false);
        AddDxfSpaceBlock(builder, "$PAPER_SPACE", isPaperSpace: true);
        AddPair(builder, 0, "ENDSEC");

        AddPair(builder, 0, "SECTION");
        AddPair(builder, 2, "ENTITIES");
    }

    private static void AddDxfSpaceBlock(
        StringBuilder builder,
        string name,
        bool isPaperSpace)
    {
        AddPair(builder, 0, "BLOCK");
        if (isPaperSpace)
        {
            AddPair(builder, 67, "1");
        }
        AddPair(builder, 8, "0");
        AddPair(builder, 2, name);
        AddPair(builder, 70, "0");
        AddPair(builder, 10, "0");
        AddPair(builder, 20, "0");
        AddPair(builder, 30, "0");
        AddPair(builder, 3, name);
        AddPair(builder, 1, string.Empty);
        AddPair(builder, 0, "ENDBLK");
        if (isPaperSpace)
        {
            AddPair(builder, 67, "1");
        }
        AddPair(builder, 8, "0");
    }

    private static string FinalizeDxf(
        StringBuilder builder,
        ProjectModel project,
        FoundationScheme scheme)
    {
        AddDrawingFrameAndTitleBlock(builder, project, scheme);
        AddReinforcementScheduleTable(builder, scheme);
        AddMaterialQuantityScheduleTable(builder, scheme);
        AddVerificationNotes(builder, scheme);
        AddTextOnLayer(
            builder,
            DrawingAnnotationX(scheme),
            UsesCompactShallowSheet(scheme) ? 2.75 : 0.05,
            0.13,
            "制图表达参考22G101-3；几何与配筋取自本项目计算结果，未确认项不得据此施工。",
            "ANNOTATION");
        AddPair(builder, 0, "ENDSEC");
        AddPair(builder, 0, "EOF");
        return builder.ToString();
    }

    private static void AddDrawingFrameAndTitleBlock(
        StringBuilder builder,
        ProjectModel project,
        FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        var supportSpan = scheme.FoundationType is FoundationType.Pile or FoundationType.RigidShortPile
            ? geometry.PileDiameterM
            : Math.Max(geometry.BaseLengthM, geometry.BaseWidthM);
        var planSpan = geometry.TieBeamCount > 0
            ? Math.Max(supportSpan, geometry.PileCenterSpacingM * 2 + supportSpan)
            : supportSpan;
        var left = -Math.Max(5.0, planSpan / 2 + 2.0);
        var right = UsesCompactShallowSheet(scheme)
            ? Math.Max(13.7, DrawingAnnotationX(scheme) + 5.80)
            : Math.Max(19.0, planSpan / 2 + 15.0);
        var bottom = DrawingFrameBottom(scheme);
        const double top = 8.0;

        AddRectangle(builder, left, bottom, right, top, "DRAWING_FRAME");
        AddRectangle(builder, left + 0.15, bottom + 0.15, right - 0.15, top - 0.15, "DRAWING_FRAME");
        AddRectangle(builder, right - 8.8, bottom, right, bottom + 2.2, "TITLE_BLOCK");
        AddLine(builder, right - 8.8, bottom + 1.55, right, bottom + 1.55, "TITLE_BLOCK");
        AddLine(builder, right - 8.8, bottom + 0.75, right, bottom + 0.75, "TITLE_BLOCK");
        AddLine(builder, right - 3.0, bottom, right - 3.0, bottom + 1.55, "TITLE_BLOCK");
        AddLine(builder, right - 1.45, bottom, right - 1.45, bottom + 0.75, "TITLE_BLOCK");
        var drawing = project.FoundationSettings.Drawing;
        AddTextOnLayer(builder, right - 8.55, bottom + 1.82, 0.20,
            DxfText($"{drawing.CompanyName}  {drawing.DrawingTitle}", 78));
        AddTextOnLayer(builder, right - 8.55, bottom + 1.32, 0.15,
            DxfText($"工程名称：{project.Name}", 68), "TITLE_BLOCK");
        AddTextOnLayer(builder, right - 8.55, bottom + 1.02, 0.16, DxfText(FormatDrawingFoundationType(project, scheme)), "TITLE_BLOCK");
        AddTextOnLayer(builder, right - 2.75, bottom + 1.02, 0.14,
            $"图号：{DxfText(drawing.DrawingNumber, 20)}", "TITLE_BLOCK");
        AddTextOnLayer(builder, right - 8.55, bottom + 0.53, 0.13,
            DxfText($"设计：{drawing.Designer}  校核：{drawing.Checker}  审核：{drawing.Approver}", 70), "TITLE_BLOCK");
        AddTextOnLayer(builder, right - 8.55, bottom + 0.27, 0.13,
            $"主要尺寸：{DxfText(scheme.GeometrySummary, 34)}", "TITLE_BLOCK");
        AddTextOnLayer(builder, right - 2.75, bottom + 0.27, 0.13,
            $"{DxfText(drawing.PaperSize, 8)}  比例：{DxfText(drawing.DrawingScale, 12)}", "TITLE_BLOCK");
        AddTextOnLayer(builder, right - 1.35, bottom + 0.27, 0.13,
            scheme.IsFormalVerificationComplete ? "状态：复核完成" : "状态：复核稿", "TITLE_BLOCK");
    }

    private static void AddReinforcementScheduleTable(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var x = DrawingAnnotationX(scheme);
        const double y = 7.35;
        const double rowHeight = 0.43;
        var width = UsesCompactShallowSheet(scheme) ? 5.45 : 9.0;
        var firstColumn = width * 0.40;
        var secondColumn = width * 0.69;
        var thirdColumn = width * 0.84;
        var rows = Math.Min(9, scheme.ReinforcementDesigns.Count);
        var bottom = y - rowHeight * (rows + 1);

        AddRectangle(builder, x, bottom, x + width, y, "REBAR_SCHEDULE");
        AddLine(builder, x + firstColumn, bottom, x + firstColumn, y, "REBAR_SCHEDULE");
        AddLine(builder, x + secondColumn, bottom, x + secondColumn, y, "REBAR_SCHEDULE");
        AddLine(builder, x + thirdColumn, bottom, x + thirdColumn, y, "REBAR_SCHEDULE");
        for (var index = 1; index <= rows; index++)
        {
            AddLine(builder, x, y - rowHeight * index, x + width, y - rowHeight * index, "REBAR_SCHEDULE");
        }

        AddTextOnLayer(builder, x + 0.10, y - 0.31, 0.15, "构件 / 钢筋表", "REBAR_SCHEDULE");
        AddTextOnLayer(builder, x + firstColumn + 0.10, y - 0.31, 0.15, "规格", "REBAR_SCHEDULE");
        AddTextOnLayer(builder, x + secondColumn + 0.10, y - 0.31, 0.15, "重量", "REBAR_SCHEDULE");
        AddTextOnLayer(builder, x + thirdColumn + 0.10, y - 0.31, 0.15, "状态", "REBAR_SCHEDULE");

        for (var index = 0; index < rows; index++)
        {
            var item = scheme.ReinforcementDesigns[index];
            var textY = y - rowHeight * (index + 1) - 0.30;
            AddTextOnLayer(builder, x + 0.10, textY, 0.14, DxfText(item.Component, UsesCompactShallowSheet(scheme) ? 16 : 28), "REBAR_SCHEDULE");
            AddTextOnLayer(builder, x + firstColumn + 0.10, textY, 0.14, DxfText(item.BarSpecification, UsesCompactShallowSheet(scheme) ? 13 : 22), "REBAR_SCHEDULE");
            AddTextOnLayer(builder, x + secondColumn + 0.10, textY, 0.14, item.CalculatedWeightKg.ToString("F1", CultureInfo.InvariantCulture), "REBAR_SCHEDULE");
            AddTextOnLayer(builder, x + thirdColumn + 0.10, textY, 0.14, DxfText(FormatCheckStatus(item.Status), 12), "REBAR_SCHEDULE");
        }

        if (scheme.ReinforcementDesigns.Count > rows)
        {
            AddText(builder, x, bottom - 0.28, 0.14, $"另有{scheme.ReinforcementDesigns.Count - rows}项，详见配筋及材料表");
        }
    }

    private static void AddMaterialQuantityScheduleTable(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var compact = UsesCompactShallowSheet(scheme);
        var x = DrawingAnnotationX(scheme);
        var width = compact ? 5.45 : 9.0;
        const double rowHeight = 0.36;
        var top = DrawingFrameBottom(scheme) + 5.0;
        var rows = new (string Item, string Specification, string Quantity, string Unit, string Status)[]
        {
            (
                "混凝土",
                "强度等级待确认",
                scheme.Quantities.ConcreteM3.ToString("F3", CultureInfo.InvariantCulture),
                "m³",
                "已计量"),
            (
                "已计算钢筋",
                "详见钢筋表",
                scheme.Quantities.EstimatedReinforcementKg.ToString("F1", CultureInfo.InvariantCulture),
                "kg",
                "已计量"),
            (
                "基坑开挖",
                "含工作面估算",
                scheme.Quantities.ExcavationM3.ToString("F3", CultureInfo.InvariantCulture),
                "m³",
                "已计量"),
            (
                "回填土",
                "开挖扣混凝土",
                scheme.Quantities.BackfillM3.ToString("F3", CultureInfo.InvariantCulture),
                "m³",
                "已计量"),
            (
                "其他材料",
                "垫层/锚栓/附加筋",
                "—",
                "—",
                "未计量")
        };
        var bottom = top - rowHeight * (rows.Length + 2);
        var firstColumn = x + width * 0.21;
        var secondColumn = x + width * 0.53;
        var thirdColumn = x + width * 0.69;
        var fourthColumn = x + width * 0.79;
        var headerTop = top - rowHeight;

        AddRectangle(builder, x, bottom, x + width, top, "MATERIAL_SCHEDULE");
        for (var index = 1; index <= rows.Length + 1; index++)
        {
            AddLine(
                builder,
                x,
                top - rowHeight * index,
                x + width,
                top - rowHeight * index,
                "MATERIAL_SCHEDULE");
        }
        foreach (var column in new[] { firstColumn, secondColumn, thirdColumn, fourthColumn })
        {
            AddLine(builder, column, bottom, column, headerTop, "MATERIAL_SCHEDULE");
        }

        AddCenteredText(
            builder,
            x + width / 2,
            top - rowHeight / 2,
            0.16,
            "主要材料及工程量表",
            "MATERIAL_SCHEDULE");
        var headerY = top - rowHeight * 2 + 0.10;
        AddTextOnLayer(builder, x + 0.07, headerY, 0.13, "项目", "MATERIAL_SCHEDULE");
        AddTextOnLayer(builder, firstColumn + 0.07, headerY, 0.13, "规格/范围", "MATERIAL_SCHEDULE");
        AddTextOnLayer(builder, secondColumn + 0.07, headerY, 0.13, "数量", "MATERIAL_SCHEDULE");
        AddTextOnLayer(builder, thirdColumn + 0.05, headerY, 0.13, "单位", "MATERIAL_SCHEDULE");
        AddTextOnLayer(builder, fourthColumn + 0.07, headerY, 0.13, "状态", "MATERIAL_SCHEDULE");

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var textY = top - rowHeight * (index + 3) + 0.10;
            AddTextOnLayer(builder, x + 0.07, textY, 0.12,
                DxfText(row.Item, compact ? 7 : 14), "MATERIAL_SCHEDULE");
            AddTextOnLayer(builder, firstColumn + 0.07, textY, 0.12,
                DxfText(row.Specification, compact ? 11 : 22), "MATERIAL_SCHEDULE");
            AddTextOnLayer(builder, secondColumn + 0.07, textY, 0.12,
                row.Quantity, "MATERIAL_SCHEDULE");
            AddTextOnLayer(builder, thirdColumn + 0.05, textY, 0.12,
                row.Unit, "MATERIAL_SCHEDULE");
            AddTextOnLayer(builder, fourthColumn + 0.07, textY, 0.12,
                row.Status, "MATERIAL_SCHEDULE");
        }
    }

    private static void AddVerificationNotes(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var x = DrawingAnnotationX(scheme);
        var scheduleRows = Math.Min(9, scheme.ReinforcementDesigns.Count);
        var y = UsesCompactShallowSheet(scheme)
            ? 7.35 - 0.43 * (scheduleRows + 1) - 0.35
            : 2.35;
        var pass = scheme.Checks.Count(item => item.Status == CheckStatus.Pass);
        var fail = scheme.Checks.Count(item => item.Status == CheckStatus.Fail);
        var pending = scheme.Checks.Count(item => item.Status is CheckStatus.PendingInput or CheckStatus.NotEvaluated);
        var special = scheme.Checks.Count(item => item.Status is CheckStatus.SpecialReview or CheckStatus.Warning);

        AddTextOnLayer(builder, x, y, 0.18, "设计与校核说明", "ANNOTATION");
        AddTextOnLayer(builder, x, y - 0.38, 0.15,
            $"通过{pass}；不通过{fail}；待补{pending}；专项{special}；最大{scheme.MaximumUtilization:P0}");
        AddTextOnLayer(builder, x, y - 0.76, 0.15, "1. 图示尺寸和已列配筋取自当前方案。", "ANNOTATION");
        AddTextOnLayer(builder, x, y - 1.14, 0.15, "2. 待补及专项复核完成前不得施工。", "ANNOTATION");
        AddTextOnLayer(builder, x, y - 1.52, 0.15, "3. 锚固、搭接、保护层和地基处理须复核。", "ANNOTATION");
    }

    private static bool UsesCompactShallowSheet(FoundationScheme scheme) =>
        scheme.Geometry.TieBeamCount <= 0 &&
        scheme.FoundationType is FoundationType.RectangularShortColumn or
            FoundationType.CircularShortColumn or
            FoundationType.Raft;

    private static double DrawingAnnotationX(FoundationScheme scheme)
    {
        if (!UsesCompactShallowSheet(scheme))
        {
            return 9.0;
        }

        var geometry = scheme.Geometry;
        var displayScale = Math.Clamp(
            4.0 / Math.Max(geometry.BaseLengthM, geometry.BaseWidthM),
            1.0,
            2.0);
        var sectionRight = 0.90 + 1.50 * geometry.BaseLengthM * displayScale;
        return Math.Max(7.75, sectionRight + 0.45);
    }

    private static double DrawingFrameBottom(FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        if (scheme.FoundationType is FoundationType.Pile or
            FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile)
        {
            return -Math.Max(10.0, DrawingPileLengthM(geometry.PileLengthM) + 3.4);
        }

        return geometry.TieBeamCount > 0 ? -10.0 : -5.2;
    }

    private static void AddPedestalReinforcementEntities(
        StringBuilder builder,
        FoundationScheme scheme,
        double sectionOriginX,
        double displayScale = 1.0)
    {
        var main = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("短柱纵筋", StringComparison.Ordinal));
        if (main is null || main.Status != CheckStatus.Pass)
        {
            return;
        }

        var geometry = scheme.Geometry;
        var cover = 0.05 * displayScale;
        var baseLength = geometry.BaseLengthM * displayScale;
        var pedestalLength = geometry.PedestalLengthM * displayScale;
        var baseThickness = geometry.BaseThicknessM * displayScale;
        var pedestalHeight = geometry.PedestalHeightM * displayScale;
        var startX = sectionOriginX + (baseLength - pedestalLength) / 2;
        AddLine(builder, startX + cover, cover,
            startX + cover, baseThickness + pedestalHeight - cover,
            "PEDESTAL_MAIN_REBAR");
        AddLine(builder, startX + pedestalLength - cover, cover,
            startX + pedestalLength - cover,
            baseThickness + pedestalHeight - cover,
            "PEDESTAL_MAIN_REBAR");
        AddLeader(
            builder,
            startX + cover,
            baseThickness + pedestalHeight * 0.72,
            sectionOriginX - 0.25,
            baseThickness + pedestalHeight + 0.32,
            $"短柱纵筋：{DxfText(main.BarSpecification, 36)}");

        var stirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("短柱箍筋", StringComparison.Ordinal));
        if (stirrup is not null && stirrup.BarSpacingMm > 0)
        {
            var spacing = Math.Max(0.08, stirrup.BarSpacingMm / 1000) * displayScale;
            foreach (var elevation in SpacedCoordinates(
                         baseThickness + cover,
                         baseThickness + pedestalHeight - cover,
                         spacing).Take(80))
            {
                AddLine(
                    builder,
                    startX + cover,
                    elevation,
                    startX + pedestalLength - cover,
                    elevation,
                    "PEDESTAL_STIRRUP");
            }
            AddLeader(
                builder,
                startX + pedestalLength - cover,
                baseThickness + pedestalHeight * 0.45,
                startX + pedestalLength + 0.62,
                baseThickness + pedestalHeight * 0.72,
                $"短柱箍筋：{DxfText(stirrup.BarSpecification, 32)}");
        }
    }

    private static string DxfText(string value, int maximumLength = 96)
    {
        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("Φ", "%%c", StringComparison.Ordinal)
            .Replace("φ", "%%c", StringComparison.Ordinal);
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..Math.Max(1, maximumLength - 3)] + "...";
    }

    private static string Millimetres(double valueM) =>
        Math.Round(valueM * 1000).ToString("F0", CultureInfo.InvariantCulture);

    private static string RebarLabel(ReinforcementDesignResult? design) =>
        design is null
            ? "待计算确认"
            : DxfText(design.BarSpecification, 40);

    private static void AddDxfLinetype(
        StringBuilder builder,
        string name,
        string description,
        IReadOnlyList<double> elements)
    {
        AddPair(builder, 0, "LTYPE");
        AddPair(builder, 2, name);
        AddPair(builder, 70, "0");
        AddPair(builder, 3, description);
        AddPair(builder, 72, "65");
        AddPair(builder, 73, elements.Count.ToString(CultureInfo.InvariantCulture));
        AddPair(builder, 40, Invariant(elements.Sum(Math.Abs)));
        foreach (var element in elements)
        {
            AddPair(builder, 49, Invariant(element));
        }
    }

    private static void AddDxfLayer(
        StringBuilder builder,
        string name,
        int color,
        string linetype,
        int lineweight)
    {
        AddPair(builder, 0, "LAYER");
        AddPair(builder, 2, name);
        AddPair(builder, 70, "0");
        AddPair(builder, 62, color.ToString(CultureInfo.InvariantCulture));
        AddPair(builder, 6, linetype);
        // Group code 370 is a post-R12 lineweight field. R12 keeps plot
        // differentiation through layer colour/linetype instead.
        _ = lineweight;
    }

    private static void AddPlanAxes(
        StringBuilder builder,
        double minX,
        double minY,
        double maxX,
        double maxY,
        IReadOnlyList<(double Position, string Label)> xAxes,
        IReadOnlyList<(double Position, string Label)> yAxes)
    {
        const double extension = 0.62;
        foreach (var axis in xAxes)
        {
            AddLine(builder, axis.Position, minY - extension, axis.Position, maxY + extension, "AXIS");
            AddAxisBubble(builder, axis.Position, maxY + extension + 0.18, axis.Label);
        }
        foreach (var axis in yAxes)
        {
            AddLine(builder, minX - extension, axis.Position, maxX + extension, axis.Position, "AXIS");
            AddAxisBubble(builder, minX - extension - 0.18, axis.Position, axis.Label);
        }
    }

    private static void AddAxisBubble(
        StringBuilder builder,
        double x,
        double y,
        string label)
    {
        AddCircle(builder, x, y, 0.18, "AXIS");
        AddCenteredText(builder, x, y - 0.005, 0.14, label, "AXIS");
    }

    private static void AddHorizontalDimension(
        StringBuilder builder,
        double x1,
        double x2,
        double sourceY,
        double dimensionY,
        string label)
    {
        var direction = Math.Sign(dimensionY - sourceY);
        if (direction == 0)
        {
            direction = -1;
        }
        AddLine(builder, x1, sourceY, x1, dimensionY + direction * 0.10, "DIMENSION");
        AddLine(builder, x2, sourceY, x2, dimensionY + direction * 0.10, "DIMENSION");
        AddLine(builder, x1, dimensionY, x2, dimensionY, "DIMENSION");
        AddDimensionTick(builder, x1, dimensionY);
        AddDimensionTick(builder, x2, dimensionY);
        AddCenteredText(builder, (x1 + x2) / 2, dimensionY + 0.12, 0.13, label, "DIMENSION");
    }

    private static void AddVerticalDimension(
        StringBuilder builder,
        double sourceX,
        double y1,
        double y2,
        double dimensionX,
        string label)
    {
        var direction = Math.Sign(dimensionX - sourceX);
        if (direction == 0)
        {
            direction = 1;
        }
        AddLine(builder, sourceX, y1, dimensionX + direction * 0.10, y1, "DIMENSION");
        AddLine(builder, sourceX, y2, dimensionX + direction * 0.10, y2, "DIMENSION");
        AddLine(builder, dimensionX, y1, dimensionX, y2, "DIMENSION");
        AddDimensionTick(builder, dimensionX, y1);
        AddDimensionTick(builder, dimensionX, y2);
        AddCenteredText(builder, dimensionX + 0.15 * direction, (y1 + y2) / 2, 0.13, label, "DIMENSION", 90);
    }

    private static void AddDimensionTick(StringBuilder builder, double x, double y) =>
        AddLine(builder, x - 0.07, y - 0.07, x + 0.07, y + 0.07, "DIMENSION");

    private static void AddSectionCutMark(
        StringBuilder builder,
        double x1,
        double x2,
        double y,
        string label)
    {
        const double segment = 0.45;
        AddLine(builder, x1, y, Math.Min(x1 + segment, x2), y, "SECTION_MARK");
        AddLine(builder, Math.Max(x2 - segment, x1), y, x2, y, "SECTION_MARK");
        AddLine(builder, x1, y - 0.16, x1, y + 0.16, "SECTION_MARK");
        AddLine(builder, x2, y - 0.16, x2, y + 0.16, "SECTION_MARK");
        AddTextOnLayer(builder, x1 - 0.05, y + 0.20, 0.15, label, "SECTION_MARK");
        AddTextOnLayer(builder, x2 - 0.05, y + 0.20, 0.15, label, "SECTION_MARK");
    }

    private static void AddLeader(
        StringBuilder builder,
        double targetX,
        double targetY,
        double textX,
        double textY,
        string label)
    {
        var elbowX = textX < targetX ? textX + 0.35 : textX - 0.35;
        AddLine(builder, targetX, targetY, elbowX, textY - 0.03, "LEADER");
        AddLine(builder, elbowX, textY - 0.03, textX, textY - 0.03, "LEADER");
        var angle = Math.Atan2(textY - targetY, elbowX - targetX);
        const double arrow = 0.10;
        AddLine(
            builder,
            targetX,
            targetY,
            targetX + arrow * Math.Cos(angle + 0.40),
            targetY + arrow * Math.Sin(angle + 0.40),
            "LEADER");
        AddLine(
            builder,
            targetX,
            targetY,
            targetX + arrow * Math.Cos(angle - 0.40),
            targetY + arrow * Math.Sin(angle - 0.40),
            "LEADER");
        AddTextOnLayer(builder, textX, textY, 0.14, label, "ANNOTATION");
    }

    private static void AddElevationMark(
        StringBuilder builder,
        double x,
        double y,
        string label)
    {
        AddLine(builder, x, y, x + 0.16, y + 0.10, "DIMENSION");
        AddLine(builder, x, y, x + 0.16, y - 0.10, "DIMENSION");
        AddLine(builder, x + 0.16, y, x + 0.55, y, "DIMENSION");
        AddTextOnLayer(builder, x + 0.20, y + 0.10, 0.12, label, "DIMENSION");
    }

    private static void AddViewTitle(
        StringBuilder builder,
        double x,
        double y,
        string title,
        string scale)
    {
        AddCenteredText(builder, x, y, 0.20, title, "VIEW_TITLE");
        AddCenteredText(builder, x, y - 0.24, 0.13, scale, "VIEW_TITLE");
        var underlineHalf = Math.Max(0.55, title.Length * 0.095);
        AddLine(builder, x - underlineHalf, y - 0.06, x + underlineHalf, y - 0.06, "VIEW_TITLE");
    }

    private static void AddConcreteHatch(
        StringBuilder builder,
        double x1,
        double y1,
        double x2,
        double y2,
        double spacing)
    {
        var width = Math.Max(0, x2 - x1);
        var height = Math.Max(0, y2 - y1);
        for (var offset = -height; offset <= width; offset += Math.Max(0.08, spacing))
        {
            var startX = x1 + Math.Max(0, offset);
            var startY = y1 + Math.Max(0, -offset);
            var length = Math.Min(x2 - startX, y2 - startY);
            if (length > 0.01)
            {
                AddLine(builder, startX, startY, startX + length, startY + length, "CONCRETE_HATCH");
            }
        }
    }

    private static void AddGroundLine(
        StringBuilder builder,
        double x1,
        double x2,
        double y)
    {
        AddLine(builder, x1, y, x2, y, "GROUND_LINE");
        for (var x = x1 + 0.12; x < x2; x += 0.22)
        {
            AddLine(builder, x, y, x - 0.10, y - 0.10, "GROUND_LINE");
        }
        AddTextOnLayer(builder, x1, y + 0.12, 0.12, "室外地坪（项目标高待确认）", "ANNOTATION");
    }

    private static IReadOnlyList<(double Position, string Label)> BuildAxisDefinitions(
        IEnumerable<double> positions,
        bool numeric)
    {
        return positions
            .Select(value => Math.Round(value, 6))
            .Distinct()
            .OrderBy(value => value)
            .Select((value, index) =>
                (value, numeric
                    ? (index + 1).ToString(CultureInfo.InvariantCulture)
                    : ChineseAxisLabel(index)))
            .ToList();
    }

    private static string ChineseAxisLabel(int index)
    {
        string[] labels = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"];
        return index < labels.Length ? labels[index] : $"横{index + 1}";
    }

    private static void AddPileCageSection(
        StringBuilder builder,
        FoundationScheme scheme,
        double centerX,
        double centerY,
        double outerRadius)
    {
        const double cover = 0.05;
        var steelRadius = Math.Max(0.06, outerRadius - cover);
        AddCircle(builder, centerX, centerY, outerRadius, "CAST_IN_PLACE_PILE");
        AddCircle(builder, centerX, centerY, steelRadius, "PILE_STIRRUP");
        var main = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("桩身纵筋", StringComparison.Ordinal));
        var barsPerPile = main is null
            ? 0
            : Math.Max(1, main.BarCount / Math.Max(1, scheme.Geometry.PileCount));
        for (var index = 0; index < barsPerPile; index++)
        {
            var angle = 2 * Math.PI * index / barsPerPile;
            AddCircle(
                builder,
                centerX + steelRadius * Math.Cos(angle),
                centerY + steelRadius * Math.Sin(angle),
                Math.Max(0.012, (main?.BarDiameterMm ?? 12) / 2000),
                "PILE_LONGITUDINAL_REBAR");
        }
        AddHorizontalDimension(
            builder,
            centerX - outerRadius,
            centerX + outerRadius,
            centerY - outerRadius,
            centerY - outerRadius - 0.34,
            $"桩径={Millimetres(outerRadius * 2)}");
        AddLeader(
            builder,
            centerX + steelRadius * 0.72,
            centerY + steelRadius * 0.72,
            centerX + outerRadius + 0.28,
            centerY + outerRadius + 0.20,
            $"纵筋 {RebarLabel(main)}");
        var hoop = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("桩身箍筋", StringComparison.Ordinal));
        AddLeader(
            builder,
            centerX - steelRadius,
            centerY,
            centerX - outerRadius - 0.75,
            centerY - outerRadius - 0.10,
            $"螺旋箍 {RebarLabel(hoop)}");
        AddViewTitle(builder, centerX, centerY - outerRadius - 0.72, "2-2桩身断面", "1:25");
    }

    private static void AddTieBeamDetail(
        StringBuilder builder,
        FoundationScheme scheme,
        double x,
        double y,
        double length)
    {
        var geometry = scheme.Geometry;
        var width = Math.Max(0.20, geometry.TieBeamWidthM);
        var height = Math.Max(0.30, geometry.TieBeamHeightM);
        var main = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("连梁纵筋", StringComparison.Ordinal) ||
            item.Component.Contains("连系梁纵筋", StringComparison.Ordinal));
        var stirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("连梁箍筋", StringComparison.Ordinal) ||
            item.Component.Contains("连系梁箍筋", StringComparison.Ordinal));

        var leftSupportX1 = x - 0.48;
        var leftSupportX2 = x + 0.30;
        var rightSupportX1 = x + length - 0.30;
        var rightSupportX2 = x + length + 0.48;
        AddRectangle(builder, leftSupportX1, y - 0.24, leftSupportX2, y + height + 0.64, "CONSTRUCTION_DETAIL");
        AddRectangle(builder, rightSupportX1, y - 0.24, rightSupportX2, y + height + 0.64, "CONSTRUCTION_DETAIL");
        AddRectangle(builder, x, y, x + length, y + height, "TIE_BEAM");
        const double cover = 0.05;
        if (main is not null)
        {
            AddLine(builder, x - 0.30, y + cover, x + length + 0.30, y + cover, "TIE_BEAM_REBAR");
            AddLine(builder, x - 0.30, y + height - cover, x + length + 0.30, y + height - cover, "TIE_BEAM_REBAR");
        }
        if (stirrup is not null && stirrup.BarSpacingMm > 0)
        {
            foreach (var station in SpacedCoordinates(
                         x + cover,
                         x + length - cover,
                         Math.Max(0.08, stirrup.BarSpacingMm / 1000)).Take(80))
            {
                AddLine(builder, station, y + cover, station, y + height - cover, "TIE_BEAM_REBAR");
            }
        }

        var sectionX = x + length + 0.85;
        AddRectangle(builder, sectionX, y, sectionX + width, y + height, "TIE_BEAM");
        if (main is not null)
        {
            AddRectangle(
                builder,
                sectionX + cover,
                y + cover,
                sectionX + width - cover,
                y + height - cover,
                "TIE_BEAM_REBAR");
            var totalBars = Math.Max(4, main.BarCount);
            var topBars = Math.Max(2, totalBars / 2);
            var bottomBars = Math.Max(2, totalBars - topBars);
            foreach (var point in SpacedCoordinates(sectionX + cover, sectionX + width - cover,
                         Math.Max(0.02, (width - 2 * cover) / Math.Max(1, topBars - 1)))
                     .Take(topBars)
                     .Select(value => (X: value, Y: y + height - cover))
                     .Concat(
                         SpacedCoordinates(sectionX + cover, sectionX + width - cover,
                                 Math.Max(0.02, (width - 2 * cover) / Math.Max(1, bottomBars - 1)))
                             .Take(bottomBars)
                             .Select(value => (X: value, Y: y + cover))))
            {
                AddCircle(builder, point.X, point.Y, 0.012, "TIE_BEAM_REBAR");
            }
        }
        AddHorizontalDimension(
            builder,
            sectionX,
            sectionX + width,
            y,
            y - 0.30,
            Millimetres(width));
        AddVerticalDimension(
            builder,
            sectionX + width,
            y,
            y + height,
            sectionX + width + 0.30,
            Millimetres(height));
        AddLeader(
            builder,
            x + length * 0.62,
            y + height - cover,
            x + 0.05,
            y + height + 0.34,
            main is null
                ? "JLL01整体分析内力待确认，不生成假配筋"
                : $"JLL01纵筋：{RebarLabel(main)}");
        if (stirrup is not null)
        {
            AddLeader(
                builder,
                x + length * 0.35,
                y + height / 2,
                x + length * 0.48,
                y + height + 0.64,
                $"箍筋：{RebarLabel(stirrup)}，第一道箍筋距支座边50");
        }
        AddTextOnLayer(
            builder,
            x,
            y + height + 0.92,
            0.15,
            $"JLL01(1B)  b={Millimetres(width)}  h={Millimetres(height)}",
            "ANNOTATION");
        AddTextOnLayer(
            builder,
            x,
            y - 0.34,
            0.12,
            main is null
                ? "纵筋、箍筋均待整体分析内力确认"
                : "纵筋伸入两端基础；锚固长度由节点连接验算确认",
            "ANNOTATION");
        AddViewTitle(builder, x + length / 2, y - 0.72, "基础连系梁JLL01纵剖及1-1断面", "1:25");
    }

    private static void AddReinforcementEntities(
        StringBuilder builder,
        FoundationScheme scheme,
        double sectionOriginX,
        double displayScale = 1.0)
    {
        var geometry = scheme.Geometry;
        var coverM = 0.05 * displayScale;
        var baseLength = geometry.BaseLengthM * displayScale;
        var baseWidth = geometry.BaseWidthM * displayScale;
        var xDesign = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Direction == "X向" && item.Status == CheckStatus.Pass);
        var yDesign = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Direction == "Y向" && item.Status == CheckStatus.Pass);
        // 22G101-3的独立基础平面不需要用密集网格遮满整块基础。
        // 仅在左上角揭示一块底筋网，弧形边界表示其余同配筋连续布置。
        var left = -baseLength / 2 + coverM;
        var top = baseWidth / 2 - coverM;
        var revealWidth = Math.Max(0.25, baseLength * 0.52 - coverM);
        var revealHeight = Math.Max(0.25, baseWidth * 0.52 - coverM);
        var bottom = top - revealHeight;
        var right = left + revealWidth;
        var graphicXSpacing = xDesign is null
            ? 0.18 * displayScale
            : Math.Max(0.14 * displayScale, xDesign.BarSpacingMm / 1000 * displayScale);
        var graphicYSpacing = yDesign is null
            ? 0.18 * displayScale
            : Math.Max(0.14 * displayScale, yDesign.BarSpacingMm / 1000 * displayScale);

        if (xDesign is not null)
        {
            foreach (var y in SpacedCoordinates(bottom, top, graphicXSpacing).Take(40))
            {
                var ratio = Math.Clamp((y - bottom) / Math.Max(0.01, revealHeight), 0, 1);
                var endX = left + revealWidth * (0.28 + 0.72 * Math.Sqrt(ratio));
                AddLine(builder, left, y, endX, y, "REBAR_BOTTOM_X");
            }
        }

        if (yDesign is not null)
        {
            foreach (var x in SpacedCoordinates(left, right, graphicYSpacing).Take(40))
            {
                var ratio = Math.Clamp((x - left) / Math.Max(0.01, revealWidth), 0, 1);
                var startY = bottom + revealHeight * Math.Pow(ratio, 1.65);
                AddLine(builder, x, startY, x, top, "REBAR_BOTTOM_Y");
            }
        }

        var boundary = Enumerable.Range(0, 13)
            .Select(index =>
            {
                var ratio = index / 12d;
                return (
                    X: left + revealWidth * (0.28 + 0.72 * Math.Sqrt(ratio)),
                    Y: bottom + revealHeight * ratio);
            })
            .ToList();
        AddOpenPolyline(builder, boundary, "REBAR_REVEAL_BOUNDARY");

        if (xDesign is not null)
        {
            AddRebarCallout(builder, left + revealWidth * 0.18, top - revealHeight * 0.20, left - 0.55, top + 0.10, "1",
                $"X {RebarLabel(xDesign)}");
        }
        if (yDesign is not null)
        {
            AddRebarCallout(builder, left + revealWidth * 0.42, top - revealHeight * 0.46, left - 0.55, top - 0.22, "2",
                $"Y {RebarLabel(yDesign)}");
        }

        AddLine(
            builder,
            sectionOriginX + coverM,
            coverM,
            sectionOriginX + baseLength - coverM,
            coverM,
            "REBAR_BOTTOM_SECTION");
    }

    private static void AddPedestalPlanReinforcement(
        StringBuilder builder,
        FoundationScheme scheme,
        double displayScale)
    {
        var main = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("短柱纵筋", StringComparison.Ordinal) &&
            item.Status == CheckStatus.Pass);
        if (main is null || main.BarCount <= 0)
        {
            return;
        }

        var geometry = scheme.Geometry;
        var cover = 0.05 * displayScale;
        if (scheme.FoundationType == FoundationType.CircularShortColumn)
        {
            var outerRadius = geometry.PedestalLengthM * displayScale / 2;
            var steelRadius = Math.Max(0.05, outerRadius - cover);
            AddCircle(builder, 0, 0, steelRadius, "PEDESTAL_STIRRUP");
            for (var index = 0; index < main.BarCount; index++)
            {
                var angle = 2 * Math.PI * index / main.BarCount;
                AddCircle(
                    builder,
                    steelRadius * Math.Cos(angle),
                    steelRadius * Math.Sin(angle),
                    Math.Max(0.010, main.BarDiameterMm / 2000 * displayScale),
                    "PEDESTAL_MAIN_REBAR");
            }
            return;
        }

        var halfLength = geometry.PedestalLengthM * displayScale / 2;
        var halfWidth = geometry.PedestalWidthM * displayScale / 2;
        var steelHalfLength = Math.Max(0.05, halfLength - cover);
        var steelHalfWidth = Math.Max(0.05, halfWidth - cover);
        AddRectangle(
            builder,
            -steelHalfLength,
            -steelHalfWidth,
            steelHalfLength,
            steelHalfWidth,
            "PEDESTAL_STIRRUP");
        foreach (var point in PerimeterBarPoints(
                     -steelHalfLength,
                     -steelHalfWidth,
                     steelHalfLength,
                     steelHalfWidth,
                     main.BarCount))
        {
            AddCircle(
                builder,
                point.X,
                point.Y,
                Math.Max(0.010, main.BarDiameterMm / 2000 * displayScale),
                "PEDESTAL_MAIN_REBAR");
        }
    }

    private static void AddSingleCastInPlacePileDxfEntities(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        var diameter = geometry.PileDiameterM;
        var radius = diameter / 2;
        var cover = 0.05;
        var pileCenters = IndependentPileCenters(
            geometry.PileCount,
            geometry.PileCenterSpacingM);
        var planMinX = pileCenters.Min(item => item.X) - radius;
        var planMaxX = pileCenters.Max(item => item.X) + radius;
        var planMinY = pileCenters.Min(item => item.Y) - radius;
        var planMaxY = pileCenters.Max(item => item.Y) + radius;
        var drawnPileLength = DrawingPileLengthM(geometry.PileLengthM);
        var pileIsBroken = geometry.PileLengthM > drawnPileLength + 0.05;

        if (geometry.TieBeamCount > 0)
        {
            for (var index = 0; index < pileCenters.Count; index++)
            {
                var next = pileCenters[(index + 1) % pileCenters.Count];
                AddBeamOutline(
                    builder,
                    pileCenters[index],
                    next,
                    geometry.TieBeamWidthM,
                    "TIE_BEAM");
            }
        }

        foreach (var center in pileCenters)
        {
            AddCircle(builder, center.X, center.Y, radius, "CAST_IN_PLACE_PILE");
        }
        if (pileCenters.Count > 1)
        {
            for (var index = 0; index < pileCenters.Count; index++)
            {
                AddTowerLegMarker(builder, pileCenters[index], index + 1, radius, radius);
                AddTieBeamPlanLabel(
                    builder,
                    pileCenters[index],
                    pileCenters[(index + 1) % pileCenters.Count],
                    index + 1,
                    geometry.TieBeamWidthM,
                    geometry.TieBeamHeightM);
            }
            AddTextOnLayer(
                builder,
                planMinX,
                planMaxY + 0.36,
                0.14,
                BuildTowerLegSpacingNote(pileCenters.Count, geometry.PileCenterSpacingM),
                "ANNOTATION");
        }

        AddPlanAxes(
            builder,
            planMinX,
            planMinY,
            planMaxX,
            planMaxY,
            BuildAxisDefinitions(pileCenters.Select(item => item.X), numeric: true),
            BuildAxisDefinitions(pileCenters.Select(item => item.Y), numeric: false));
        AddHorizontalDimension(
            builder,
            planMinX,
            planMaxX,
            planMinY,
            planMinY - 0.55,
            geometry.PileCount == 1
                ? $"桩径={Millimetres(diameter)}"
                : $"桩位总宽 {Millimetres(planMaxX - planMinX)}");
        AddVerticalDimension(
            builder,
            planMinX,
            planMinY,
            planMaxY,
            planMinX - 0.55,
            geometry.PileCount == 1
                ? $"桩径={Millimetres(diameter)}"
                : $"桩位总高 {Millimetres(planMaxY - planMinY)}");
        AddSectionCutMark(builder, planMinX - 0.18, planMaxX + 0.18, pileCenters[0].Y, "1");
        AddLeader(
            builder,
            pileCenters[0].X + radius * 0.70,
            pileCenters[0].Y + radius * 0.70,
            planMinX,
            planMaxY + 0.66,
            $"GZH01  灌注桩  桩径={Millimetres(diameter)}  桩长={Millimetres(geometry.PileLengthM)}");

        var longitudinal = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("桩身纵筋", StringComparison.Ordinal));
        if (longitudinal is not null && longitudinal.BarCount > 0)
        {
            var steelRadius = Math.Max(0.05, radius - cover);
            var barsPerPile = Math.Max(
                1,
                longitudinal.BarCount / Math.Max(1, geometry.PileCount));
            foreach (var center in pileCenters)
            {
                for (var index = 0; index < barsPerPile; index++)
                {
                    var angle = 2 * Math.PI * index / barsPerPile;
                    AddCircle(
                        builder,
                        center.X + steelRadius * Math.Cos(angle),
                        center.Y + steelRadius * Math.Sin(angle),
                        Math.Max(0.006, longitudinal.BarDiameterMm / 2000),
                        "PILE_LONGITUDINAL_REBAR");
                }
            }
        }

        var sectionX = planMaxX + 2.2;
        const double pileBottomCover = 0.03;
        AddBrokenVerticalMemberOutline(
            builder,
            sectionX,
            sectionX + diameter,
            -drawnPileLength,
            geometry.PedestalHeightM,
            "CAST_IN_PLACE_PILE",
            pileIsBroken);
        AddBrokenVerticalLine(
            builder,
            sectionX + cover,
            -drawnPileLength + pileBottomCover,
            geometry.PedestalHeightM,
            "PILE_LONGITUDINAL_REBAR",
            pileIsBroken);
        AddBrokenVerticalLine(
            builder,
            sectionX + diameter - cover,
            -drawnPileLength + pileBottomCover,
            geometry.PedestalHeightM,
            "PILE_LONGITUDINAL_REBAR",
            pileIsBroken);

        var hoop = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("桩身箍筋", StringComparison.Ordinal));
        if (hoop is not null && hoop.BarSpacingMm > 0)
        {
            var hoopSpacing = Math.Max(0.05, hoop.BarSpacingMm / 1000);
            var graphicHoopSpacing = Math.Max(
                0.12,
                hoopSpacing * drawnPileLength / Math.Max(geometry.PileLengthM, drawnPileLength));
            foreach (var elevation in SpacedCoordinates(
                         -drawnPileLength + cover,
                         geometry.PedestalHeightM - cover,
                         graphicHoopSpacing).Take(120))
            {
                if (pileIsBroken && IsWithinPileBreak(elevation))
                {
                    continue;
                }
                AddLine(
                    builder,
                    sectionX + cover,
                    elevation,
                    sectionX + diameter - cover,
                    elevation,
                    "PILE_STIRRUP");
            }

            AddText(
                builder,
                sectionX,
                geometry.PedestalHeightM + 0.35,
                0.16,
                $"GZH01螺旋箍筋：{DxfText(hoop.BarSpecification, 36)}");
        }

        AddTextOnLayer(
            builder,
            sectionX,
            geometry.PedestalHeightM + 0.65,
            0.15,
            $"GZH01；纵筋 {RebarLabel(longitudinal)}；螺旋箍 {RebarLabel(hoop)}",
            "ANNOTATION");
        AddTextOnLayer(
            builder,
            sectionX,
            geometry.PedestalHeightM + 0.92,
            0.13,
            "桩顶纵筋伸入上部塔柱或承台的锚固形式待连接设计确认",
            "ANNOTATION");

        AddGroundLine(builder, sectionX - 0.5, sectionX + diameter + 0.5, 0);
        AddHorizontalDimension(
            builder,
            sectionX,
            sectionX + diameter,
            -drawnPileLength,
            -drawnPileLength - 0.40,
            $"桩径={Millimetres(diameter)}");
        AddVerticalDimension(
            builder,
            sectionX,
            -drawnPileLength,
            0,
            sectionX + diameter + 0.48,
            $"桩长={Millimetres(geometry.PileLengthM)}");
        AddPileCageSection(
            builder,
            scheme,
            sectionX + diameter + 1.35,
            -Math.Min(1.10, drawnPileLength * 0.42),
            radius);
        AddViewTitle(
            builder,
            (planMinX + planMaxX) / 2,
            planMinY - 1.18,
            geometry.TieBeamCount > 0 ? "桩位及基础连系梁平面图" : "灌注桩平面定位图",
            "1:50");
        AddViewTitle(
            builder,
            sectionX + diameter / 2,
            -drawnPileLength - 1.12,
            "1-1 代表桩配筋纵剖面",
            pileIsBroken ? "纵向折断示意" : "1:50");
        AddText(
            builder,
            planMinX,
            planMaxY + 1.05,
            0.20,
            geometry.PileCount > 1
                ? $"{DescribeDrawingLegLayout(geometry.PileCount)}；GZH01共{geometry.PileCount}根；尺寸单位为毫米，标高单位为米"
                : $"GZH01共{geometry.PileCount}根；尺寸单位为毫米，标高单位为米");
        AddText(
            builder,
            sectionX,
            -drawnPileLength - 0.58,
            0.13,
            geometry.TieBeamCount > 0
                ? $"连系梁共{geometry.TieBeamCount}根；不设承台"
                : "不设承台；计算范围见右侧说明");
        if (pileIsBroken)
        {
            AddText(
                builder,
                sectionX,
                -drawnPileLength - 0.32,
                0.13,
                $"桩身中段折断示意；实际桩长={Millimetres(geometry.PileLengthM)}，长度以标注为准");
        }

        var tieBeamMain = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("连梁纵筋", StringComparison.Ordinal));
        var tieBeamStirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("连梁箍筋", StringComparison.Ordinal));
        if (tieBeamMain is not null)
        {
            AddText(
                builder,
                pileCenters.Min(item => item.X) - radius,
                pileCenters.Min(item => item.Y) - radius - 0.62,
                0.16,
                $"连系梁纵筋：{DxfText(tieBeamMain.BarSpecification, 36)}");
        }

        if (tieBeamStirrup is not null)
        {
            AddText(
                builder,
                pileCenters.Min(item => item.X) - radius,
                pileCenters.Min(item => item.Y) - radius - 0.92,
                0.16,
                $"连系梁箍筋：{DxfText(tieBeamStirrup.BarSpecification, 36)}");
        }

        if (geometry.TieBeamCount > 0)
        {
            AddTieBeamDetail(
                builder,
                scheme,
                planMinX,
                planMinY - 3.25,
                Math.Min(3.4, Math.Max(2.0, geometry.PileCenterSpacingM)));
        }
    }

    private static void AddIndependentFoundationTieBeamDxfEntities(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        var centers = IndependentPileCenters(
            geometry.FoundationUnitCount,
            geometry.PileCenterSpacingM);

        for (var index = 0; index < centers.Count; index++)
        {
            var next = centers[(index + 1) % centers.Count];
            AddBeamOutline(
                builder,
                centers[index],
                next,
                geometry.TieBeamWidthM,
                "TIE_BEAM");
        }

        var supportSpan = scheme.FoundationType == FoundationType.RigidShortPile
            ? geometry.PileDiameterM
            : Math.Max(geometry.BaseLengthM, geometry.BaseWidthM);
        var planMinX = centers.Min(item => item.X) - supportSpan / 2;
        var planMaxX = centers.Max(item => item.X) + supportSpan / 2;
        var planMinY = centers.Min(item => item.Y) - supportSpan / 2;
        var planMaxY = centers.Max(item => item.Y) + supportSpan / 2;
        var drawnPileLength = DrawingPileLengthM(geometry.PileLengthM);
        var pileIsBroken = geometry.PileLengthM > drawnPileLength + 0.05;

        for (var index = 0; index < centers.Count; index++)
        {
            var center = centers[index];
            if (scheme.FoundationType == FoundationType.RigidShortPile)
            {
                AddCircle(
                    builder,
                    center.X,
                    center.Y,
                    geometry.PileDiameterM / 2,
                    "INDEPENDENT_FOUNDATION");
                AddRigidFoundationPlanReinforcement(builder, scheme, center);
                AddTowerLegMarker(builder, center, index + 1, supportSpan / 2, supportSpan / 2);
                continue;
            }

            AddRectangle(
                builder,
                center.X - geometry.BaseLengthM / 2,
                center.Y - geometry.BaseWidthM / 2,
                center.X + geometry.BaseLengthM / 2,
                center.Y + geometry.BaseWidthM / 2,
                "INDEPENDENT_FOUNDATION");
            if (scheme.FoundationType == FoundationType.CircularShortColumn)
            {
                AddCircle(
                    builder,
                    center.X,
                    center.Y,
                    geometry.PedestalLengthM / 2,
                    "PEDESTAL");
            }
            else if (scheme.FoundationType is not FoundationType.RigidRectangularShortPile)
            {
                AddRectangle(
                    builder,
                    center.X - geometry.PedestalLengthM / 2,
                    center.Y - geometry.PedestalWidthM / 2,
                    center.X + geometry.PedestalLengthM / 2,
                    center.Y + geometry.PedestalWidthM / 2,
                    "PEDESTAL");
            }

            if (scheme.FoundationType is FoundationType.RectangularShortColumn or
                FoundationType.CircularShortColumn or FoundationType.Raft)
            {
                AddFoundationRebarPlanAt(builder, scheme, center.X, center.Y);
            }
            else if (scheme.FoundationType == FoundationType.RigidRectangularShortPile)
            {
                AddRigidFoundationPlanReinforcement(builder, scheme, center);
            }
            AddTextOnLayer(
                builder,
                center.X - supportSpan / 2,
                center.Y + supportSpan / 2 + 0.18,
                0.12,
                scheme.FoundationType is FoundationType.RectangularShortColumn or FoundationType.CircularShortColumn
                    ? "DJj01"
                    : "基础单元-01",
                "ANNOTATION");
            AddTowerLegMarker(
                builder,
                center,
                index + 1,
                geometry.BaseLengthM / 2,
                geometry.BaseWidthM / 2);
        }

        for (var index = 0; index < centers.Count; index++)
        {
            AddTieBeamPlanLabel(
                builder,
                centers[index],
                centers[(index + 1) % centers.Count],
                index + 1,
                geometry.TieBeamWidthM,
                geometry.TieBeamHeightM);
        }
        AddTextOnLayer(
            builder,
            planMinX,
            planMaxY + 0.25,
            0.14,
            BuildTowerLegSpacingNote(centers.Count, geometry.PileCenterSpacingM),
            "ANNOTATION");

        AddPlanAxes(
            builder,
            planMinX,
            planMinY,
            planMaxX,
            planMaxY,
            BuildAxisDefinitions(centers.Select(item => item.X), numeric: true),
            BuildAxisDefinitions(centers.Select(item => item.Y), numeric: false));
        AddHorizontalDimension(
            builder,
            centers.Min(item => item.X),
            centers.Max(item => item.X),
            planMinY,
            planMinY - 0.55,
            centers.Count == 3
                ? $"底边塔脚中心距 {Millimetres(geometry.PileCenterSpacingM)}"
                : $"塔脚中心距 {Millimetres(geometry.PileCenterSpacingM)}");
        AddVerticalDimension(
            builder,
            planMinX,
            centers.Min(item => item.Y),
            centers.Max(item => item.Y),
            planMinX - 0.55,
            centers.Count == 3
                ? $"三角形高 {Millimetres(centers.Max(item => item.Y) - centers.Min(item => item.Y))}"
                : $"塔脚中心距 {Millimetres(geometry.PileCenterSpacingM)}");
        AddSectionCutMark(builder, planMinX - 0.18, planMaxX + 0.18, centers[0].Y, "1");

        var sectionX = centers.Max(item => item.X) + supportSpan / 2 + 2.0;
        if (scheme.FoundationType == FoundationType.RigidShortPile)
        {
            AddBrokenVerticalMemberOutline(
                builder,
                sectionX,
                sectionX + geometry.PileDiameterM,
                -drawnPileLength,
                geometry.PedestalHeightM,
                "INDEPENDENT_FOUNDATION_SECTION",
                pileIsBroken);
            AddRigidRepresentativeCageSection(
                builder,
                scheme,
                sectionX,
                geometry.PileDiameterM,
                drawnPileLength,
                pileIsBroken);
        }
        else if (scheme.FoundationType == FoundationType.RigidRectangularShortPile)
        {
            AddBrokenVerticalMemberOutline(
                builder,
                sectionX,
                sectionX + geometry.BaseLengthM,
                -drawnPileLength,
                geometry.PedestalHeightM,
                "INDEPENDENT_FOUNDATION_SECTION",
                pileIsBroken);
            AddRigidRepresentativeCageSection(
                builder,
                scheme,
                sectionX,
                geometry.BaseLengthM,
                drawnPileLength,
                pileIsBroken);
        }
        else
        {
            AddRectangle(
                builder,
                sectionX,
                0,
                sectionX + geometry.BaseLengthM,
                geometry.BaseThicknessM,
                "INDEPENDENT_FOUNDATION_SECTION");
            var pedestalStartX =
                sectionX + (geometry.BaseLengthM - geometry.PedestalLengthM) / 2;
            AddRectangle(
                builder,
                pedestalStartX,
                geometry.BaseThicknessM,
                pedestalStartX + geometry.PedestalLengthM,
                geometry.BaseThicknessM + geometry.PedestalHeightM,
                "PEDESTAL_SECTION");
            AddConcreteHatch(
                builder,
                sectionX,
                0,
                sectionX + geometry.BaseLengthM,
                geometry.BaseThicknessM,
                0.34);
            AddRectangle(
                builder,
                sectionX - 0.10,
                -0.10,
                sectionX + geometry.BaseLengthM + 0.10,
                0,
                "BLINDING");
            AddReinforcementSectionBars(builder, scheme, sectionX);
            AddPedestalReinforcementEntities(builder, scheme, sectionX);
            AddHorizontalDimension(
                builder,
                sectionX,
                sectionX + geometry.BaseLengthM,
                -0.10,
                -0.43,
                Millimetres(geometry.BaseLengthM));
            AddVerticalDimension(
                builder,
                sectionX + geometry.BaseLengthM,
                0,
                geometry.BaseThicknessM,
                sectionX + geometry.BaseLengthM + 0.42,
                Millimetres(geometry.BaseThicknessM));
        }

        AddGroundLine(
            builder,
            sectionX - 0.45,
            sectionX + (scheme.FoundationType == FoundationType.RigidShortPile
                ? geometry.PileDiameterM
                : geometry.BaseLengthM) + 0.45,
            scheme.FoundationType is FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile
                ? 0
                : geometry.BaseThicknessM + Math.Min(0.25, geometry.PedestalHeightM / 3));
        AddViewTitle(
            builder,
            (planMinX + planMaxX) / 2,
            planMinY - 1.15,
            "独立基础及基础连系梁平面图",
            "1:50");
        AddViewTitle(
            builder,
            sectionX + supportSpan / 2,
            scheme.FoundationType is FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile
                ? -drawnPileLength - 0.85
                : -0.78,
            "1-1 代表基础剖面",
            pileIsBroken ? "纵向折断示意" : "1:50");
        if (pileIsBroken &&
            scheme.FoundationType is (FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile))
        {
            AddText(
                builder,
                sectionX,
                -drawnPileLength - 0.38,
                0.13,
                $"桩身中段折断示意；实际桩长={Millimetres(geometry.PileLengthM)}，长度以标注为准");
        }

        var top = centers.Max(item => item.Y) + supportSpan / 2;
        var bottom = centers.Min(item => item.Y) - supportSpan / 2;
        AddText(
            builder,
            centers.Min(item => item.X) - supportSpan / 2,
            top + 0.56,
            0.20,
            $"{DescribeDrawingLegLayout(geometry.FoundationUnitCount)}；独立基础单元{geometry.FoundationUnitCount}个；闭合周边连系梁{geometry.TieBeamCount}根");
        AddText(
            builder,
            sectionX,
            Math.Max(geometry.PedestalHeightM, geometry.BaseThicknessM + geometry.PedestalHeightM) + 0.35,
            0.18,
            scheme.FoundationType is FoundationType.RectangularShortColumn or FoundationType.CircularShortColumn
                ? $"DJj01；B：X {RebarLabel(scheme.ReinforcementDesigns.FirstOrDefault(item => item.Direction == "X向"))}；Y {RebarLabel(scheme.ReinforcementDesigns.FirstOrDefault(item => item.Direction == "Y向"))}"
                : "代表基础剖面；已确认配筋按图示，未确认项不绘假配筋");

        var tieBeamMain = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("连系梁纵筋", StringComparison.Ordinal));
        var tieBeamStirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("连系梁箍筋", StringComparison.Ordinal));
        AddText(
            builder,
            centers.Min(item => item.X) - supportSpan / 2,
            bottom - 0.45,
            0.16,
            tieBeamMain is null
                ? "JLL01内力待补；未假定配筋"
                : $"JLL01纵筋：{DxfText(tieBeamMain.BarSpecification, 36)}");
        if (tieBeamStirrup is not null)
        {
            AddText(
                builder,
                centers.Min(item => item.X) - supportSpan / 2,
                bottom - 0.75,
                0.16,
                $"JLL01箍筋：{DxfText(tieBeamStirrup.BarSpecification, 36)}");
        }

        AddTieBeamDetail(
            builder,
            scheme,
            planMinX,
            planMinY - 3.20,
            Math.Min(3.4, Math.Max(2.0, geometry.PileCenterSpacingM)));
    }

    private static void AddFoundationRebarPlanAt(
        StringBuilder builder,
        FoundationScheme scheme,
        double centerX,
        double centerY)
    {
        var geometry = scheme.Geometry;
        const double cover = 0.05;
        var xDesign = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Direction == "X向" && item.Status == CheckStatus.Pass);
        var yDesign = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Direction == "Y向" && item.Status == CheckStatus.Pass);
        var left = centerX - geometry.BaseLengthM / 2 + cover;
        var top = centerY + geometry.BaseWidthM / 2 - cover;
        var revealWidth = Math.Max(0.25, geometry.BaseLengthM * 0.52 - cover);
        var revealHeight = Math.Max(0.25, geometry.BaseWidthM * 0.52 - cover);
        var bottom = top - revealHeight;
        var right = left + revealWidth;

        if (xDesign is not null)
        {
            foreach (var y in SpacedCoordinates(
                         bottom,
                         top,
                         Math.Max(0.14, xDesign.BarSpacingMm / 1000)).Take(40))
            {
                var ratio = Math.Clamp((y - bottom) / revealHeight, 0, 1);
                AddLine(
                    builder,
                    left,
                    y,
                    left + revealWidth * (0.28 + 0.72 * Math.Sqrt(ratio)),
                    y,
                    "REBAR_BOTTOM_X");
            }
        }
        if (yDesign is not null)
        {
            foreach (var x in SpacedCoordinates(
                         left,
                         right,
                         Math.Max(0.14, yDesign.BarSpacingMm / 1000)).Take(40))
            {
                var ratio = Math.Clamp((x - left) / revealWidth, 0, 1);
                AddLine(
                    builder,
                    x,
                    bottom + revealHeight * Math.Pow(ratio, 1.65),
                    x,
                    top,
                    "REBAR_BOTTOM_Y");
            }
        }
        AddOpenPolyline(
            builder,
            Enumerable.Range(0, 13)
                .Select(index =>
                {
                    var ratio = index / 12d;
                    return (
                        X: left + revealWidth * (0.28 + 0.72 * Math.Sqrt(ratio)),
                        Y: bottom + revealHeight * ratio);
                })
                .ToList(),
            "REBAR_REVEAL_BOUNDARY");
    }

    private static IReadOnlyList<(double X, double Y)> IndependentPileCenters(
        int pileCount,
        double spacing)
    {
        if (pileCount == 3)
        {
            var height = Math.Sqrt(3) * spacing / 2;
            return
            [
                (-spacing / 2, -height / 3),
                (spacing / 2, -height / 3),
                (0, 2 * height / 3)
            ];
        }

        if (pileCount == 4)
        {
            return
            [
                (-spacing / 2, -spacing / 2),
                (spacing / 2, -spacing / 2),
                (spacing / 2, spacing / 2),
                (-spacing / 2, spacing / 2)
            ];
        }

        return [(0, 0)];
    }

    private static int ResolveDrawingTowerLegCount(TowerMastInput tower) =>
        tower.FoundationLegCount is 3 or 4
            ? tower.FoundationLegCount
            : tower.StructureType switch
            {
                TowerStructureType.ThreeTube => 3,
                TowerStructureType.HeighteningFrame => 3,
                TowerStructureType.AngleSteel => 4,
                _ => 1
            };

    private static string FormatDrawingFoundationType(ProjectModel project, FoundationScheme scheme)
    {
        var legCount = ResolveDrawingTowerLegCount(project.TowerMast);
        return scheme.FoundationType == FoundationType.Raft && legCount > 1
            ? $"共用整体筏板基础（{legCount}塔脚）"
            : FormatFoundationType(scheme);
    }

    private static string DescribeDrawingLegLayout(int count) =>
        count == 3
            ? "三管塔或三塔脚增高架三角形布置"
            : count == 4
                ? "角钢塔或四塔脚增高架四角布置"
                : "单塔脚布置";

    private static string BuildTowerLegSpacingNote(int count, double spacing) =>
        count == 3
            ? $"塔脚1～3按等边三角形定位，三边中心距均为{Millimetres(spacing)}"
            : count == 4
                ? $"塔脚1～4按正方形四角定位，纵横中心距均为{Millimetres(spacing)}"
                : string.Empty;

    private static void AddTowerLegMarker(
        StringBuilder builder,
        (double X, double Y) center,
        int number,
        double halfLength,
        double halfWidth)
    {
        var markerX = center.X + Math.Max(0.22, halfLength * 0.54);
        var markerY = center.Y + Math.Max(0.22, halfWidth * 0.54);
        AddCircle(builder, markerX, markerY, 0.16, "TOWER_LEG_MARK");
        AddCenteredText(builder, markerX, markerY, 0.13, number.ToString(CultureInfo.InvariantCulture), "TOWER_LEG_MARK");
        AddTextOnLayer(
            builder,
            markerX + 0.20,
            markerY - 0.05,
            0.12,
            $"塔脚{number}",
            "ANNOTATION");
    }

    private static void AddTieBeamPlanLabel(
        StringBuilder builder,
        (double X, double Y) start,
        (double X, double Y) end,
        int number,
        double width,
        double height)
    {
        var midX = (start.X + end.X) / 2;
        var midY = (start.Y + end.Y) / 2;
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180 / Math.PI;
        if (angle > 90 || angle < -90)
        {
            angle += 180;
        }
        AddCenteredText(
            builder,
            midX,
            midY + 0.18,
            0.12,
            $"JLL01-{number}  {Millimetres(width)}×{Millimetres(height)}",
            "ANNOTATION",
            angle);
    }

    private static void AddRigidFoundationPlanReinforcement(
        StringBuilder builder,
        FoundationScheme scheme,
        (double X, double Y) center)
    {
        var main = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("刚性", StringComparison.Ordinal) &&
            item.Component.Contains("纵筋", StringComparison.Ordinal) &&
            item.Status == CheckStatus.Pass);
        if (main is null || main.BarCount <= 0)
        {
            return;
        }

        var unitCount = Math.Max(1, scheme.Geometry.FoundationUnitCount);
        var barsPerUnit = Math.Max(1, main.BarCount / unitCount);
        const double cover = 0.05;
        var barRadius = Math.Max(0.008, main.BarDiameterMm / 2000);
        if (scheme.FoundationType == FoundationType.RigidShortPile)
        {
            var steelRadius = Math.Max(0.06, scheme.Geometry.PileDiameterM / 2 - cover);
            AddCircle(builder, center.X, center.Y, steelRadius, "RIGID_STIRRUP");
            for (var index = 0; index < barsPerUnit; index++)
            {
                var angle = 2 * Math.PI * index / barsPerUnit;
                AddCircle(
                    builder,
                    center.X + steelRadius * Math.Cos(angle),
                    center.Y + steelRadius * Math.Sin(angle),
                    barRadius,
                    "RIGID_LONGITUDINAL_REBAR");
            }
            return;
        }

        var halfLength = scheme.Geometry.BaseLengthM / 2 - cover;
        var halfWidth = scheme.Geometry.BaseWidthM / 2 - cover;
        AddRectangle(
            builder,
            center.X - halfLength,
            center.Y - halfWidth,
            center.X + halfLength,
            center.Y + halfWidth,
            "RIGID_RECT_STIRRUP");
        foreach (var point in PerimeterBarPoints(
                     center.X - halfLength,
                     center.Y - halfWidth,
                     center.X + halfLength,
                     center.Y + halfWidth,
                     barsPerUnit))
        {
            AddCircle(builder, point.X, point.Y, barRadius, "RIGID_RECT_LONGITUDINAL_REBAR");
        }
    }

    private static void AddRigidRepresentativeCageSection(
        StringBuilder builder,
        FoundationScheme scheme,
        double sectionX,
        double sectionWidth,
        double drawnLength,
        bool isBroken)
    {
        var main = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("刚性", StringComparison.Ordinal) &&
            item.Component.Contains("纵筋", StringComparison.Ordinal) &&
            item.Status == CheckStatus.Pass);
        var stirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component.Contains("刚性", StringComparison.Ordinal) &&
            item.Component.Contains("箍筋", StringComparison.Ordinal) &&
            item.Status == CheckStatus.Pass);
        const double cover = 0.05;
        const double bottomCover = 0.03;
        var longitudinalLayer = scheme.FoundationType == FoundationType.RigidShortPile
            ? "RIGID_LONGITUDINAL_REBAR"
            : "RIGID_RECT_LONGITUDINAL_REBAR";
        var stirrupLayer = scheme.FoundationType == FoundationType.RigidShortPile
            ? "RIGID_STIRRUP"
            : "RIGID_RECT_STIRRUP";
        if (main is not null)
        {
            AddBrokenVerticalLine(
                builder,
                sectionX + cover,
                -drawnLength + bottomCover,
                scheme.Geometry.PedestalHeightM,
                longitudinalLayer,
                isBroken);
            AddBrokenVerticalLine(
                builder,
                sectionX + sectionWidth - cover,
                -drawnLength + bottomCover,
                scheme.Geometry.PedestalHeightM,
                longitudinalLayer,
                isBroken);
        }
        if (stirrup is null || stirrup.BarSpacingMm <= 0)
        {
            return;
        }

        var graphicSpacing = Math.Max(
            0.12,
            stirrup.BarSpacingMm / 1000 * drawnLength /
            Math.Max(scheme.Geometry.PileLengthM, drawnLength));
        foreach (var elevation in SpacedCoordinates(
                     -drawnLength + cover,
                     scheme.Geometry.PedestalHeightM - cover,
                     graphicSpacing).Take(120))
        {
            if (isBroken && IsWithinPileBreak(elevation))
            {
                continue;
            }
            AddLine(
                builder,
                sectionX + cover,
                elevation,
                sectionX + sectionWidth - cover,
                elevation,
                stirrupLayer);
        }
    }

    private static void AddBeamOutline(
        StringBuilder builder,
        (double X, double Y) start,
        (double X, double Y) end,
        double width,
        string layer)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-9 || width <= 0)
        {
            return;
        }

        var offsetX = -dy / length * width / 2;
        var offsetY = dx / length * width / 2;
        AddLine(builder, start.X + offsetX, start.Y + offsetY, end.X + offsetX, end.Y + offsetY, layer);
        AddLine(builder, start.X - offsetX, start.Y - offsetY, end.X - offsetX, end.Y - offsetY, layer);
    }

    private static void AddRigidShortPileDxfEntities(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        var diameter = geometry.PileDiameterM;
        var radius = diameter / 2;
        var cover = 0.05;
        var drawnPileLength = DrawingPileLengthM(geometry.PileLengthM);
        var pileIsBroken = geometry.PileLengthM > drawnPileLength + 0.05;
        AddCircle(builder, 0, 0, radius, "RIGID_SHORT_PILE");
        AddPlanAxes(builder, -radius, -radius, radius, radius, [(0d, "1")], [(0d, "甲")]);
        AddHorizontalDimension(builder, -radius, radius, -radius, -radius - 0.48, $"桩径={Millimetres(diameter)}");
        AddVerticalDimension(builder, -radius, -radius, radius, -radius - 0.48, $"桩径={Millimetres(diameter)}");

        var longitudinal = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component == "刚性短柱桩纵筋");
        if (longitudinal is not null && longitudinal.BarCount > 0)
        {
            var steelRadius = Math.Max(0.05, radius - cover);
            AddCircle(builder, 0, 0, steelRadius, "RIGID_STIRRUP");
            for (var index = 0; index < longitudinal.BarCount; index++)
            {
                var angle = 2 * Math.PI * index / longitudinal.BarCount;
                AddCircle(
                    builder,
                    steelRadius * Math.Cos(angle),
                    steelRadius * Math.Sin(angle),
                    Math.Max(0.006, longitudinal.BarDiameterMm / 2000),
                    "RIGID_LONGITUDINAL_REBAR");
            }
            AddLeader(
                builder,
                steelRadius * 0.70,
                steelRadius * 0.70,
                -radius,
                radius + 0.48,
                $"纵筋：{RebarLabel(longitudinal)}");
        }

        var sectionX = radius + 2.0;
        AddBrokenVerticalMemberOutline(
            builder,
            sectionX,
            sectionX + diameter,
            -drawnPileLength,
            geometry.PedestalHeightM,
            "RIGID_SHORT_PILE",
            pileIsBroken);
        const double pileBottomCover = 0.03;
        AddBrokenVerticalLine(
            builder,
            sectionX + cover,
            -drawnPileLength + pileBottomCover,
            geometry.PedestalHeightM,
            "RIGID_LONGITUDINAL_REBAR",
            pileIsBroken);
        AddSectionCutMark(builder, sectionX - 0.18, sectionX + diameter + 0.18, -1.05, "1");
        AddBrokenVerticalLine(
            builder,
            sectionX + diameter - cover,
            -drawnPileLength + pileBottomCover,
            geometry.PedestalHeightM,
            "RIGID_LONGITUDINAL_REBAR",
            pileIsBroken);

        var stirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component == "刚性短柱桩箍筋");
        if (stirrup is not null && stirrup.BarSpacingMm > 0)
        {
            var actualSpacing = Math.Max(0.05, stirrup.BarSpacingMm / 1000);
            var graphicSpacing = Math.Max(
                0.12,
                actualSpacing * drawnPileLength / Math.Max(geometry.PileLengthM, drawnPileLength));
            foreach (var elevation in SpacedCoordinates(
                         -drawnPileLength,
                         geometry.PedestalHeightM,
                         graphicSpacing))
            {
                if (pileIsBroken && IsWithinPileBreak(elevation))
                {
                    continue;
                }
                AddLine(
                    builder,
                    sectionX + cover,
                    elevation,
                    sectionX + diameter - cover,
                    elevation,
                    "RIGID_STIRRUP");
            }
            AddLeader(
                builder,
                sectionX + diameter - cover,
                geometry.PedestalHeightM - cover,
                sectionX + diameter + 0.30,
                geometry.PedestalHeightM + 0.35,
                $"箍筋：{RebarLabel(stirrup)}");
        }

        AddGroundLine(builder, sectionX - 0.45, sectionX + diameter + 0.45, 0);
        AddTextOnLayer(
            builder,
            -radius,
            radius + 0.82,
            0.15,
            $"刚性短柱桩-01  直径{Millimetres(diameter)}  桩长{Millimetres(geometry.PileLengthM)}",
            "ANNOTATION");
        AddTextOnLayer(
            builder,
            sectionX,
            geometry.PedestalHeightM + 0.66,
            0.14,
            $"纵筋 {RebarLabel(longitudinal)}；箍筋 {RebarLabel(stirrup)}",
            "ANNOTATION");
        AddVerticalDimension(
            builder,
            sectionX,
            -drawnPileLength,
            0,
            sectionX + diameter + 0.45,
            $"桩长={Millimetres(geometry.PileLengthM)}");
        AddViewTitle(builder, 0, -radius - 1.05, "刚性短柱桩1-1断面", "1:25");
        AddViewTitle(
            builder,
            sectionX + diameter / 2,
            -drawnPileLength - 1.02,
            "刚性短柱桩配筋纵剖面",
            pileIsBroken ? "纵向折断示意" : "1:50");
        AddText(
            builder,
            sectionX,
            -drawnPileLength - 0.46,
            0.13,
            pileIsBroken
                ? $"中段折断；实际桩长={Millimetres(geometry.PileLengthM)}，长度以标注为准"
                : "已确认配筋按图示表达；待复核项见右侧说明");
    }

    private static void AddRigidRectangularShortPileDxfEntities(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        var length = geometry.BaseLengthM;
        var width = geometry.BaseWidthM;
        var halfLength = length / 2;
        var halfWidth = width / 2;
        const double cover = 0.05;
        var drawnPileLength = DrawingPileLengthM(geometry.PileLengthM);
        var pileIsBroken = geometry.PileLengthM > drawnPileLength + 0.05;
        AddRectangle(
            builder,
            -halfLength,
            -halfWidth,
            halfLength,
            halfWidth,
            "RIGID_RECT_SHORT_PILE");
        AddPlanAxes(builder, -halfLength, -halfWidth, halfLength, halfWidth, [(0d, "1")], [(0d, "甲")]);
        AddHorizontalDimension(
            builder,
            -halfLength,
            halfLength,
            -halfWidth,
            -halfWidth - 0.48,
            Millimetres(length));
        AddVerticalDimension(
            builder,
            -halfLength,
            -halfWidth,
            halfWidth,
            -halfLength - 0.48,
            Millimetres(width));

        var longitudinal = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component == "刚性短柱桩－矩形纵筋");
        if (longitudinal is not null && longitudinal.BarCount > 0)
        {
            var steelHalfLength = Math.Max(0.05, halfLength - cover);
            var steelHalfWidth = Math.Max(0.05, halfWidth - cover);
            AddRectangle(
                builder,
                -steelHalfLength,
                -steelHalfWidth,
                steelHalfLength,
                steelHalfWidth,
                "RIGID_RECT_STIRRUP");
            var horizontalLength = 2 * steelHalfLength;
            var verticalLength = 2 * steelHalfWidth;
            var perimeter = 2 * (horizontalLength + verticalLength);
            for (var index = 0; index < longitudinal.BarCount; index++)
            {
                var distance = perimeter * index / longitudinal.BarCount;
                double x;
                double y;
                if (distance <= horizontalLength)
                {
                    x = -steelHalfLength + distance;
                    y = -steelHalfWidth;
                }
                else if (distance <= horizontalLength + verticalLength)
                {
                    x = steelHalfLength;
                    y = -steelHalfWidth + distance - horizontalLength;
                }
                else if (distance <= 2 * horizontalLength + verticalLength)
                {
                    x = steelHalfLength -
                        (distance - horizontalLength - verticalLength);
                    y = steelHalfWidth;
                }
                else
                {
                    x = -steelHalfLength;
                    y = steelHalfWidth -
                        (distance - 2 * horizontalLength - verticalLength);
                }
                AddCircle(
                    builder,
                    x,
                    y,
                    Math.Max(0.006, longitudinal.BarDiameterMm / 2000),
                    "RIGID_RECT_LONGITUDINAL_REBAR");
            }
            AddLeader(
                builder,
                steelHalfLength,
                steelHalfWidth,
                -halfLength,
                halfWidth + 0.48,
                $"纵筋：{RebarLabel(longitudinal)}");
        }

        var sectionX = halfLength + 2.0;
        AddBrokenVerticalMemberOutline(
            builder,
            sectionX,
            sectionX + length,
            -drawnPileLength,
            geometry.PedestalHeightM,
            "RIGID_RECT_SHORT_PILE",
            pileIsBroken);
        const double pileBottomCover = 0.03;
        AddBrokenVerticalLine(
            builder,
            sectionX + cover,
            -drawnPileLength + pileBottomCover,
            geometry.PedestalHeightM,
            "RIGID_RECT_LONGITUDINAL_REBAR",
            pileIsBroken);
        AddSectionCutMark(builder, sectionX - 0.18, sectionX + length + 0.18, -1.05, "1");
        AddBrokenVerticalLine(
            builder,
            sectionX + length - cover,
            -drawnPileLength + pileBottomCover,
            geometry.PedestalHeightM,
            "RIGID_RECT_LONGITUDINAL_REBAR",
            pileIsBroken);

        var stirrup = scheme.ReinforcementDesigns.FirstOrDefault(item =>
            item.Component == "刚性短柱桩－矩形箍筋");
        if (stirrup is not null && stirrup.BarSpacingMm > 0)
        {
            var actualSpacing = Math.Max(0.05, stirrup.BarSpacingMm / 1000);
            var graphicSpacing = Math.Max(
                0.12,
                actualSpacing * drawnPileLength / Math.Max(geometry.PileLengthM, drawnPileLength));
            foreach (var elevation in SpacedCoordinates(
                         -drawnPileLength,
                         geometry.PedestalHeightM,
                         graphicSpacing))
            {
                if (pileIsBroken && IsWithinPileBreak(elevation))
                {
                    continue;
                }
                AddLine(
                    builder,
                    sectionX + cover,
                    elevation,
                    sectionX + length - cover,
                    elevation,
                    "RIGID_RECT_STIRRUP");
            }
            AddLeader(
                builder,
                sectionX + length - cover,
                geometry.PedestalHeightM - cover,
                sectionX + length + 0.30,
                geometry.PedestalHeightM + 0.35,
                $"箍筋：{RebarLabel(stirrup)}");
        }

        AddGroundLine(builder, sectionX - 0.45, sectionX + length + 0.45, 0);
        AddTextOnLayer(
            builder,
            -halfLength,
            halfWidth + 0.82,
            0.15,
            $"刚性短柱桩-01  {Millimetres(length)}×{Millimetres(width)}  桩长{Millimetres(geometry.PileLengthM)}",
            "ANNOTATION");
        AddTextOnLayer(
            builder,
            sectionX,
            geometry.PedestalHeightM + 0.66,
            0.14,
            $"纵筋 {RebarLabel(longitudinal)}；箍筋 {RebarLabel(stirrup)}",
            "ANNOTATION");
        AddVerticalDimension(
            builder,
            sectionX,
            -drawnPileLength,
            0,
            sectionX + length + 0.45,
            $"桩长={Millimetres(geometry.PileLengthM)}");
        AddViewTitle(builder, 0, -halfWidth - 1.05, "矩形刚性短柱桩1-1断面", "1:25");
        AddViewTitle(
            builder,
            sectionX + length / 2,
            -drawnPileLength - 1.02,
            "矩形刚性短柱桩配筋纵剖面",
            pileIsBroken ? "纵向折断示意" : "1:50");
        AddText(
            builder,
            sectionX,
            -drawnPileLength - 0.46,
            0.13,
            pileIsBroken
                ? $"中段折断；实际桩长={Millimetres(geometry.PileLengthM)}，长度以标注为准"
                : "已确认配筋按图示表达；待复核项见右侧说明");
    }

    private const double MaximumPileDrawingLengthM = 6.2;
    private const double PileBreakCenterY = -3.1;
    private const double PileBreakHalfGap = 0.13;

    private static double DrawingPileLengthM(double actualLengthM) =>
        actualLengthM > 0
            ? Math.Min(actualLengthM, MaximumPileDrawingLengthM)
            : MaximumPileDrawingLengthM;

    private static bool IsWithinPileBreak(double y) =>
        Math.Abs(y - PileBreakCenterY) <= PileBreakHalfGap + 0.03;

    private static void AddBrokenVerticalMemberOutline(
        StringBuilder builder,
        double x1,
        double x2,
        double bottom,
        double top,
        string layer,
        bool isBroken)
    {
        if (!isBroken)
        {
            AddRectangle(builder, x1, bottom, x2, top, layer);
            return;
        }

        var lowerBreakY = PileBreakCenterY - PileBreakHalfGap;
        var upperBreakY = PileBreakCenterY + PileBreakHalfGap;
        AddLine(builder, x1, bottom, x2, bottom, layer);
        AddLine(builder, x1, top, x2, top, layer);
        AddLine(builder, x1, bottom, x1, lowerBreakY, layer);
        AddLine(builder, x1, upperBreakY, x1, top, layer);
        AddLine(builder, x2, bottom, x2, lowerBreakY, layer);
        AddLine(builder, x2, upperBreakY, x2, top, layer);
        AddPileBreakSawtooth(builder, x1, x2, lowerBreakY, layer, upward: true);
        AddPileBreakSawtooth(builder, x1, x2, upperBreakY, layer, upward: false);
    }

    private static void AddBrokenVerticalLine(
        StringBuilder builder,
        double x,
        double bottom,
        double top,
        string layer,
        bool isBroken)
    {
        if (!isBroken)
        {
            AddLine(builder, x, bottom, x, top, layer);
            return;
        }

        AddLine(builder, x, bottom, x, PileBreakCenterY - PileBreakHalfGap, layer);
        AddLine(builder, x, PileBreakCenterY + PileBreakHalfGap, x, top, layer);
    }

    private static void AddPileBreakSawtooth(
        StringBuilder builder,
        double x1,
        double x2,
        double y,
        string layer,
        bool upward)
    {
        var width = x2 - x1;
        var amplitude = (upward ? 1 : -1) * 0.09;
        var points = new[]
        {
            (X: x1, Y: y),
            (X: x1 + width * 0.25, Y: y + amplitude),
            (X: x1 + width * 0.50, Y: y - amplitude),
            (X: x1 + width * 0.75, Y: y + amplitude),
            (X: x2, Y: y)
        };
        for (var index = 0; index < points.Length - 1; index++)
        {
            AddLine(
                builder,
                points[index].X,
                points[index].Y,
                points[index + 1].X,
                points[index + 1].Y,
                layer);
        }
    }

    private static void AddRectangle(
        StringBuilder builder,
        double x1,
        double y1,
        double x2,
        double y2,
        string layer)
    {
        AddLine(builder, x1, y1, x2, y1, layer);
        AddLine(builder, x2, y1, x2, y2, layer);
        AddLine(builder, x2, y2, x1, y2, layer);
        AddLine(builder, x1, y2, x1, y1, layer);
    }

    private static void AddOpenPolyline(
        StringBuilder builder,
        IReadOnlyList<(double X, double Y)> points,
        string layer)
    {
        for (var index = 0; index + 1 < points.Count; index++)
        {
            AddLine(
                builder,
                points[index].X,
                points[index].Y,
                points[index + 1].X,
                points[index + 1].Y,
                layer);
        }
    }

    private static void AddRebarCallout(
        StringBuilder builder,
        double targetX,
        double targetY,
        double bubbleX,
        double bubbleY,
        string number,
        string label)
    {
        AddCircle(builder, bubbleX, bubbleY, 0.15, "LEADER");
        AddCenteredText(builder, bubbleX, bubbleY, 0.12, number, "ANNOTATION");
        AddLine(builder, targetX, targetY, bubbleX + 0.15, bubbleY, "LEADER");
        AddLine(builder, bubbleX + 0.15, bubbleY, bubbleX + 0.54, bubbleY, "LEADER");
        AddTextOnLayer(builder, bubbleX + 0.20, bubbleY + 0.10, 0.13, label, "ANNOTATION");
    }

    private static IReadOnlyList<(double X, double Y)> PerimeterBarPoints(
        double x1,
        double y1,
        double x2,
        double y2,
        int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var width = Math.Max(0, x2 - x1);
        var height = Math.Max(0, y2 - y1);
        var perimeter = 2 * (width + height);
        if (perimeter <= 1e-9)
        {
            return [(x1, y1)];
        }

        var result = new List<(double X, double Y)>(count);
        for (var index = 0; index < count; index++)
        {
            var distance = perimeter * index / count;
            if (distance <= width)
            {
                result.Add((x1 + distance, y1));
            }
            else if (distance <= width + height)
            {
                result.Add((x2, y1 + distance - width));
            }
            else if (distance <= 2 * width + height)
            {
                result.Add((x2 - (distance - width - height), y2));
            }
            else
            {
                result.Add((x1, y2 - (distance - 2 * width - height)));
            }
        }
        return result;
    }

    private static void AddLine(
        StringBuilder builder,
        double x1,
        double y1,
        double x2,
        double y2,
        string layer)
    {
        var width = DxfLineWidth(layer);
        if (width > 0)
        {
            AddPair(builder, 0, "POLYLINE");
            AddPair(builder, 8, layer);
            AddPair(builder, 66, "1");
            AddPair(builder, 10, "0");
            AddPair(builder, 20, "0");
            AddPair(builder, 30, "0");
            AddPair(builder, 70, "0");
            AddPair(builder, 40, Invariant(width));
            AddPair(builder, 41, Invariant(width));
            AddDxfPolylineVertex(builder, x1, y1, layer, width);
            AddDxfPolylineVertex(builder, x2, y2, layer, width);
            AddPair(builder, 0, "SEQEND");
            AddPair(builder, 8, layer);
            return;
        }

        AddPair(builder, 0, "LINE");
        AddPair(builder, 8, layer);
        AddPair(builder, 10, Invariant(x1));
        AddPair(builder, 20, Invariant(y1));
        AddPair(builder, 30, "0");
        AddPair(builder, 11, Invariant(x2));
        AddPair(builder, 21, Invariant(y2));
        AddPair(builder, 31, "0");
    }

    private static void AddDxfPolylineVertex(
        StringBuilder builder,
        double x,
        double y,
        string layer,
        double width)
    {
        AddPair(builder, 0, "VERTEX");
        AddPair(builder, 8, layer);
        AddPair(builder, 10, Invariant(x));
        AddPair(builder, 20, Invariant(y));
        AddPair(builder, 30, "0");
        AddPair(builder, 40, Invariant(width));
        AddPair(builder, 41, Invariant(width));
    }

    private static double DxfLineWidth(string layer)
    {
        if (layer == "REBAR_SCHEDULE")
        {
            return 0;
        }

        if (layer.Contains("LONGITUDINAL_REBAR", StringComparison.Ordinal) ||
            layer is "PEDESTAL_MAIN_REBAR" or "REBAR_BOTTOM_SECTION")
        {
            return 0.024;
        }

        if (layer.Contains("REBAR", StringComparison.Ordinal) ||
            layer.Contains("STIRRUP", StringComparison.Ordinal))
        {
            return 0.016;
        }

        return 0;
    }

    private static void AddCircle(
        StringBuilder builder,
        double centerX,
        double centerY,
        double radius,
        string layer)
    {
        AddPair(builder, 0, "CIRCLE");
        AddPair(builder, 8, layer);
        AddPair(builder, 10, Invariant(centerX));
        AddPair(builder, 20, Invariant(centerY));
        AddPair(builder, 30, "0");
        AddPair(builder, 40, Invariant(radius));
    }

    private static IReadOnlyList<double> SpacedCoordinates(
        double start,
        double end,
        double spacing)
    {
        if (end < start || spacing <= 0)
        {
            return [];
        }

        var count = (int)Math.Floor((end - start) / spacing) + 1;
        return Enumerable.Range(0, Math.Max(1, count))
            .Select(index => Math.Min(end, start + index * spacing))
            .Distinct()
            .ToList();
    }

    private static void AppendGeometrySummary(
        StringBuilder builder,
        FoundationScheme scheme)
    {
        var geometry = scheme.Geometry;
        if (scheme.FoundationType == FoundationType.RigidShortPile)
        {
            builder.AppendLine(
                $"- 刚性短柱桩－圆形：直径{geometry.PileDiameterM:F2} m，埋深{geometry.PileLengthM:F2} m，出地面{geometry.PedestalHeightM:F2} m");
            AppendTieBeamGeometrySummary(builder, geometry);
            return;
        }

        if (scheme.FoundationType == FoundationType.RigidRectangularShortPile)
        {
            builder.AppendLine(
                $"- 刚性短柱桩－矩形：截面{geometry.BaseLengthM:F2} m × {geometry.BaseWidthM:F2} m，埋深{geometry.PileLengthM:F2} m，出地面{geometry.PedestalHeightM:F2} m");
            AppendTieBeamGeometrySummary(builder, geometry);
            return;
        }

        if (scheme.FoundationType == FoundationType.Pile)
        {
            if (geometry.PileCount == 1)
            {
                builder.AppendLine(
                    $"- 单管塔独立灌注桩：1根，直径{geometry.PileDiameterM:F2} m，埋深{geometry.PileLengthM:F2} m，出地面{geometry.PedestalHeightM:F2} m；不设承台和连梁");
            }
            else
            {
                builder.AppendLine(
                    $"- 独立灌注桩：{geometry.PileCount}根，每根直径{geometry.PileDiameterM:F2} m、埋深{geometry.PileLengthM:F2} m；中心距{geometry.PileCenterSpacingM:F2} m");
                builder.AppendLine(
                    $"- 连梁：{geometry.TieBeamCount}根，宽{geometry.TieBeamWidthM:F2} m×高{geometry.TieBeamHeightM:F2} m；不设承台");
            }
            return;
        }
        else
        {
            builder.AppendLine(
                $"- 底板：{geometry.BaseLengthM:F2} m × {geometry.BaseWidthM:F2} m × {geometry.BaseThicknessM:F2} m");
        }

        if (scheme.FoundationType == FoundationType.CircularShortColumn)
        {
            builder.AppendLine(
                $"- 圆形柱：直径{geometry.PedestalLengthM:F2} m × 高{geometry.PedestalHeightM:F2} m");
        }
        else
        {
            builder.AppendLine(
                $"- 短柱：{geometry.PedestalLengthM:F2} m × {geometry.PedestalWidthM:F2} m × {geometry.PedestalHeightM:F2} m");
        }
        AppendTieBeamGeometrySummary(builder, geometry);
    }

    private static void AppendTieBeamGeometrySummary(
        StringBuilder builder,
        FoundationGeometry geometry)
    {
        if (geometry.TieBeamCount <= 0)
        {
            return;
        }

        builder.AppendLine(
            $"- 连系梁：{geometry.TieBeamCount}根闭合周边布置，轴线长{geometry.PileCenterSpacingM:F2} m，宽{geometry.TieBeamWidthM:F2} m×高{geometry.TieBeamHeightM:F2} m");
    }

    private static string BuildScopeStatement(FoundationScheme scheme)
    {
        var completed = scheme.VerificationChecks
            .Select(check => check.Name)
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToList();
        var pending = scheme.ScopeAndInputItems
            .Select(check => $"{check.Name}（{FormatCheckStatus(check.Status)}）")
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToList();
        var completedText = completed.Count == 0
            ? "尚未形成安全性验算"
            : $"已执行：{string.Join("、", completed)}";
        var pendingText = pending.Count == 0
            ? "无待补参数或专项复核项"
            : $"尚需处理：{string.Join("、", pending)}";
        return $"{completedText}。{pendingText}。计算结果项只记录数值，不单独作为安全通过结论。";
    }

    private static string BuildPackageStatus(FoundationScheme scheme) =>
        !scheme.IsFeasible
            ? "FAILED_VERIFICATION"
            : scheme.IsFormalVerificationComplete
                ? "FORMAL_VERIFICATION_COMPLETE"
                : scheme.HasPendingInputs
                    ? "REVIEW_DRAFT_PENDING_INPUTS"
                    : "REVIEW_DRAFT_SPECIAL_REVIEW";

    private static string BuildPackageDisclaimer(FoundationScheme scheme) =>
        scheme.IsFormalVerificationComplete
            ? "当前规则包内的确定性验算已完成；施工前仍应核对原始资料、设计条件、详图和适用规范。"
            : $"{scheme.VerificationConclusion}。已完成项目与待补参数/专项复核项目已在计算书中分栏列明，本成果为复核稿，不得标记为全部通过或直接作为施工依据。";

    private static string BuildExcavationScope(FoundationType type) =>
        type is
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile or
            FoundationType.Pile
            ? type == FoundationType.RigidRectangularShortPile
                ? "按矩形桩孔净体积计取，未计护壁、超挖和工作面"
                : "按圆形桩孔净体积计取，未计护壁、扩孔、超挖和工作面"
            : "按程序设置的基础几何及工作面估算";

    private static string BuildBackfillScope(FoundationType type) =>
        type is
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile or
            FoundationType.Pile
            ? "桩孔灌注不另计回填土"
            : "开挖量扣除混凝土体积";

    private static void AddText(
        StringBuilder builder,
        double x,
        double y,
        double height,
        string text)
    {
        AddTextOnLayer(builder, x, y, height, text, "ANNOTATION");
    }

    private static void AddTextOnLayer(
        StringBuilder builder,
        double x,
        double y,
        double height,
        string text,
        string layer = "ANNOTATION",
        double rotation = 0)
    {
        AddPair(builder, 0, "TEXT");
        AddPair(builder, 8, layer);
        AddPair(builder, 10, Invariant(x));
        AddPair(builder, 20, Invariant(y));
        AddPair(builder, 30, "0");
        AddPair(builder, 40, Invariant(height));
        AddPair(builder, 7, "_TCH_DIM");
        if (Math.Abs(rotation) > 1e-9)
        {
            AddPair(builder, 50, Invariant(rotation));
        }
        AddPair(builder, 1, DxfText(text, 180));
    }

    private static void AddCenteredText(
        StringBuilder builder,
        double x,
        double y,
        double height,
        string text,
        string layer,
        double rotation = 0)
    {
        AddPair(builder, 0, "TEXT");
        AddPair(builder, 8, layer);
        AddPair(builder, 10, Invariant(x));
        AddPair(builder, 20, Invariant(y));
        AddPair(builder, 30, "0");
        AddPair(builder, 11, Invariant(x));
        AddPair(builder, 21, Invariant(y));
        AddPair(builder, 31, "0");
        AddPair(builder, 40, Invariant(height));
        AddPair(builder, 7, "_TCH_DIM");
        AddPair(builder, 72, "1");
        AddPair(builder, 73, "2");
        if (Math.Abs(rotation) > 1e-9)
        {
            AddPair(builder, 50, Invariant(rotation));
        }
        AddPair(builder, 1, DxfText(text, 180));
    }

    private static void AddPair(StringBuilder builder, int code, string value)
    {
        builder.AppendLine(code.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(EscapeDxfAsciiValue(value));
    }

    private static string EscapeDxfAsciiValue(string value)
    {
        if (value.All(character => character is >= ' ' and <= '~'))
        {
            return value;
        }

        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is >= ' ' and <= '~')
            {
                escaped.Append(character);
            }
            else if (!char.IsSurrogate(character))
            {
                escaped.Append("\\U+");
                escaped.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                // R12's text escape is four hexadecimal digits. Supplementary
                // Unicode characters are not portable in this interchange format.
                escaped.Append('?');
            }
        }

        return escaped.ToString();
    }

    private static string CreateUniqueDirectory(string parentDirectory, string preferredName)
    {
        Directory.CreateDirectory(parentDirectory);
        var path = Path.Combine(parentDirectory, preferredName);
        var suffix = 1;
        while (Directory.Exists(path))
        {
            path = Path.Combine(parentDirectory, $"{preferredName}_{suffix++}");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static string MakeSafeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(character =>
            invalidCharacters.Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(safe) ? "未命名项目" : safe.Trim();
    }

    private static string BuildUncalculatedReinforcementScope(FoundationScheme scheme)
    {
        var baseScope = scheme.FoundationType switch
        {
            FoundationType.Pile =>
                "未计量：桩梁节点加密与锚固附加钢筋、吊筋、定位筋及搭接/机械连接增量；须结合桩身内力、裂缝、沉降、塔脚连接和完整荷载组合专项设计。本分支没有承台工程量。",
            FoundationType.Raft =>
                "未计量：筏板顶筋、塔脚柱配筋、柱下局部冲切附加筋、柱带/跨中带钢筋、锚栓及连接构造；须经塔架—筏板整体分析确认，软件不采用经验含钢量补齐。",
            FoundationType.RigidShortPile =>
                "未计量：锚栓及连接区附加钢筋、吊筋、定位筋、搭接/机械连接增量和特殊地基构造；须结合塔脚连接、裂缝、抗震及施工要求专项设计。",
            FoundationType.RigidRectangularShortPile =>
                "矩形箍筋135°弯钩量度差及两端弯后平直段已经计量。未计量：锚栓及连接区附加钢筋、吊筋、定位筋、纵筋搭接/机械连接增量和特殊地基构造；须结合塔脚连接、裂缝、抗震及施工要求专项设计。",
            _ =>
                "矩形短柱箍筋135°弯钩量度差及两端弯后平直段已经计量。未计量：纵筋锚固/接头、锚栓、基础侧面及其他构造钢筋；须结合塔脚连接、裂缝和构造要求专项设计。"
        };

        if (scheme.Geometry.TieBeamCount <= 0)
        {
            return baseScope;
        }

        var tieBeamReinforcementCalculated = scheme.ReinforcementDesigns.Any(item =>
            item.Component.Contains("连系梁纵筋", StringComparison.Ordinal)) &&
            scheme.ReinforcementDesigns.Any(item =>
                item.Component.Contains("连系梁箍筋", StringComparison.Ordinal));
        var tieBeamScope = tieBeamReinforcementCalculated
            ? $" {scheme.Geometry.TieBeamCount}根闭合周边连系梁纵筋和箍筋已按用户确认的整体分析内力计量；节点锚固、加密区及附加构造钢筋仍未计量。"
            : $" {scheme.Geometry.TieBeamCount}根闭合周边连系梁的混凝土已计量；因尚未确认塔架－基础整体分析内力，连系梁纵筋、箍筋和节点附加钢筋均未计量，软件未采用经验含钢量补齐。";
        return baseScope + tieBeamScope;
    }

    private static void WriteZipEntry(
        ZipArchive archive,
        string entryName,
        string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void AppendWordHeading(StringBuilder builder, string text) =>
        AppendWordParagraph(builder, text, "Heading1");

    private static void AppendWordHeading2(StringBuilder builder, string text) =>
        AppendWordParagraph(builder, text, "Heading2");

    private static void AppendWordEquation(StringBuilder builder, string text) =>
        AppendWordParagraph(builder, text, "Equation");

    private static void AppendWordBullet(StringBuilder builder, string text) =>
        AppendWordParagraph(builder, text, "Reference");

    private static void AppendWordKeyValue(
        StringBuilder builder,
        string key,
        string value) =>
        AppendWordParagraph(builder, $"{key}：{value}");

    private static void AppendWordParagraph(
        StringBuilder builder,
        string text,
        string? style = null)
    {
        builder.Append("<w:p>");
        if (!string.IsNullOrWhiteSpace(style))
        {
            builder.Append($"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>");
        }
        builder.Append("<w:r><w:t xml:space=\"preserve\">");
        builder.Append(XmlEscape(text));
        builder.Append("</w:t></w:r></w:p>");
    }

    private static void AppendWordTable(
        StringBuilder builder,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        IReadOnlyList<int>? columnWidthsDxa = null)
    {
        var widths = columnWidthsDxa?.ToArray() ??
                     BuildEqualColumnWidths(headers.Count);
        if (widths.Length != headers.Count || widths.Sum() != 9360)
        {
            throw new InvalidOperationException("Word表格列宽必须与列数一致且合计9360 DXA。");
        }

        builder.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/>" +
                       "<w:tblW w:w=\"9360\" w:type=\"dxa\"/>" +
                       "<w:tblInd w:w=\"120\" w:type=\"dxa\"/>" +
                       "<w:tblLayout w:type=\"fixed\"/>" +
                       "<w:tblCellMar><w:top w:w=\"80\" w:type=\"dxa\"/>" +
                       "<w:start w:w=\"120\" w:type=\"dxa\"/>" +
                       "<w:bottom w:w=\"80\" w:type=\"dxa\"/>" +
                       "<w:end w:w=\"120\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>" +
                       "<w:tblGrid>");
        foreach (var width in widths)
        {
            builder.Append($"<w:gridCol w:w=\"{width}\"/>");
        }
        builder.Append("</w:tblGrid>");
        AppendWordTableRow(builder, headers, widths, header: true);
        var hasRows = false;
        foreach (var row in rows)
        {
            AppendWordTableRow(builder, row, widths, header: false);
            hasRows = true;
        }
        if (!hasRows)
        {
            AppendWordTableRow(
                builder,
                ["本方案尚未形成可用的结构化结果。"],
                [9360],
                header: false,
                columnSpan: headers.Count);
        }
        builder.Append("</w:tbl><w:p/>");
    }

    private static void AppendWordTableRow(
        StringBuilder builder,
        IReadOnlyList<string> values,
        IReadOnlyList<int> widths,
        bool header,
        int columnSpan = 1)
    {
        builder.Append("<w:tr><w:trPr><w:cantSplit/>");
        if (header)
        {
            builder.Append("<w:tblHeader/>");
        }
        builder.Append("</w:trPr>");
        for (var index = 0; index < values.Count; index++)
        {
            var width = widths[Math.Min(index, widths.Count - 1)];
            builder.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/>");
            if (columnSpan > 1)
            {
                builder.Append($"<w:gridSpan w:val=\"{columnSpan}\"/>");
            }
            if (header)
            {
                builder.Append("<w:shd w:val=\"clear\" w:fill=\"E8EEF5\"/>");
            }
            builder.Append("<w:vAlign w:val=\"center\"/></w:tcPr><w:p><w:pPr>" +
                           "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"260\" w:lineRule=\"auto\"/>" +
                           "</w:pPr><w:r><w:rPr><w:sz w:val=\"18\"/>");
            if (header)
            {
                builder.Append("<w:b/>");
            }
            builder.Append("</w:rPr><w:t xml:space=\"preserve\">");
            builder.Append(XmlEscape(values[index]));
            builder.Append("</w:t></w:r></w:p></w:tc>");
        }
        builder.Append("</w:tr>");
    }

    private static int[] BuildEqualColumnWidths(int count)
    {
        var result = Enumerable.Repeat(9360 / count, count).ToArray();
        result[^1] += 9360 - result.Sum();
        return result;
    }

    private static string XmlEscape(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private static string Invariant(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>" +
        "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>" +
        "</Types>";

    private const string PackageRelationshipsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private const string DocumentRelationshipsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "<Relationship Id=\"rIdHeader\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
        "<Relationship Id=\"rIdFooter\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
        "</Relationships>";

    private const string WordStylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Microsoft YaHei\"/><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:pPr><w:spacing w:before=\"0\" w:after=\"120\" w:line=\"300\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Microsoft YaHei\"/><w:sz w:val=\"22\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:pPr><w:jc w:val=\"left\"/><w:spacing w:before=\"0\" w:after=\"160\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"0B2545\"/><w:sz w:val=\"48\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Subtitle\"><w:name w:val=\"Subtitle\"/><w:pPr><w:spacing w:before=\"0\" w:after=\"260\"/></w:pPr><w:rPr><w:color w:val=\"53657A\"/><w:sz w:val=\"24\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:keepNext/><w:spacing w:before=\"360\" w:after=\"200\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2E74B5\"/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:pPr><w:keepNext/><w:spacing w:before=\"260\" w:after=\"120\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"1F4E79\"/><w:sz w:val=\"26\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Reference\"><w:name w:val=\"Reference\"/><w:pPr><w:ind w:left=\"360\"/><w:spacing w:after=\"80\"/></w:pPr><w:rPr><w:color w:val=\"334155\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Equation\"><w:name w:val=\"Equation\"/><w:pPr><w:ind w:left=\"600\" w:right=\"240\"/><w:spacing w:before=\"40\" w:after=\"90\" w:line=\"300\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\" w:eastAsia=\"Microsoft YaHei\"/><w:color w:val=\"111827\"/><w:sz w:val=\"22\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Note\"><w:name w:val=\"Note\"/><w:pPr><w:ind w:left=\"240\" w:right=\"240\"/><w:spacing w:before=\"80\" w:after=\"120\"/></w:pPr><w:rPr><w:color w:val=\"475569\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Warning\"><w:name w:val=\"Warning\"/><w:pPr><w:spacing w:before=\"80\" w:after=\"160\" w:line=\"300\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"7A5A00\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"ResultPass\"><w:name w:val=\"ResultPass\"/><w:pPr><w:spacing w:before=\"80\" w:after=\"160\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"19734B\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"ResultFail\"><w:name w:val=\"ResultFail\"/><w:pPr><w:spacing w:before=\"80\" w:after=\"160\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"B42318\"/></w:rPr></w:style>" +
        "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"B7C3D0\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"B7C3D0\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"B7C3D0\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"B7C3D0\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"D8E0E8\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"D8E0E8\"/></w:tblBorders></w:tblPr></w:style>" +
        "</w:styles>";

    private const string WordHeaderXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:p><w:pPr><w:jc w:val=\"right\"/><w:spacing w:after=\"0\"/></w:pPr>" +
        "<w:r><w:rPr><w:color w:val=\"7A8794\"/><w:sz w:val=\"18\"/></w:rPr><w:t>塔基智设 | 基础计算书</w:t></w:r></w:p></w:hdr>";

    private const string WordFooterXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:p><w:pPr><w:jc w:val=\"center\"/><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr>" +
        "<w:r><w:rPr><w:color w:val=\"7A8794\"/><w:sz w:val=\"18\"/></w:rPr><w:t>第 </w:t></w:r>" +
        "<w:fldSimple w:instr=\"PAGE\"><w:r><w:rPr><w:color w:val=\"7A8794\"/><w:sz w:val=\"18\"/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
        "<w:r><w:rPr><w:color w:val=\"7A8794\"/><w:sz w:val=\"18\"/></w:rPr><w:t> 页</w:t></w:r></w:p></w:ftr>";

    private static string EscapeMarkdown(string value) => value.Replace("|", "\\|");

    private static string FormatProjectType(ProjectType type) => type switch
    {
        ProjectType.MonitoringPole => "监控杆基础",
        ProjectType.CommunicationTower => "通信塔桅基础",
        _ => "未选择"
    };

    private static string FormatFoundationType(FoundationType type) => type switch
    {
        FoundationType.RectangularShortColumn => "独立基础－矩形柱",
        FoundationType.CircularShortColumn => "独立基础－圆形柱",
        FoundationType.Raft => "中央塔柱筏板基础",
        FoundationType.RigidShortPile => "刚性短柱桩基础－圆形",
        FoundationType.RigidRectangularShortPile => "刚性短柱桩基础－矩形",
        FoundationType.Pile => "独立灌注桩基础（无承台）",
        _ => type.ToString()
    };

    private static string FormatFoundationType(FoundationScheme scheme)
    {
        if (scheme.Geometry.TieBeamCount <= 0)
        {
            return FormatFoundationType(scheme.FoundationType);
        }

        return scheme.FoundationType == FoundationType.Pile
            ? "独立灌注桩＋连系梁基础（无承台）"
            : $"{FormatFoundationType(scheme.FoundationType)}＋闭合周边连系梁";
    }

    private static string FormatCheckStatus(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "通过",
        CheckStatus.Fail => "不通过",
        CheckStatus.Warning => "需专项复核",
        CheckStatus.Result => "计算结果",
        CheckStatus.PendingInput => "待补参数",
        CheckStatus.SpecialReview => "已转专业核对",
        CheckStatus.Advisory => "施工提醒",
        _ => "未校核"
    };
}
