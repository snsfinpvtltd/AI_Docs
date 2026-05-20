using ClosedXML.Excel;

const int TOTAL_ROWS = 100_000;
const int SEED       = 42;

var rng = new Random(SEED);

// ── Reference data ────────────────────────────────────────────────────────────

string[] trialIds    = ["TRIAL-001", "TRIAL-002", "TRIAL-003", "TRIAL-004", "TRIAL-005"];
string[] sites       = Enumerable.Range(1, 15).Select(i => $"SITE-{i:D2}").ToArray();
string[] genders     = ["Male", "Female"];
string[] treatments  = ["Treatment-A", "Treatment-B", "Treatment-C", "Placebo"];
string[] outcomes    = ["Improved", "Stable", "Worsened", "Withdrawn", "Lost to Follow-Up"];
string[] aeGrades    = ["None", "Mild", "Moderate", "Severe", "Life-Threatening"];
string[] phases      = ["Phase I", "Phase II", "Phase III"];
string[] ethnicities = ["Asian", "Black or African American", "Hispanic or Latino", "White", "Other", "Not Reported"];
string[] completions = ["Completed", "Ongoing", "Discontinued", "Screen Failure"];
string[] countries   = ["India", "USA", "UK", "Germany", "Australia", "Canada", "Japan", "Brazil"];

var visitStart = new DateTime(2022, 1, 1);
var visitEnd   = new DateTime(2025, 3, 31);
int dateRange  = (visitEnd - visitStart).Days;

// ── Workbook ──────────────────────────────────────────────────────────────────

Console.WriteLine($"Generating {TOTAL_ROWS:N0} synthetic clinical trial records…");
var sw = System.Diagnostics.Stopwatch.StartNew();

using var wb = new XLWorkbook();
var ws = wb.AddWorksheet("PatientData");

// ── Headers ───────────────────────────────────────────────────────────────────

string[] headers =
[
    "PatientID", "SubjectID", "TrialID", "TrialPhase", "SiteID", "Country",
    "Age", "Gender", "Ethnicity", "WeightKg", "HeightCm", "BMI",
    "TreatmentGroup", "Dosage_mg", "VisitDate", "VisitNumber",
    "BloodPressure_Systolic", "BloodPressure_Diastolic", "HeartRate_bpm",
    "Temperature_C", "HbA1c_pct", "CholesterolTotal_mgdL", "Creatinine_mgdL",
    "AdverseEvent", "AE_Grade", "SAE_Flag",
    "Outcome", "CompletionStatus", "ProtocolDeviation", "ConsentDate", "Notes"
];

for (int c = 0; c < headers.Length; c++)
{
    var cell = ws.Cell(1, c + 1);
    cell.Value = headers[c];
    cell.Style.Font.Bold = true;
    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 114, 255);
    cell.Style.Font.FontColor       = XLColor.White;
    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
}

// ── Rows ──────────────────────────────────────────────────────────────────────

for (int i = 0; i < TOTAL_ROWS; i++)
{
    int row = i + 2;

    // Demographics
    int age         = rng.Next(18, 86);
    string gender   = genders[rng.Next(genders.Length)];
    double height   = Math.Round(150 + rng.NextDouble() * 50, 1);           // 150-200 cm
    double weight   = Math.Round(45 + rng.NextDouble() * 80, 1);            // 45-125 kg
    double bmi      = Math.Round(weight / Math.Pow(height / 100.0, 2), 1);

    // Trial
    string trialId    = trialIds[i % trialIds.Length];
    string trialPhase = phases[rng.Next(phases.Length)];
    string site       = sites[rng.Next(sites.Length)];
    string country    = countries[rng.Next(countries.Length)];
    string treatment  = treatments[i % treatments.Length];

    // Dosage (placebo = 0)
    int dosage = treatment == "Placebo" ? 0 : new[] { 50, 100, 150, 200, 300 }[rng.Next(5)];

    // Visit
    var visitDate   = visitStart.AddDays(rng.Next(dateRange));
    int visitNumber = rng.Next(1, 9);
    var consentDate = visitDate.AddDays(-rng.Next(1, 30));

    // Vitals
    int bpSys  = rng.Next(90,  181);
    int bpDia  = rng.Next(55,  115);
    int hr     = rng.Next(48,  112);
    double temp = Math.Round(36.0 + rng.NextDouble() * 2.5, 1);

    // Labs
    double hba1c  = Math.Round(4.5 + rng.NextDouble() * 8.0,  1);
    int    chol   = rng.Next(140, 320);
    double creat  = Math.Round(0.5 + rng.NextDouble() * 3.5,  2);

    // AE
    string aeGrade = aeGrades[rng.Next(aeGrades.Length)];
    bool   sae     = aeGrade is "Severe" or "Life-Threatening" && rng.NextDouble() < 0.4;

    // Outcome
    string outcome    = outcomes[rng.Next(outcomes.Length)];
    string completion = completions[rng.Next(completions.Length)];
    bool   protDev    = rng.NextDouble() < 0.06;

    // ── Write cells ───────────────────────────────────────────────────────────

    int c = 1;
    ws.Cell(row, c++).Value = $"P-{i + 1:D6}";
    ws.Cell(row, c++).Value = $"SUB-{rng.Next(10000, 99999)}";
    ws.Cell(row, c++).Value = trialId;
    ws.Cell(row, c++).Value = trialPhase;
    ws.Cell(row, c++).Value = site;
    ws.Cell(row, c++).Value = country;
    ws.Cell(row, c++).Value = age;
    ws.Cell(row, c++).Value = gender;
    ws.Cell(row, c++).Value = ethnicities[rng.Next(ethnicities.Length)];
    ws.Cell(row, c++).Value = weight;
    ws.Cell(row, c++).Value = height;
    ws.Cell(row, c++).Value = bmi;
    ws.Cell(row, c++).Value = treatment;
    ws.Cell(row, c++).Value = dosage;

    var dateCell = ws.Cell(row, c++);
    dateCell.Value                     = visitDate;
    dateCell.Style.DateFormat.Format   = "yyyy-MM-dd";

    ws.Cell(row, c++).Value = visitNumber;
    ws.Cell(row, c++).Value = bpSys;
    ws.Cell(row, c++).Value = bpDia;
    ws.Cell(row, c++).Value = hr;
    ws.Cell(row, c++).Value = temp;
    ws.Cell(row, c++).Value = hba1c;
    ws.Cell(row, c++).Value = chol;
    ws.Cell(row, c++).Value = creat;
    ws.Cell(row, c++).Value = aeGrade != "None" ? "Yes" : "No";
    ws.Cell(row, c++).Value = aeGrade;
    ws.Cell(row, c++).Value = sae ? "Yes" : "No";
    ws.Cell(row, c++).Value = outcome;
    ws.Cell(row, c++).Value = completion;
    ws.Cell(row, c++).Value = protDev ? "Yes" : "No";

    var consentCell = ws.Cell(row, c++);
    consentCell.Value                    = consentDate;
    consentCell.Style.DateFormat.Format  = "yyyy-MM-dd";

    ws.Cell(row, c).Value = rng.NextDouble() < 0.1 ? "Requires follow-up" : "";

    if (i % 10_000 == 0 && i > 0)
        Console.Write($"  {i:N0} rows…\r");
}

// ── Set column widths (skip AdjustToContents — slow on 100k rows) ─────────────

int[] widths = [12, 12, 11, 10, 9, 12, 5, 8, 25, 10, 10, 7, 14, 10, 12, 12, 22, 22, 14, 13, 10, 22, 16, 14, 10, 9, 12, 17, 18, 12, 20];
for (int c = 0; c < widths.Length && c < headers.Length; c++)
    ws.Column(c + 1).Width = widths[c];

// Freeze header row
ws.SheetView.FreezeRows(1);

// ── Save ──────────────────────────────────────────────────────────────────────

string outDir  = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string outFile = Path.Combine(outDir, "tests", "Fixtures", "sample_100k_patients.xlsx");
Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

Console.WriteLine($"\nSaving to: {outFile}");
wb.SaveAs(outFile);

sw.Stop();
var fi = new FileInfo(outFile);
Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F1}s  |  {TOTAL_ROWS:N0} rows  |  {fi.Length / 1024.0 / 1024.0:F1} MB");
