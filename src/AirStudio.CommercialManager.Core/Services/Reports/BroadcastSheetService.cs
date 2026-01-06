using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirStudio.CommercialManager.Core.Models;
using AirStudio.CommercialManager.Core.Services.Library;
using AirStudio.CommercialManager.Core.Services.Logging;
using AirStudio.CommercialManager.Core.Services.Tags;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AirStudio.CommercialManager.Core.Services.Reports
{
    /// <summary>
    /// Service for generating broadcast sheet PDF reports
    /// </summary>
    public class BroadcastSheetService
    {
        private readonly Channel _channel;
        private readonly CommercialService _commercialService;
        private readonly TagService _tagService;

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

                // Load commercials for spot details
                var commercials = options.IncludeSpotDetails
                    ? await _commercialService.LoadCommercialsAsync(cancellationToken)
                    : new List<Commercial>();

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
                                if (tagFile != null)
                                {
                                    foreach (var entry in tagFile.CommercialEntries)
                                    {
                                        var commercial = commercials.FirstOrDefault(c =>
                                            string.Equals(c.Spot, entry.SpotName, StringComparison.OrdinalIgnoreCase));

                                        var spot = new BroadcastSheetSpot
                                        {
                                            SpotName = entry.SpotName,
                                            Duration = entry.DurationFormatted,
                                            Agency = commercial?.Agency ?? "--",
                                            AgencyCode = commercial?.Code ?? 0
                                        };

                                        capsule.Spots.Add(spot);
                                        allSpots.Add(entry.SpotName);

                                        if (!string.IsNullOrEmpty(commercial?.Agency))
                                        {
                                            allAgencies.Add(commercial.Agency);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.Warning($"Failed to parse TAG file for broadcast sheet: {ex.Message}");
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

                // Update summary
                data.Summary = new BroadcastSheetSummary
                {
                    TotalSchedules = schedules.Count,
                    TotalDuration = totalDuration,
                    UniqueCommercials = allSpots.Count,
                    UniqueAgencies = allAgencies.Count
                };

                LogService.Info($"Generated broadcast sheet data: {schedules.Count} schedules, {data.Days.Count} days");
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
                // Title
                column.Item().AlignCenter().Text("COMMERCIAL BROADCAST SHEET")
                    .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

                column.Item().Height(10);

                // Info row
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text($"Channel: {data.ChannelName}").Bold();
                        col.Item().Text($"Period: {data.PeriodDisplay}");
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignRight().Text($"Generated: {data.GeneratedAt:dd-MMM-yyyy HH:mm}");
                        col.Item().AlignRight().Text($"By: {data.GeneratedBy}");
                    });
                });

                column.Item().Height(10);
                column.Item().BorderBottom(1).BorderColor(Colors.Grey.Medium);
            });
        }

        private void ComposeContent(IContainer container, BroadcastSheetData data)
        {
            container.PaddingVertical(10).Column(column =>
            {
                foreach (var day in data.Days)
                {
                    column.Item().Element(c => ComposeDaySection(c, day, data.Options));
                    column.Item().Height(15);
                }

                // Summary section
                column.Item().Element(c => ComposeSummary(c, data.Summary));
            });
        }

        private void ComposeDaySection(IContainer container, BroadcastSheetDay day, BroadcastSheetOptions options)
        {
            container.Column(column =>
            {
                // Day header
                column.Item().Background(Colors.Blue.Lighten4).Padding(8)
                    .Text(day.DayDisplay).Bold().FontSize(11);

                // Capsules table
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(60);  // Time
                        columns.RelativeColumn(2);   // Capsule Name
                        columns.ConstantColumn(60);  // Duration
                        columns.ConstantColumn(80);  // Valid Until
                        if (options.IncludeUserInfo)
                        {
                            columns.RelativeColumn(1);   // User
                        }
                    });

                    // Header row
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                            .Text("TIME").Bold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                            .Text("CAPSULE").Bold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                            .Text("DURATION").Bold().FontSize(9);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                            .Text("VALID UNTIL").Bold().FontSize(9);
                        if (options.IncludeUserInfo)
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                .Text("USER").Bold().FontSize(9);
                        }
                    });

                    // Data rows
                    foreach (var capsule in day.Capsules)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(capsule.TxTime);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(capsule.CapsuleName);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(capsule.Duration);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(capsule.ValidUntil.ToString("dd-MMM-yy"));
                        if (options.IncludeUserInfo)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                .Text(capsule.UserName ?? "--");
                        }

                        // Spot details
                        if (options.IncludeSpotDetails && capsule.Spots.Count > 0)
                        {
                            var colSpan = options.IncludeUserInfo ? 5 : 4;
                            table.Cell().ColumnSpan((uint)colSpan).Padding(4).PaddingLeft(20)
                                .Column(spotCol =>
                                {
                                    foreach (var spot in capsule.Spots)
                                    {
                                        var agencyText = options.IncludeAgencyInfo && !string.IsNullOrEmpty(spot.Agency)
                                            ? $" ({spot.Agency})"
                                            : "";
                                        spotCol.Item().Text($"- {spot.SpotName} [{spot.Duration}]{agencyText}")
                                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                                    }
                                });
                        }
                    }
                });
            });
        }

        private void ComposeSummary(IContainer container, BroadcastSheetSummary summary)
        {
            container.Background(Colors.Grey.Lighten4).Padding(10).Column(column =>
            {
                column.Item().Text("SUMMARY").Bold().FontSize(11);
                column.Item().Height(5);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Total Schedules: {summary.TotalSchedules}");
                    row.RelativeItem().Text($"Total Duration: {summary.TotalDurationDisplay}");
                    row.RelativeItem().Text($"Unique Spots: {summary.UniqueCommercials}");
                    row.RelativeItem().Text($"Agencies: {summary.UniqueAgencies}");
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
