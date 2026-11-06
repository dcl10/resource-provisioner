namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using Models;
using Services.Requests;
using Xunit;

public class RunnerVariablesTests
{
  private static readonly Guid RequestId = Guid.Parse("3F2B8C1E-5D4A-4E6F-9A7B-1C2D3E4F5A6B");

  [Fact]
  public void NameSuffix_IsFirstEightLowercaseHexDigits_OfRequestId()
  {
    Assert.Equal("3f2b8c1e", RunnerVariables.NameSuffix(RequestId));
  }

  [Theory]
  [InlineData("8D1E2F3A-0000-4000-8000-000000000000", "8d1e2f3a")]
  [InlineData("ab-cd", "abcd")]
  [InlineData("user_1234567890", "user1234")]
  public void ShortOwner_KeepsFirstEightLowercaseLettersOrDigits(string ownerId, string expected)
  {
    Assert.Equal(expected, RunnerVariables.ShortOwner(ownerId));
  }

  [Fact]
  public void ShortOwner_Throws_WhenIdHasNoLettersOrDigits()
  {
    Assert.Throws<ArgumentException>(() => RunnerVariables.ShortOwner("--"));
  }

  [Fact]
  public void Tags_AreCommonRequestAndOwnerTags_InLowercase()
  {
    Assert.Equal(
      ["resource-provisioner", "rp-3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b", "rp-owner-8d1e2f3a"],
      RunnerVariables.Tags(RequestId, "8D1E2F3A-0000-4000-8000-000000000000"));
  }

  [Fact]
  public void For_ReturnsNameSuffixAndTags_AsTfvars()
  {
    var request = new ProvisioningRequest
    {
      Id = RequestId,
      OwnerId = "8d1e2f3a-0000-4000-8000-000000000000",
      Type = "droplet",
      Variables = "{}",
    };

    var expected = JsonNode.Parse("""
      { "rp_name_suffix": "3f2b8c1e",
        "rp_tags": ["resource-provisioner", "rp-3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b", "rp-owner-8d1e2f3a"] }
      """);
    Assert.True(JsonNode.DeepEquals(expected, RunnerVariables.For(request)));
  }
}
