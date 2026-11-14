using System;
#nullable enable
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using WorkTime.Server.Services;
using Xunit;

namespace WorkTime.Server.Tests;

public class PeriodEditGuardTests
{
    private static PeriodEditGuard CreateGuard(int? windowDays)
    {
        var data = new Dictionary<string, string?>();
        if (windowDays.HasValue)
        {
            data["TimeEditing:EmployeeEditWindowDays"] = windowDays.Value.ToString();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        return new PeriodEditGuard(config);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Employee")]
    public void RecentRecord_IsEditableByAnyRole(string role)
    {
        var guard = CreateGuard(windowDays: 7);

        Assert.Null(guard.CheckEdit(role, DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public void Admin_CanEditOldRecord()
    {
        var guard = CreateGuard(windowDays: 7);

        Assert.Null(guard.CheckEdit("Admin", DateTime.UtcNow.AddDays(-400)));
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData("Manager")]
    public void OldRecord_IsBlockedForNonAdmin(string role)
    {
        var guard = CreateGuard(windowDays: 7);

        var error = guard.CheckEdit(role, DateTime.UtcNow.AddDays(-30));

        Assert.NotNull(error);
        Assert.Contains("7", error);
    }

    [Fact]
    public void RecordInsideWindow_IsEditable()
    {
        var guard = CreateGuard(windowDays: 7);

        // Чуть внутри окна — правка разрешена.
        Assert.Null(guard.CheckEdit("Employee", DateTime.UtcNow.AddDays(-6)));
    }

    [Fact]
    public void Missing_Config_UsesDefaultWindow()
    {
        var guard = CreateGuard(windowDays: null);

        Assert.Equal(7, guard.WindowDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveWindow_FallsBackToDefault(int windowDays)
    {
        var guard = CreateGuard(windowDays);

        Assert.Equal(7, guard.WindowDays);
    }

    [Fact]
    public void Custom_Window_IsRespected()
    {
        var guard = CreateGuard(windowDays: 30);

        Assert.Equal(30, guard.WindowDays);
        Assert.Null(guard.CheckEdit("Employee", DateTime.UtcNow.AddDays(-10)));
        Assert.NotNull(guard.CheckEdit("Employee", DateTime.UtcNow.AddDays(-60)));
    }

    [Fact]
    public void UnspecifiedKind_IsTreatedAsUtc()
    {
        var guard = CreateGuard(windowDays: 7);

        var oldUnspecified = DateTime.SpecifyKind(
            DateTime.UtcNow.AddDays(-30),
            DateTimeKind.Unspecified);

        Assert.NotNull(guard.CheckEdit("Employee", oldUnspecified));
    }
}
