using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Agencies;
using AirStudio.CommercialManager.Core.Services.Library;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Core.Services.Tags;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AirStudio.CommercialManager.Core.Services.Reports
{
    /// <summary>
    /// Professional color scheme for PDF generation
    /// </summary>
    internal static class PdfColors
    {
        public static readonly Color Primary = Color.FromHex("#1E3A5F");
        public static readonly Color Accent = Color.FromHex("#3182CE");
        public static readonly Color DayHeader = Color.FromHex("#E8F4FD");
        public static readonly Color TableHeader = Color.FromHex("#2C5282");
        public static readonly Color RowAlt = Color.FromHex("#F7FAFC");
        public static readonly Color SpotBg = Color.FromHex("#EDF2F7");
        public static readonly Color SpotBorder = Color.FromHex("#3182CE");
        public static readonly Color SpotText = Color.FromHex("#4A5568");
        public static readonly Color DaySummaryBg = Color.FromHex("#E2E8F0");
        public static readonly Color DaySummaryText = Color.FromHex("#718096");
        public static readonly Color StatusActive = Color.FromHex("#38A169");
        public static readonly Color StatusPending = Color.FromHex("#D69E2E");
        public static readonly Color StatusExpired = Color.FromHex("#E53E3E");
        public static readonly Color CardBlue = Color.FromHex("#3182CE");
        public static readonly Color CardGreen = Color.FromHex("#38A169");
        public static readonly Color CardOrange = Color.FromHex("#DD6B20");
        public static readonly Color CardPurple = Color.FromHex("#805AD5");
    }

    /// <summary>
    /// Service for generating broadcast sheet PDF reports
    /// </summary>
    public class BroadcastSheetService
    {
        private readonly Channel _channel;
        private readonly CommercialService _commercialService;
        private readonly TagService _tagService;
        private readonly AgencyService _agencyService;

        static BroadcastSheetService()
        {
            // Configure QuestPDF license (Community license for non-commercial use)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public BroadcastSheetService(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _commercialService = new CommercialService(channel);
            _tagService = new TagService(channel);
            _agencyService = new AgencyService(channel);
        }

        /// <summary>
        /// Get broadcast sheet data for a date range
        /// </summary>
        public async Task<BroadcastSheetData> GetBroadcastSheetDataAsync(
            DateTime fromDate,
            DateTime toDate,
            BroadcastSheetOptions options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new BroadcastSheetOptions();

            var data = new BroadcastSheetData
            {
                ChannelName = _channel.Name,
                FromDate = fromDate.Date,
                ToDate = toDate.Date,
                GeneratedAt = DateTime.Now,
                GeneratedBy = Environment.UserName,
                Options = options
            };

            try
            {
                // Load scheduled commercials
                var schedules = await _commercialService.LoadScheduledCommercialsAsync(cancellationToken);

                // Filter by date range
                schedules = schedules
                    .Where(s => s.TxDate.Date >= fromDate.Date && s.TxDate.Date <= toDate.Date)
                    .OrderBy(s => s.TxDate)
                    .ThenBy(s => s.TxTime)
                    .ToList();

                // Load agencies for spot details
                var agencies = options.IncludeAgencyInfo
                    ? await _agencyService.LoadAgenciesAsync(cancellationToken)
                    : new List<Agency>();

                // Create agency lookup dictionary
                var agencyLookup = agencies.ToDictionary(a => a.Code, a => a.AgencyName);

                // Group by date
                var groupedByDate = schedules.GroupBy(s => s.TxDate.Date);

                var allSpots = new HashSet<string>();
                var allAgencies = new HashSet<string>();
                var totalDuration = TimeSpan.Zero;

                foreach (var dateGroup in groupedByDate)
                {
                    var day = new BroadcastSheetDay
                    {
                        Date = dateGroup.Key
                    };

                    foreach (var schedule in dateGroup)
                    {
                        var capsule = new BroadcastSheetCapsule
                        {
                            TxTime = schedule.TxTimeDisplay,
                            CapsuleName = schedule.CapsuleName,
                            Duration = schedule.Duration,
                            TxDate = schedule.TxDate,
                            ValidUntil = schedule.ToDate,
                            UserName = schedule.UserName,
                            MobileNo = schedule.MobileNo
                        };

                        // Parse TAG file for spot details
                        if (options.IncludeSpotDetails && !string.IsNullOrEmpty(schedule.TagFilePath))
                        {
                            try
                            {
                                var tagFile = _tagService.LoadTagFile(schedule.TagFilePath);
                                if (tagFile != null && tagFile.CommercialEntries.Count > 0)
                                {
                                    LogService.Info($"Loaded TAG file with {tagFile.CommercialEntries.Count} spots: {schedule.TagFilePath}");

                                    foreach (var entry in tagFile.CommercialEntries)
                                    {
                                        // Get agency name from lookup
                                        var agencyName = "--";
                                        if (entry.AgencyCode > 0 && agencyLookup.TryGetValue(entry.AgencyCode, out var foundAgency))
                                        {
                                            agencyName = foundAgency;
                                        }

                                        var spot = new BroadcastSheetSpot
                                        {
                                            SpotName = entry.SpotName,
                                            Duration = entry.DurationFormatted,
                                            Agency = agencyName,
                                            AgencyCode = entry.AgencyCode
                                        };

                                        capsule.Spots.Add(spot);
                                        allSpots.Add(entry.SpotName);

                                        if (!string.IsNullOrEmpty(agencyName) && agencyName != "--")
                                        {
                                            allAgencies.Add(agencyName);
                                        }
                                    }

                                    LogService.Info($"Added {capsule.Spots.Count} spots to capsule '{capsule.CapsuleName}'");
                                }
                                else
                                {
                                    LogService.Warning($"TAG file is null or empty for schedule: {schedule.TagFilePath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.Error($"Failed to parse TAG file for broadcast sheet: {schedule.TagFilePath}", ex);
                            }
                        }
                        else
                        {
                            if (options.IncludeSpotDetails)
                            {
                                LogService.Warning($"Schedule has no TAG file path: {schedule.CapsuleName} at {schedule.TxTime}");
                            }
                        }

                        // Parse duration
                        if (TimeSpan.TryParse(schedule.Duration, out var dur))
                        {
                            totalDuration += dur;
                        }

                        day.Capsules.Add(capsule);
                    }

                    data.Days.Add(day);
                }

                // Calculate agency breakdown
                var agencyBreakdown = new List<AgencyStats>();
                var agencySpotData = new Dictionary<string, (int count, double duration)>();

                foreach (var day in data.Days)
                {
                    foreach (var capsule in day.Capsules)
                    {
                        foreach (var spot in capsule.Spots)
                        {
                            var agencyName = !string.IsNullOrEmpty(spot.Agency) && spot.Agency != "--"
                                ? spot.Agency
                                : "Other";

                            if (!agencySpotData.ContainsKey(agencyName))
                            {
                                agencySpotData[agencyName] = (0, 0);
                            }

                            var current = agencySpotData[agencyName];
                            var spotDuration = TimeSpan.TryParse(spot.Duration, out var d) ? d.TotalSeconds : 0;
                            agencySpotData[agencyName] = (current.count + 1, current.duration + spotDuration);
                        }
                    }
                }

                foreach (var kvp in agencySpotData.OrderByDescending(x => x.Value.count))
                {
                    agencyBreakdown.Add(new AgencyStats
                    {
                        AgencyName = kvp.Key,
                        SpotCount = kvp.Value.count,
                        TotalDuration = TimeSpan.FromSeconds(kvp.Value.duration)
                    });
                }

                // Update summary
                data.Summary = new BroadcastSheetSummary
                {
                    TotalSchedules = schedules.Count,
                    TotalDuration = totalDuration,
                    UniqueCommercials = allSpots.Count,
                    UniqueAgencies = allAgencies.Count,
                    AgencyBreakdown = agencyBreakdown
                };

                LogService.Info($"Generated broadcast sheet data: {schedules.Count} schedules, {data.Days.Count} days, {agencyBreakdown.Count} agencies");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to generate broadcast sheet data", ex);
                throw;
            }

            return data;
        }

        /// <summary>
        /// Generate PDF document from broadcast sheet data
        /// </summary>
        public void GeneratePdf(BroadcastSheetData data, string outputPath)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));

            try
            {
                var document = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(40);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Header().Element(c => ComposeHeader(c, data));
                        page.Content().Element(c => ComposeContent(c, data));
                        page.Footer().Element(c => ComposeFooter(c, data));
                    });
                });

                document.GeneratePdf(outputPath);
                LogService.Info($"Generated broadcast sheet PDF: {outputPath}");
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to generate PDF: {outputPath}", ex);
                throw;
            }
        }

        /// <summary>
        /// Generate PDF and return as byte array
        /// </summary>
        public byte[] GeneratePdfBytes(BroadcastSheetData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComposeHeader(c, data));
                    page.Content().Element(c => ComposeContent(c, data));
                    page.Footer().Element(c => ComposeFooter(c, data));
                });
            });

            return document.GeneratePdf();
        }

        private void ComposeHeader(IContainer container, BroadcastSheetData data)
        {
            container.Column(column =>
            {
                // Title with accent bar
                column.Item().Row(row =>
                {
                    row.ConstantItem(4).Background(PdfColors.Accent);
                    row.RelativeItem().Background(PdfColors.DayHeader).Padding(12).Column(titleCol =>
                    {
                        titleCol.Item().AlignCenter().Text("COMMERCIAL BROADCAST SHEET")
                            .FontSize(20).Bold().FontColor(PdfColors.Primary);
                    });
                });

                column.Item().Height(12);

                // Info row
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(text =>
                        {
                            text.Span("Channel: ").FontSize(11);
                            text.Span(data.ChannelName).Bold().FontSize(11).FontColor(PdfColors.Accent);
                        });
                        col.Item().Text($"Period: {data.PeriodDisplay}").FontSize(10);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignRight().Text($"Generated: {data.GeneratedAt:dd-MMM-yyyy HH:mm}").FontSize(10);
                        col.Item().AlignRight().Text($"By: {data.GeneratedBy}").FontSize(10);
                    });
                });

                column.Item().Height(10);
                column.Item().BorderBottom(2).BorderColor(PdfColors.Accent);
            });
        }

        private void ComposeContent(IContainer container, BroadcastSheetData data)
        {
            container.PaddingVertical(10).Column(column =>
            {
                foreach (var day in data.Days)
                {
                    column.Item().Element(c => ComposeDaySection(c, day, data.Options));
                    column.Item().Element(c => ComposeDaySummary(c, day));
                    column.Item().Height(20);
                }

                // Summary section with dashboard cards
                column.Item().Element(c => ComposeSummary(c, data.Summary));

                // Agency breakdown table
                if (data.Summary.AgencyBreakdown.Count > 0)
                {
                    column.Item().Height(15);
                    column.Item().Element(c => ComposeAgencyBreakdown(c, data.Summary.AgencyBreakdown));
                }
            });
        }

        private void ComposeDaySection(IContainer container, BroadcastSheetDay day, BroadcastSheetOptions options)
        {
            container.Column(column =>
            {
                // Day header with accent bar
                column.Item().Row(row =>
                {
                    row.ConstantItem(4).Background(PdfColors.Accent);
                    row.RelativeItem().Background(PdfColors.DayHeader).Padding(10)
                        .Text(day.DayDisplay).Bold().FontSize(12).FontColor(PdfColors.Primary);
                });

                // Capsules table
                column.Item().Table(table =>
                {
                    // Define columns including STATUS
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(55);  // Time
                        columns.RelativeColumn(2);   // Capsule Name
                        columns.ConstantColumn(60);  // Duration
                        columns.ConstantColumn(70);  // Valid Until
                        columns.ConstantColumn(55);  // Status
                        if (options.IncludeUserInfo)
                        {
                            columns.RelativeColumn(1);   // User
                        }
                    });

                    // Header row with professional styling
                    table.Header(header =>
                    {
                        header.Cell().Background(PdfColors.TableHeader).Padding(6)
                            .Text("TIME").Bold().FontSize(9).FontColor(Colors.White);
                        header.Cell().Background(PdfColors.TableHeader).Padding(6)
                            .Text("CAPSULE").Bold().FontSize(9).FontColor(Colors.White);
                        header.Cell().Background(PdfColors.TableHeader).Padding(6)
                            .Text("DURATION").Bold().FontSize(9).FontColor(Colors.White);
                        header.Cell().Background(PdfColors.TableHeader).Padding(6)
                            .Text("VALID").Bold().FontSize(9).FontColor(Colors.White);
                        header.Cell().Background(PdfColors.TableHeader).Padding(6)
                            .Text("STATUS").Bold().FontSize(9).FontColor(Colors.White);
                        if (options.IncludeUserInfo)
                        {
                            header.Cell().Background(PdfColors.TableHeader).Padding(6)
                                .Text("USER").Bold().FontSize(9).FontColor(Colors.White);
                        }
                    });

                    // Data rows with alternation
                    var rowIndex = 0;
                    var colCount = options.IncludeUserInfo ? 6 : 5;

                    foreach (var capsule in day.Capsules)
                    {
                        var isEvenRow = rowIndex % 2 == 0;
                        var rowBg = isEvenRow ? Colors.White : PdfColors.RowAlt;

                        // Get status color
                        var statusColor = capsule.Status switch
                        {
                            "Active" => PdfColors.StatusActive,
                            "Pending" => PdfColors.StatusPending,
                            "Expired" => PdfColors.StatusExpired,
                            _ => Colors.Grey.Medium
                        };

                        // Time
                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(6).AlignMiddle().Text(capsule.TxTime).FontSize(10);

                        // Capsule Name
                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(6).AlignMiddle().Text(capsule.CapsuleName).FontSize(10);

                        // Duration
                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(6).AlignMiddle().Text(capsule.Duration).FontSize(10);

                        // Valid Until
                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(6).AlignMiddle().Text(capsule.ValidUntil.ToString("dd-MMM")).FontSize(10);

                        // Status badge
                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4).AlignMiddle().AlignCenter()
                            .Element(cell =>
                            {
                                cell.Background(statusColor).Padding(3).AlignCenter()
                                    .Text(capsule.Status).FontSize(8).Bold().FontColor(Colors.White);
                            });

                        // User
                        if (options.IncludeUserInfo)
                        {
                            table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                .Padding(6).AlignMiddle().Text(capsule.UserName ?? "--").FontSize(9);
                        }

                        // Spot details with accent box
                        if (options.IncludeSpotDetails && capsule.Spots.Count > 0)
                        {
                            table.Cell().ColumnSpan((uint)colCount).PaddingVertical(4).PaddingLeft(20).PaddingRight(10)
                                .Element(c => ComposeSpotDetails(c, capsule.Spots, options));
                        }

                        rowIndex++;
                    }
                });
            });
        }

        private void ComposeSpotDetails(IContainer container, List<BroadcastSheetSpot> spots, BroadcastSheetOptions options)
        {
            container.Row(row =>
            {
                // Left accent bar
                row.ConstantItem(3).Background(PdfColors.SpotBorder);

                // Spot content box
                row.RelativeItem().Background(PdfColors.SpotBg).Padding(8).Column(col =>
                {
                    foreach (var spot in spots)
                    {
                        var agencyText = options.IncludeAgencyInfo && !string.IsNullOrEmpty(spot.Agency) && spot.Agency != "--"
                            ? $" ({spot.Agency})"
                            : "";

                        col.Item().Text(text =>
                        {
                            text.Span("\u2022 ").FontSize(9).FontColor(PdfColors.Accent);
                            text.Span($"{spot.SpotName}").FontSize(9).FontColor(PdfColors.SpotText);
                            text.Span($" [{spot.Duration}]").FontSize(9).FontColor(Colors.Grey.Medium);
                            text.Span(agencyText).FontSize(9).FontColor(PdfColors.Accent);
                        });
                    }
                });
            });
        }

        private void ComposeDaySummary(IContainer container, BroadcastSheetDay day)
        {
            container.Background(PdfColors.DaySummaryBg).Padding(8).AlignCenter()
                .Text(text =>
                {
                    text.Span("Day Total: ").FontSize(9).FontColor(PdfColors.DaySummaryText);
                    text.Span($"{day.TotalCapsules} capsule{(day.TotalCapsules != 1 ? "s" : "")}").FontSize(9).Bold().FontColor(PdfColors.DaySummaryText);
                    text.Span(" | ").FontSize(9).FontColor(PdfColors.DaySummaryText);
                    text.Span(day.TotalDurationDisplay).FontSize(9).Bold().FontColor(PdfColors.DaySummaryText);
                });
        }

        private void ComposeSummary(IContainer container, BroadcastSheetSummary summary)
        {
            container.Column(column =>
            {
                // Section title
                column.Item().PaddingBottom(10).Text("BROADCAST SUMMARY")
                    .Bold().FontSize(14).FontColor(PdfColors.Primary);

                // Dashboard cards row
                column.Item().Row(row =>
                {
                    // Schedules card
                    row.RelativeItem().Padding(4).Element(c => ComposeDashboardCard(c,
                        summary.TotalSchedules.ToString(),
                        "SCHEDULES",
                        PdfColors.CardBlue));

                    // Duration card
                    row.RelativeItem().Padding(4).Element(c => ComposeDashboardCard(c,
                        summary.TotalDurationDisplay,
                        "DURATION",
                        PdfColors.CardGreen));

                    // Spots card
                    row.RelativeItem().Padding(4).Element(c => ComposeDashboardCard(c,
                        summary.UniqueCommercials.ToString(),
                        "UNIQUE SPOTS",
                        PdfColors.CardOrange));

                    // Agencies card
                    row.RelativeItem().Padding(4).Element(c => ComposeDashboardCard(c,
                        summary.UniqueAgencies.ToString(),
                        "AGENCIES",
                        PdfColors.CardPurple));
                });
            });
        }

        private void ComposeDashboardCard(IContainer container, string value, string label, Color bgColor)
        {
            container.Background(bgColor).Padding(12).Column(col =>
            {
                col.Item().AlignCenter().Text(value)
                    .FontSize(22).Bold().FontColor(Colors.White);
                col.Item().AlignCenter().Text(label)
                    .FontSize(9).FontColor(Colors.White);
            });
        }

        private void ComposeAgencyBreakdown(IContainer container, List<AgencyStats> agencyStats)
        {
            container.Column(column =>
            {
                // Section title
                column.Item().PaddingBottom(8).Text("Agency Distribution")
                    .Bold().FontSize(12).FontColor(PdfColors.Primary);

                // Agency table
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);   // Agency Name
                        columns.RelativeColumn(1);   // Spots
                        columns.RelativeColumn(1);   // Duration
                    });

                    // Header
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                            .Text("AGENCY").Bold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight()
                            .Text("SPOTS").Bold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight()
                            .Text("DURATION").Bold().FontSize(9);
                    });

                    // Data rows with alternation
                    var rowIndex = 0;
                    foreach (var agency in agencyStats)
                    {
                        var rowBg = rowIndex % 2 == 0 ? Colors.White : PdfColors.RowAlt;

                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(5).Text(agency.AgencyName).FontSize(10);
                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(5).AlignRight().Text(agency.SpotCount.ToString()).FontSize(10);
                        table.Cell().Background(rowBg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(5).AlignRight().Text(agency.DurationDisplay).FontSize(10);

                        rowIndex++;
                    }
                });
            });
        }

        private void ComposeFooter(IContainer container, BroadcastSheetData data)
        {
            container.Column(column =>
            {
                column.Item().BorderTop(1).BorderColor(Colors.Grey.Medium);
                column.Item().Height(5);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("AirStudio Commercial Manager").FontSize(8).FontColor(Colors.Grey.Medium);
                    row.RelativeItem().AlignCenter().Text(text =>
                    {
                        text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                        text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                        text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                        text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                    row.RelativeItem().AlignRight().Text("Confidential").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }
    }
}
