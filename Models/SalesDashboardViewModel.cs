using Loom.Services.Analytics;

namespace Loom.Models;

public sealed record SalesDashboardViewModel(
    SalesReport Report,
    SalesInsights? Insights,
    string? InsightsError);
