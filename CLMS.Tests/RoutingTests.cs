using System.Reflection;
using CLMS.Web.Components;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace CLMS.Tests;

public sealed class RoutingTests
{
    [Fact]
    public void AppRoutesHaveOneComponentPerTemplate()
    {
        var duplicates = typeof(Routes).Assembly.GetTypes()
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>(inherit: false)
                .Select(route => new { Template = route.Template.Trim('/'), Component = type.FullName }))
            .GroupBy(route => route.Template, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"/{group.Key}: {string.Join(", ", group.Select(route => route.Component))}")
            .ToArray();

        Assert.Empty(duplicates);
    }
}
