namespace OT.Assessment.Infrastructure.Persistence;

/// <summary>Database settings (section <c>Database</c>). The connection string itself lives in <c>ConnectionStrings:OtAssessmentDb</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string ConnectionStringName = "OtAssessmentDb";

    public int CommandTimeoutSeconds { get; set; } = 30;
}
