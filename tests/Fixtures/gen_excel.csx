using ClosedXML.Excel;

var wb = new XLWorkbook();
var ws = wb.Worksheets.Add("PatientData");

// Headers
var headers = new[] { "PatientID","Age","Gender","TreatmentGroup","Dose_mg","BaselineScore","OutcomeScore","AdverseEvent","EnrollmentDate","Status" };
for (int c = 1; c <= headers.Length; c++)
{
    ws.Cell(1, c).Value = headers[c-1];
    ws.Cell(1, c).Style.Font.Bold = true;
    ws.Cell(1, c).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.DarkBlue;
    ws.Cell(1, c).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
}

// Sample patient data (mix of ages above and below 40)
var rows = new object[,]
{
    { "P001", 25, "Female", "Drug A", 50, 72, 58, "None",    "2024-01-15", "Completed" },
    { "P002", 45, "Male",   "Placebo", 0, 68, 65, "None",    "2024-01-16", "Completed" },
    { "P003", 31, "Female", "Drug B", 100, 80, 51, "Nausea", "2024-01-17", "Completed" },
    { "P004", 52, "Male",   "Drug A", 50, 74, 60, "None",    "2024-01-18", "Completed" },
    { "P005", 28, "Female", "Drug B", 100, 77, 49, "Headache","2024-01-19","Completed" },
    { "P006", 19, "Male",   "Placebo", 0, 65, 63, "None",    "2024-01-20", "Completed" },
    { "P007", 40, "Female", "Drug A", 50, 71, 55, "Fatigue", "2024-01-21", "Active"    },
    { "P008", 33, "Male",   "Drug B", 100, 83, 47, "Nausea", "2024-01-22", "Completed" },
    { "P009", 61, "Female", "Placebo", 0, 69, 67, "None",    "2024-01-23", "Completed" },
    { "P010", 22, "Male",   "Drug A", 50, 75, 59, "None",    "2024-01-24", "Completed" },
    { "P011", 37, "Female", "Drug B", 100, 78, 50, "Headache","2024-01-25","Completed" },
    { "P012", 48, "Male",   "Placebo", 0, 66, 64, "None",    "2024-01-26", "Active"    },
    { "P013", 29, "Female", "Drug A", 50, 73, 57, "None",    "2024-01-27", "Completed" },
    { "P014", 55, "Male",   "Drug B", 100, 81, 52, "Nausea", "2024-01-28", "Completed" },
    { "P015", 34, "Female", "Placebo", 0, 67, 66, "None",    "2024-01-29", "Completed" },
};

for (int r = 0; r < rows.GetLength(0); r++)
    for (int c = 0; c < rows.GetLength(1); c++)
        ws.Cell(r + 2, c + 1).Value = rows[r, c]?.ToString();

ws.Columns().AdjustToContents();
wb.SaveAs("D:/Projects/2026-2027/AI_Docs/tests/Fixtures/sample_trial.xlsx");
Console.WriteLine("Excel created: tests/Fixtures/sample_trial.xlsx");
