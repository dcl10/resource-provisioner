namespace ResourceProvisioner.Api.Tests;

using Config;
using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class OptionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public void AnthropicOptions_DefaultsToSonnetAtLowEffort_WhenNotOverridden()
  {
    var options = factory.Services.GetRequiredService<IOptions<AnthropicOptions>>().Value;

    Assert.Equal("claude-sonnet-5-5", options.Model);
    Assert.Equal("low", options.Effort);
  }

  [Fact]
  public void LimitsOptions_BindsFromAppSettings_WhenHostStarts()
  {
    var options = factory.Services.GetRequiredService<IOptions<LimitsOptions>>().Value;

    Assert.Equal(3, options.MaxActiveRequestsPerUser);
    Assert.Equal(10, options.MaxActiveRequestsTotal);
    Assert.Equal(10, options.ChatMessagesPerMinute);
  }

  [Theory]
  [InlineData("Anthropic:Effort", "extreme")]
  [InlineData("Anthropic:Model", "")]
  [InlineData("Limits:MaxActiveRequestsPerUser", "0")]
  [InlineData("Workspaces:Root", "")]
  public void Startup_Fails_WhenOptionIsInvalid(string key, string value)
  {
    using var invalidFactory = factory.WithWebHostBuilder(b => b.UseSetting(key, value));

    Assert.Throws<OptionsValidationException>(() => invalidFactory.Services);
  }
}
