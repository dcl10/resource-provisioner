namespace ResourceProvisioner.Api.Tests;

using Config;
using Extensions;
using Fixtures;
using Microsoft.Extensions.Configuration;
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

  [Fact]
  public void StartupValidation_Passes_WhenOptionsUseDefaults()
  {
    using var provider = BuildOptionsProvider([]);

    provider.GetRequiredService<IStartupValidator>().Validate();
  }

  [Theory]
  [InlineData("Anthropic:Effort", "extreme")]
  [InlineData("Anthropic:Model", "")]
  [InlineData("Limits:MaxActiveRequestsPerUser", "0")]
  [InlineData("Catalog:Root", "")]
  [InlineData("Workspaces:Root", "")]
  public void StartupValidation_Fails_WhenOptionIsInvalid(string key, string value)
  {
    using var provider = BuildOptionsProvider(new() { [key] = value });

    // IStartupValidator is what ValidateOnStart registers and the host runs at startup. Calling it directly avoids
    // starting a host: WebApplicationFactory disposes a host whose startup fails while it's still resolving
    // services, so the test saw ObjectDisposedException or OptionsValidationException depending on timing (#27).
    Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
  }

  private static ServiceProvider BuildOptionsProvider(Dictionary<string, string?> settings)
  {
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    return new ServiceCollection()
      .AddSingleton<IConfiguration>(configuration)
      .AddAppOptions()
      .BuildServiceProvider();
  }
}
