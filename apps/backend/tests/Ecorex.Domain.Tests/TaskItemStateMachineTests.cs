using Ecorex.Domain.Enums;
using Ecorex.Domain.Rules;

namespace Ecorex.Domain.Tests;

/// <summary>
/// Maquina de estados del nucleo TaskItem (ADR-0013): transiciones validas e invalidas,
/// y Closed como estado terminal inmutable.
/// </summary>
public class TaskItemStateMachineTests
{
    [Theory]
    [InlineData(TaskItemStatus.Pending, TaskItemStatus.Active)]
    [InlineData(TaskItemStatus.Pending, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Pending, TaskItemStatus.Suspended)]
    [InlineData(TaskItemStatus.Active, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Active, TaskItemStatus.Suspended)]
    [InlineData(TaskItemStatus.Active, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Suspended)]
    [InlineData(TaskItemStatus.Suspended, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Suspended, TaskItemStatus.Active)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Closed)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.InProgress)] // reabrir
    public void ValidTransitions_AreAllowed(TaskItemStatus from, TaskItemStatus to)
    {
        Assert.True(TaskItemStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(TaskItemStatus.Pending, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Pending, TaskItemStatus.Closed)]
    [InlineData(TaskItemStatus.Active, TaskItemStatus.Pending)]
    [InlineData(TaskItemStatus.Active, TaskItemStatus.Closed)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Pending)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Active)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Closed)]
    [InlineData(TaskItemStatus.Suspended, TaskItemStatus.Pending)]
    [InlineData(TaskItemStatus.Suspended, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Suspended, TaskItemStatus.Closed)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Pending)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Active)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Suspended)]
    public void InvalidTransitions_AreRejected(TaskItemStatus from, TaskItemStatus to)
    {
        Assert.False(TaskItemStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(TaskItemStatus.Pending)]
    [InlineData(TaskItemStatus.Active)]
    [InlineData(TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Suspended)]
    [InlineData(TaskItemStatus.Closed)]
    public void Closed_IsUnreachableExceptFromDone_AndTerminal(TaskItemStatus from)
    {
        // Solo Done -> Closed es valido.
        Assert.Equal(from == TaskItemStatus.Done, TaskItemStateMachine.CanTransition(from, TaskItemStatus.Closed));
    }

    [Fact]
    public void Closed_IsTerminal_NoOutgoingTransitions()
    {
        Assert.True(TaskItemStateMachine.IsTerminal(TaskItemStatus.Closed));
        Assert.Empty(TaskItemStateMachine.AllowedTargets(TaskItemStatus.Closed));
        foreach (var target in Enum.GetValues<TaskItemStatus>())
        {
            Assert.False(TaskItemStateMachine.CanTransition(TaskItemStatus.Closed, target));
        }
    }

    [Fact]
    public void SameState_IsNotATransition()
    {
        foreach (var status in Enum.GetValues<TaskItemStatus>())
        {
            Assert.False(TaskItemStateMachine.CanTransition(status, status));
        }
    }

    [Fact]
    public void NonTerminalStates_HaveTargets()
    {
        foreach (var status in Enum.GetValues<TaskItemStatus>().Where(s => s != TaskItemStatus.Closed))
        {
            Assert.NotEmpty(TaskItemStateMachine.AllowedTargets(status));
        }
    }
}
