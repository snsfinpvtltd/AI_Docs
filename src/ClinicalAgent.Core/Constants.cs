namespace ClinicalAgent.Core;

public static class BlobContainerNames
{
    public const string Uploads   = "uploads";
    public const string Templates = "clinical-templates";
    public const string Generated = "generated-docs";
}

public static class TemplateTypes
{
    public const string PatientSummary = "patient-summary";
    public const string OutcomeData    = "outcome-data";
    public const string FullReport     = "full-report";
}
