using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed class ProjectReadinessService
{
    public ProjectStage Evaluate(ProjectModel project)
    {
        if (project.ProjectType == ProjectType.NotSelected ||
            string.IsNullOrWhiteSpace(project.Name))
        {
            return ProjectStage.Created;
        }

        if (project.ProjectType == ProjectType.MonitoringPole &&
            (string.IsNullOrWhiteSpace(project.Province) ||
             string.IsNullOrWhiteSpace(project.City)))
        {
            return ProjectStage.Created;
        }

        if (!project.Geotechnical.IsConfirmed)
        {
            return ProjectStage.SiteReady;
        }

        if (project.FoundationLoad.VerticalKn == 0 &&
            project.FoundationLoad.ShearXKn == 0 &&
            project.FoundationLoad.ShearYKn == 0 &&
            project.FoundationLoad.MomentXKnM == 0 &&
            project.FoundationLoad.MomentYKnM == 0 &&
            project.FoundationLoad.TorsionKnM == 0)
        {
            return ProjectStage.GeotechnicalReady;
        }

        if (project.Schemes.Count == 0)
        {
            return ProjectStage.LoadReady;
        }

        if (project.SelectedSchemeId is null)
        {
            return ProjectStage.CandidateReady;
        }

        var selected = project.Schemes.SingleOrDefault(
            scheme => scheme.Id == project.SelectedSchemeId);
        return selected?.IsFeasible == true
            ? ProjectStage.Verified
            : ProjectStage.SchemeSelected;
    }
}
