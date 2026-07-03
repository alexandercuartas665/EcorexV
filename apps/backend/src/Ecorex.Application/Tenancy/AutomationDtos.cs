using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

public sealed record AutomationRuleDto(
    Guid Id,
    string Name,
    AutomationTrigger Trigger,
    int ThresholdMinutes,
    Guid? StageId,
    string? TimeWindowStart,
    string? TimeWindowEnd,
    AutomationAction Action,
    string? FollowUpTitle,
    string? TemplateCategory,
    string? ShiftName,
    bool IsActive,
    int ExecutionCount,
    DateTimeOffset? LastRunAt);

public sealed record SaveAutomationRuleRequest(
    string Name,
    AutomationTrigger Trigger,
    int ThresholdMinutes,
    Guid? StageId,
    string? TimeWindowStart,
    string? TimeWindowEnd,
    AutomationAction Action,
    string? FollowUpTitle,
    string? TemplateCategory,
    string? ShiftName);

public sealed record AutomationRunResult(int RulesEvaluated, int FollowUpsCreated);
