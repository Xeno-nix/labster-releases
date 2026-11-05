namespace CLMS.Application.DTOs.Booking;

/// <summary>
/// The validated and classified rows of an import, plus roll-up counts for the summary strip.
/// </summary>
public class ClassListImportPreview
{
    public IReadOnlyList<ClassListImportRow> Rows { get; set; } = [];

    public int ToEnroll => Rows.Count(r => r.Status == ClassListImportStatus.WillEnroll);

    public int ToCreate => Rows.Count(r => r.Status == ClassListImportStatus.WillCreateStudent);

    public int AlreadyEnrolled => Rows.Count(r => r.Status == ClassListImportStatus.AlreadyEnrolled);

    public int Invalid => Rows.Count(r => r.Status == ClassListImportStatus.Invalid);

    /// <summary>Rows that would actually be enrolled (WillEnroll + WillCreateStudent).</summary>
    public int Importable => ToEnroll + ToCreate;
}